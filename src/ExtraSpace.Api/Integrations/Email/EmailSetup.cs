using Amazon.SimpleEmailV2;

namespace ExtraSpace.Api.Integrations.Email;

internal static class EmailSetup
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section))
            .Validate(o => o.Transport != EmailTransport.Ses || o.From.Length > 0, "Email:From must be set when Email:Transport is Ses")
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, HangfireEmailSender>();
        services.AddScoped<EmailJob>();

        var transport = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>()?.Transport ?? EmailTransport.Ses;
        switch (transport)
        {
            case EmailTransport.Ses:
                var aws = configuration.GetAWSOptions();
                aws.DefaultClientConfig.MaxErrorRetry = 0;          // the Hangfire job retries instead (5 attempts)
                aws.DefaultClientConfig.Timeout = TimeSpan.FromSeconds(10);
                services.AddDefaultAWSOptions(aws);
                services.AddAWSService<IAmazonSimpleEmailServiceV2>();
                services.AddSingleton<IEmailTransport, SesEmailTransport>();
                break;
            case EmailTransport.Log when environment.IsDevelopment():
                services.AddSingleton<IEmailTransport, LogEmailTransport>();
                break;
            case EmailTransport.Log:
                throw new InvalidOperationException("Email:Transport=Log prints login codes and is allowed in Development only.");
            case EmailTransport.None when environment.IsEnvironment("Testing"):
                break;
            case EmailTransport.None:
                throw new InvalidOperationException("Email:Transport=None sends nothing and is allowed in Testing only.");
        }
        return services;
    }
}
