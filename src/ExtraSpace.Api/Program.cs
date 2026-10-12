using ExtraSpace.Api.Platform.Health;
using ExtraSpace.Api.Platform.Hosting;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Platform.Tracing;

var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformLogging();
builder.Services.AddPlatform(builder.Configuration);

var app = builder.Build();

// Order = S0 spec §4.2.
app.UseForwardedHeaders();                          // 1
app.UseMiddleware<RequestTraceMiddleware>();        // 2
app.UseMiddleware<RequestSummaryMiddleware>();      // 3
app.UseExceptionHandler();                          // 4
app.UseStatusCodePages();                           //   empty error responses become ProblemDetails
app.UseMiddleware<SecurityHeadersMiddleware>();     // 5
                                                    // 6: no HTTPS redirection, TLS ends at the ALB
if (!app.Environment.IsProduction())                // 7: static files (Swagger UI now, /admin in S0-5)
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "ExtraSpace API v1");
        o.RoutePrefix = "swagger";
    });
app.UseRouting();                                   // 8
app.UseMiddleware<RouteLogContextMiddleware>();
app.UseCors();                                      // 9
app.UseRateLimiter();                               // 10
app.UseMiddleware<AppVersionMiddleware>();          // 11
app.UseAuthentication();                            // 12
app.UseAuthorization();                             // 13
                                                    // 14: feature endpoints (S0-3 on), each Features/<X>/<X>Endpoints.cs
foreach (var mapper in app.Services.GetServices<IEndpointMapper>()) mapper.Map(app);
app.MapHealthEndpoints();
if (!app.Environment.IsProduction()) app.MapOpenApi();
                                                    // 15: SPA fallback /admin/{*path} in S0-5
app.Run();

public partial class Program;
