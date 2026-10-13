using ExtraSpace.Api.Features.Admin;
using ExtraSpace.Api.Features.Identity;
using ExtraSpace.Api.Integrations.Email;
using ExtraSpace.Api.Platform.Data;
using ExtraSpace.Api.Platform.Health;
using ExtraSpace.Api.Platform.Hosting;
using ExtraSpace.Api.Platform.Http;
using ExtraSpace.Api.Platform.Logging;
using ExtraSpace.Api.Platform.Tracing;

ContainerExit.ExitOnUnhandledException();
var builder = WebApplication.CreateBuilder(args);
builder.AddPlatformLogging();
DatabaseConnection.Compose(builder.Configuration);
builder.Services.AddPlatform(builder.Configuration, builder.Environment);
builder.Services.AddEmail(builder.Configuration, builder.Environment);
builder.Services.AddIdentityFeature(builder.Configuration);
builder.Services.AddAdminFeature(builder.Configuration, builder.Environment);

var app = builder.Build();

// Order = S0 spec §4.2.
app.UseForwardedHeaders();                          // 1
app.UseMiddleware<RequestTraceMiddleware>();        // 2
app.UseMiddleware<RequestSummaryMiddleware>();      // 3
app.UseExceptionHandler();                          // 4
app.UseStatusCodePages();                           //   empty error responses become ProblemDetails
app.UseMiddleware<SecurityHeadersMiddleware>();     // 5
                                                    // 6: no HTTPS redirection, TLS ends at the ALB
app.UseAdminUiCompression();                        //   UI files only, never the API or the jobs dashboard
app.UseStaticFiles();                               // 7: the built admin UI (wwwroot/admin)
if (!app.Environment.IsProduction())                //    and Swagger UI outside production
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
                                                    // 14: feature endpoints, each Features/<X>/<X>Endpoints.cs; groups only, never handlers here
var api = app.MapGroup("/api/v1").RequireCustomer().WithEnumGuard();   // customer API; public routes opt out with .AllowAnonymous()
api.MapIdentityEndpoints();
var admin = app.MapGroup("/admin/api").RequireStaff().WithEnumGuard();   // admin API; cookie + CSRF + audit
admin.MapAdminEndpoints();
app.MapJobsDashboard();
foreach (var mapper in app.Services.GetServices<IEndpointMapper>()) mapper.Map(app);
app.MapHealthEndpoints();
if (!app.Environment.IsProduction()) app.MapOpenApi();
app.MapAdminSpa();                                  // 15: every other /admin page address gets index.html
app.Run();

public partial class Program;
