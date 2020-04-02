using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(AmsHighAvailability.Startup))]

namespace AmsHighAvailability
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddHttpClient();

            builder.Services.AddOptions<Configuration.Options>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection("Options").Bind(settings);
                });
        }
    }
}
