using System.Threading.RateLimiting;
using ExtraSpace.Api.Platform.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace ExtraSpace.Api.Features.Identity;

internal static class CustomerAuth
{
    public const string Scheme = "Customer";
    public const string Policy = "Customer";
    public const string CodeRateLimit = "auth-code";
    public const string VerifyRateLimit = "auth-verify";

    public static IServiceCollection AddIdentityFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CustomerAuthOptions>().Bind(configuration.GetSection(CustomerAuthOptions.Section))
            .Validate(o => CustomerAuthOptions.IsValidKey(o.SigningKey), "CustomerAuth:SigningKey must be base64 of at least 32 bytes")
            .ValidateOnStart();

        services.AddAuthentication().AddJwtBearer(Scheme);
        services.AddOptions<JwtBearerOptions>(Scheme).Configure<IOptions<CustomerAuthOptions>>((jwt, auth) =>
        {
            jwt.MapInboundClaims = false;   // keep `sub` as `sub`
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = auth.Value.Issuer,
                ValidAudience = auth.Value.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(auth.Value.SigningKeyBytes()),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = JwtRegisteredClaimNames.Sub,
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, p => p.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());

        services.AddSingleton<AccessTokens>();
        services.AddScoped<CustomerService>();
        services.AddSingleton<LoginCodeHasher>();
        services.AddScoped<TokenService>();
        services.AddScoped<LoginCodeService>();
        services.AddRecurringJob<IdentityHousekeepingJob>("IdentityHousekeeping", Cron.Daily(3));
        // Per IP, in memory, so per API task (plan decision 3); the per-email limits live in the database.
        services.Configure<RateLimiterOptions>(o =>
        {
            o.AddPolicy(CodeRateLimit, http => PerIpPerHour(http, 20));
            o.AddPolicy(VerifyRateLimit, http => PerIpPerHour(http, 30));
        });

        // Swagger UI's "Authorize" button: paste the accessToken from /api/v1/auth/code/verify.
        services.Configure<OpenApiOptions>("v1", o => o.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Customer access token from POST /api/v1/auth/code/verify",
            };
            return Task.CompletedTask;
        }).AddOperationTransformer((operation, context, _) =>
        {
            // Only routes that need a signed-in customer carry the lock (S0-4 admin cookie routes do not); /auth/* and /health/* stay open in Swagger UI.
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (metadata.OfType<IAuthorizeData>().Any(a => a.Policy == Policy) && !metadata.OfType<IAllowAnonymous>().Any())
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
            return Task.CompletedTask;
        }));
        return services;
    }

    private static RateLimitPartition<string> PerIpPerHour(HttpContext http, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromHours(1), QueueLimit = 0 });
}
