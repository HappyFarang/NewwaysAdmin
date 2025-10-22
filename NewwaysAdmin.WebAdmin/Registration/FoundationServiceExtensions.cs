// File: NewwaysAdmin.WebAdmin/Registration/FoundationServiceExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.Configuration;
using NewwaysAdmin.SharedModels.Config;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class FoundationServiceExtensions
    {
        public static IServiceCollection AddFoundationServices(this IServiceCollection services, IConfiguration configuration)
        {
            // IOManager Configuration - FOUNDATIONAL DEPENDENCY
            var ioManagerOptions = new IOManagerOptions
            {
                LocalBaseFolder = configuration.GetValue<string>("IOManager:LocalBaseFolder")
                    ?? @"C:\NewwaysData",
                ServerDefinitionsPath = configuration.GetValue<string>("IOManager:ServerDefinitionsPath")
                    ?? "X:/NewwaysAdmin/Definitions",
                ApplicationName = "NewwaysAdmin.WebAdmin"
            };
            services.AddSingleton(ioManagerOptions);

            // EnhancedStorageFactory - IOManager depends on this
            services.AddSingleton<EnhancedStorageFactory>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger<EnhancedStorageFactory>();
                return new EnhancedStorageFactory(logger);
            });

            // MachineConfigProvider - IOManager depends on this
            services.AddSingleton<MachineConfigProvider>();

            // IOManager - Everything depends on this
            services.AddSingleton<IOManager>();

            // ConfigSyncTracker - Depends on IOManager
            services.AddSingleton<ConfigSyncTracker>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
                var ioManager = sp.GetRequiredService<IOManager>();
                return new ConfigSyncTracker(ioManager.LocalBaseFolder, logger);
            });

            // ConfigProvider - Depends on IOManager
            services.AddSingleton<ConfigProvider>(sp =>
            {
                var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                var logger = loggerFactory.CreateLogger<ConfigProvider>();
                var ioManager = sp.GetRequiredService<IOManager>();
                return new ConfigProvider(logger, ioManager);
            });

            return services;
        }
    }
}