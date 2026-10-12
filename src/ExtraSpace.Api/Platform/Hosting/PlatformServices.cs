using System.Text.Json;
using System.Text.Json.Serialization;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Errors;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Platform.Tracing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi;

namespace ExtraSpace.Api.Platform.Hosting;

public static class PlatformServices
{
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
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
            .AddInterceptors(sp.GetRequiredService<TimestampsInterceptor>(), sp.GetRequiredService<SlowQueryInterceptor>()));
        services.AddDataProtection().SetApplicationName("ExtraSpace").PersistKeysToDbContext<AppDbContext>();
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        // Errors and MVC (S0 spec §4.1)
        ActivitySetup.EnsureRequestActivities();
        services.AddExceptionHandler<AppExceptionHandler>();
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsSetup.Customize);
        services.AddControllers(o => o.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ProblemDetailsSetup.ValidationResponse);
        // Microsoft.AspNetCore.OpenApi builds its schemas from these options, not MVC's, so the document must see the converter too.
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Pipeline (S0 spec §4.2)
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;          // the ALB appends the real client IP last; earlier entries are client-supplied
            o.KnownIPNetworks.Clear();   // only the ALB can reach the tasks (security groups), and its IPs change
            o.KnownProxies.Clear();
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
        }));
        return services;
    }
}
