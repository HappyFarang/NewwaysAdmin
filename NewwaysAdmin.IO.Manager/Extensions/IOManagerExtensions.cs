using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.IO.Manager
{
    public static class IOManagerExtensions
    {
        public static async Task<IServiceCollection> AddIOManagerAsync(
            this IServiceCollection services,
            string applicationName,
            string? configPath = null)
        {
            var loggerFactory = services.BuildServiceProvider()
                .GetRequiredService<ILoggerFactory>();

            var configLoader = new IOConfigLoader(
                loggerFactory.CreateLogger<IOConfigLoader>());

            var config = await configLoader.LoadConfigAsync(configPath);

            var options = new IOManagerOptions
            {
                LocalBaseFolder = config.LocalBaseFolder,
                ServerDefinitionsPath = config.ServerDefinitionsPath,
                ApplicationName = applicationName
            };

            services.AddSingleton(options);
            services.AddSingleton<IOManager>();

            return services;
        }
    }
}