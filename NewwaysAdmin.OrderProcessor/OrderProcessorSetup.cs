using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.OrderProcessor;
using NewwaysAdmin.Shared.Configuration;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.OrderProcessor
{
    public static class OrderProcessorSetup
    {
        public static IServiceCollection AddOrderProcessor(this IServiceCollection services)
        {
            services.AddLogging(builder => builder.AddConsole());

            services.AddSingleton<IOManagerOptions>(sp =>
            {
                return new IOManagerOptions
                {
                    LocalBaseFolder = "C:/NewwaysData",
                    ServerDefinitionsPath = "X:/NewwaysAdmin",
                    ApplicationName = "PDFProcessor"
                };
            });

            services.AddSingleton<EnhancedStorageFactory>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
                return new EnhancedStorageFactory(logger);
            });

            services.AddSingleton<MachineConfigProvider>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<MachineConfigProvider>>();
                return new MachineConfigProvider(logger);
            });

            services.AddSingleton<IOManager>();

            services.AddScoped<PdfProcessor>();

            // Add ConfigSyncTracker
            services.AddSingleton<ConfigSyncTracker>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
                var options = sp.GetRequiredService<IOManagerOptions>();
                return new ConfigSyncTracker(options.LocalBaseFolder, logger);
            });

            services.AddHostedService<FileSyncService>();

            services.AddSingleton(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                return new OrderProcessorLogger(ioManager, "OrderLogs");
            });

            return services;
        }

        public static async Task InitializeServicesAsync(IServiceProvider serviceProvider)
        {
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("OrderProcessor.Initialization");

            try
            {
                logger.LogInformation("Starting service initialization...");

                var ioManager = serviceProvider.GetRequiredService<IOManager>();
                var storageFactory = serviceProvider.GetRequiredService<EnhancedStorageFactory>();

                logger.LogInformation("Storage factory and IO Manager initialized");

                var configFolder = new StorageFolder
                {
                    Name = "PDFProcessor_Config",
                    Description = "PDFProcessor configuration storage",
                    Type = StorageType.Json,
                    Path = "Config/PDFProcessor",
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 5,
                    CreatedBy = "PDFProcessor"
                };
                storageFactory.RegisterFolder(configFolder);
                logger.LogInformation("Registered PDFProcessor_Config folder");

                var scansFolder = new StorageFolder
                {
                    Name = "PDFProcessor_Scans",
                    Description = "Storage for PDF scan results",
                    Type = StorageType.Json,
                    Path = "Data/PDFProcessor/Scans",
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 5,
                    CreatedBy = "PDFProcessor"
                };
                storageFactory.RegisterFolder(scansFolder);
                logger.LogInformation("Registered PDFProcessor_Scans folder");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize services: {Error}", ex.Message);
                throw;
            }
        }
    }
}