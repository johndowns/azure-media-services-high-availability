﻿using AmsHighAvailability.Services;
using AmsHighAvailability.Telemetry;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

[assembly: FunctionsStartup(typeof(AmsHighAvailability.Startup))]

namespace AmsHighAvailability
{
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            builder.Services.AddHttpClient();

            builder.Services.AddSingleton((s) =>
            {
                return new Random();
            });
            builder.Services.AddSingleton<ITelemetryInitializer, EnrichedTelemetryInitializer>();
            builder.Services.AddScoped<IMediaServicesJobService, MediaServicesJobService>();

            builder.Services.AddOptions<Configuration.Options>()
                .Configure<IConfiguration>((settings, configuration) =>
                {
                    configuration.GetSection("Options").Bind(settings);
                });
        }
    }
}
