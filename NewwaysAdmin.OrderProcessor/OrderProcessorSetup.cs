using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.Configuration;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.OrderProcessor
{
    public static class OrderProcessorSetup
    {
        // Consistent application name for all registrations
        private const string APPLICATION_NAME = "PdfProcessor";

        public static IServiceCollection AddOrderProcessor(this IServiceCollection services)
        {
            services.AddLogging(builder => builder.AddConsole());

            // Configuration and shared services
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

            // IO Manager using the application name
            services.AddSingleton<IOManagerOptions>(sp => new IOManagerOptions
            {
                LocalBaseFolder = "C:/NewwaysData",
                ServerDefinitionsPath = "X:/NewwaysAdmin/Definitions",
                ApplicationName = APPLICATION_NAME
            });

            services.AddSingleton<IOManager>();
            services.AddSingleton<ConfigSyncTracker>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
                var ioManager = sp.GetRequiredService<IOManager>();
                return new ConfigSyncTracker(ioManager.LocalBaseFolder, logger);
            });

            // OrderProcessorLogger
            services.AddSingleton(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                return new OrderProcessorLogger(ioManager, "OrderLogs");
            });

            // PdfProcessor
            services.AddSingleton<PdfProcessor>(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                var logger = sp.GetRequiredService<ILogger<PdfProcessor>>();

                // Use a path within NewwaysData structure for consistency
                var backupFolder = Path.Combine(ioManager.LocalBaseFolder, "Data", "PdfProcessor", "Backup");
                return new PdfProcessor(ioManager, backupFolder, logger);
            });

            // PdfFileWatcher
            services.AddSingleton<PdfFileWatcher>(sp =>
            {
                var processor = sp.GetRequiredService<PdfProcessor>();
                var logger = sp.GetRequiredService<OrderProcessorLogger>();

                // Read the PDF watch folder from configuration if possible
                var machineConfig = sp.GetRequiredService<MachineConfigProvider>().LoadConfigAsync().GetAwaiter().GetResult();
                var pdfWatchFolder = "C:/PDFtemp"; // Default

                // Try to get path from config if available
                if (machineConfig.Apps.TryGetValue("PdfProcessor", out var appConfig) &&
                    appConfig.LocalPaths.TryGetValue("WatchFolder", out var configuredPath))
                {
                    pdfWatchFolder = configuredPath;
                }

                var watcher = new PdfFileWatcher(pdfWatchFolder, processor, logger);
                return watcher;
            });

            services.AddHostedService<FileSyncService>();

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

                // Register folder for configuration storage
                var configFolder = new StorageFolder
                {
                    Name = "PdfProcessor_Config",
                    Description = "PDF Processor configuration storage",
                    Type = StorageType.Json,
                    Path = "Config/PdfProcessor",
                    IsShared = true,
                    CreateBackups = true,
                    MaxBackupCount = 5,
                    CreatedBy = APPLICATION_NAME
                };

                storageFactory.RegisterFolder(configFolder, APPLICATION_NAME);
                logger.LogInformation("Registered PdfProcessor_Config folder");

                // Register folder for scan results
                var scansFolder = new StorageFolder
                {
                    Name = "PdfProcessor_Scans",
                    Description = "Storage for PDF scan results",
                    Type = StorageType.Json,
                    Path = "Data/PdfProcessor/Scans",
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 5,
                    CreatedBy = APPLICATION_NAME
                };

                storageFactory.RegisterFolder(scansFolder, APPLICATION_NAME);
                logger.LogInformation("Registered PdfProcessor_Scans folder");

                // Register shared logs folder
                var logsFolder = new StorageFolder
                {
                    Name = "Logs",
                    Description = "Application logs storage",
                    Type = StorageType.Json,
                    Path = "Logs",
                    IsShared = true,
                    CreateBackups = true,
                    MaxBackupCount = 10,
                    CreatedBy = APPLICATION_NAME
                };

                storageFactory.RegisterFolder(logsFolder, APPLICATION_NAME);
                logger.LogInformation("Registered Logs folder");

                // Create necessary directories
                await EnsureDirectoriesExistAsync(ioManager.LocalBaseFolder);

                // Start file watcher
                var fileWatcher = serviceProvider.GetRequiredService<PdfFileWatcher>();
                fileWatcher.Start();
                logger.LogInformation("Started PDF file watcher");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize services: {Error}", ex.Message);
                throw;
            }
        }

        private static async Task EnsureDirectoriesExistAsync(string baseFolder)
        {
            // Create standard directories
            var directories = new[]
            {
            Path.Combine(baseFolder, "Data", "PdfProcessor", "Scans"),
            Path.Combine(baseFolder, "Data", "PdfProcessor", "Backup"),
            Path.Combine(baseFolder, "Config", "PdfProcessor"),
            Path.Combine(baseFolder, "Logs"),
            Path.Combine(baseFolder, "Outgoing"),
            "C:/PDFtemp",
            "C:/PDFtemp/PDFbackup"
        };

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            await Task.CompletedTask;
        }
    }
}