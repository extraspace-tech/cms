using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SimpleSystemsManagement;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Platform.Jobs;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Platform.Tracing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;

namespace ExtraSpace.Api.Platform.Hosting;

public static class PlatformServices
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<AppOptions>().Bind(configuration.GetSection(AppOptions.Section))
            .Validate(o => o.MinVersion.Values.All(v => AppVersion.Parse(v) is not null), "App:MinVersion values must be versions like 1.4.0")
            .ValidateOnStart();
        services.Configure<LoggingOptions>(configuration.GetSection(LoggingOptions.Section));
        services.AddSingleton(TimeProvider.System);

        // Data (S0 spec §5)
        services.AddSingleton<TimestampsInterceptor>();
        services.AddSingleton<SlowQueryInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not set."))
            .UseSnakeCaseNamingConvention()
            // AppExceptionHandler logs every failure once; EF's own Error lines would double it (and log 409 races as errors).
            .ConfigureWarnings(w => w.Log(
                (RelationalEventId.CommandError, LogLevel.Debug),
                (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                (CoreEventId.QueryIterationFailed, LogLevel.Debug),
                (RelationalEventId.ConnectionError, LogLevel.Debug),
                (RelationalEventId.TransactionError, LogLevel.Debug)))
            .AddInterceptors(sp.GetRequiredService<TimestampsInterceptor>(), sp.GetRequiredService<SlowQueryInterceptor>())
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));   // feature interceptors (Admin audit), after the stamps
        // The key ring signs admin cookies and encrypts email jobs: on AWS it lives in SSM (SecureString, KMS), never in the database.
        var keyRing = services.AddDataProtection().SetApplicationName("ExtraSpace");
        if (configuration["DataProtection:SsmPath"] is { Length: > 0 } ssmPath)
        {
            // Own client: the SES setup turns off SDK retries on the shared default AWS options.
            services.TryAddSingleton(_ => configuration.GetAWSOptions().CreateServiceClient<IAmazonSimpleSystemsManagement>());
            keyRing.PersistKeysToAWSSystemsManager(ssmPath);
        }
        else if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            keyRing.PersistKeysToDbContext<AppDbContext>();
        else
            throw new InvalidOperationException("DataProtection:SsmPath must be set outside Development and Testing.");
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing") && configuration.GetValue("Database:RequireMigrated", true))
            services.AddHostedService<MigrationGuard>();

        // Jobs (S0 spec §7)
        services.AddPlatformJobs(configuration);

        // Errors and endpoints (S0 spec §4.1)
        ActivitySetup.EnsureRequestActivities();
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddSingleton<IProblemDetailsWriter, JsonProblemWriter>(); // before AddProblemDetails: the first writer that accepts wins
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsSetup.Customize);
        services.AddValidation();
        // The default is true only in Development; true everywhere sends every bad body through AppExceptionHandler.
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        // The one JSON option set: endpoint responses and the OpenAPI document both read it.
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            // The web default also reads "5" as 5, and the OpenAPI document then types every integer as integer | string.
            o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });

        // Pipeline (S0 spec §4.2)
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;          // the ALB appends the real client IP last; earlier entries are client-supplied
            o.KnownIPNetworks.Clear();   // only the ALB can reach the tasks (security groups), and its IPs change
            o.KnownProxies.Clear();
        });
        services.Configure<StaticFileOptions>(AdminSpa.ConfigureStaticFiles);
        // Static text only. JSON is left alone: API responses carry tokens (BREACH).
        services.AddResponseCompression(o =>
        {
            o.EnableForHttps = true;
            o.MimeTypes = ["text/html", "text/css", "text/javascript", "application/javascript", "image/svg+xml"];
        });
        var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(RequestTrace.HeaderName, "Retry-After")));
        services.AddRateLimiter(RateLimitSetup.Configure);
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddOpenApi(o => o.AddSchemaTransformer((schema, context, _) =>
        {
            // .NET 10 documents string enums as a bare `enum` list, shared by every use and built from the first one
            // met (maybe `T?`, which adds null); name the type and drop null so generated clients see strings.
            var type = context.JsonTypeInfo.Type;
            if ((Nullable.GetUnderlyingType(type) ?? type).IsEnum && schema.Type is null)
            {
                schema.Type = JsonSchemaType.String;
                if (schema.Enum is not null)
                    schema.Enum = schema.Enum.Where(e => e is not null && e.GetValueKind() != JsonValueKind.Null).ToList();
            }
            return Task.CompletedTask;
        }).AddRequiredNotNull().AddProblemResponses());
        return services;
    }
}
