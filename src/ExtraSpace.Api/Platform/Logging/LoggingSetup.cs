using System.Globalization;
using ExtraSpace.Api.Platform.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting;
using Serilog.Templates;

namespace ExtraSpace.Api.Platform.Logging;

public static class LoggingSetup
{
    // One JSON object per line (S0 spec §4.4). Plain field names: CloudWatch Logs Insights reserves names that start with '@'.
    private const string JsonTemplate =
        "{ {timestamp: UtcDateTime(@t), level: @l, message: @m, messageTemplate: @mt, exception: @x, traceId: @tr, spanId: @sp, ..@p} }\n";

    public static ITextFormatter CreateJsonFormatter() => new ExpressionTemplate(JsonTemplate, CultureInfo.InvariantCulture);

    public static WebApplicationBuilder AddPlatformLogging(this WebApplicationBuilder builder)
    {
        var environment = builder.Environment;
        var configuration = builder.Configuration;
        // preserveStaticLogger: each host keeps its own logger; test hosts in one process share Serilog's static one, and disposing one silenced the rest.
        builder.Services.AddSerilog((services, logger) =>
        {
            var app = services.GetRequiredService<IOptions<AppOptions>>().Value;
            logger.ReadFrom.Configuration(configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("app", "ExtraSpace")
                .Enrich.WithProperty("env", environment.EnvironmentName)
                .Enrich.WithProperty("role", app.Role.ToString().ToLowerInvariant())
                .Enrich.WithProperty("version", app.Version)
                .Enrich.With<FeatureEnricher>()
                .Enrich.With<SensitiveDataMasker>()
                .Enrich.With<FrameworkFieldsRemover>();

            if (environment.IsDevelopment())
            {
                logger.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} trace={TraceId}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture);
                if (configuration["Seq:Url"] is { Length: > 0 } seqUrl) logger.WriteTo.Seq(seqUrl);
            }
            else
            {
                logger.WriteTo.Console(CreateJsonFormatter());
            }
        }, preserveStaticLogger: true);
        return builder;
    }
}
