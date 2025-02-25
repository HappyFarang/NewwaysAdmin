using Microsoft.Extensions.DependencyInjection;
using System;

namespace NewwaysAdmin.IO.Manager.Extensions
{
    public static class IOManagerExtensions
    {
        public static IServiceCollection AddIOManager(
            this IServiceCollection services,
            Action<IOManagerOptions> configure)
        {
            var options = new IOManagerOptions
            {
                LocalBaseFolder = "C:/NewwaysData",
                ServerDefinitionsPath = "Z:/NewwaysAdmin/Definitions",
                ApplicationName = "Unknown"
            };

            configure(options);

            services.AddSingleton(options);
            services.AddSingleton<IOManager>();

            return services;
        }
    }
}
