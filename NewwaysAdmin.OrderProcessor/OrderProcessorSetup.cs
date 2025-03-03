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
        private const string APPLICATION_NAME = "PdfProcessor";

        public static IServiceCollection AddOrderProcessor(this IServiceCollection services)
        {
            services.AddLogging(builder => builder.AddConsole());

            // First, register configuration and basic services
            services.AddSingleton<IOManagerOptions>(sp => new IOManagerOptions
            {
                LocalBaseFolder = "C:/NewwaysData",
                ServerDefinitionsPath = "X:/NewwaysAdmin",
                ApplicationName = "PDFProcessor"
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

            services.AddSingleton<ConfigSyncTracker>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
                var options = sp.GetRequiredService<IOManagerOptions>();
                return new ConfigSyncTracker(options.LocalBaseFolder, logger);
            });

            // Then register the OrderProcessorLogger
            services.AddSingleton(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                return new OrderProcessorLogger(ioManager, "OrderLogs");
            });

            // Then register PdfProcessor
            services.AddSingleton<PdfProcessor>(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                var logger = sp.GetRequiredService<ILogger<PdfProcessor>>();
                var backupFolder = "C:/PDFtemp/PDFbackup"; // Configure this path as needed

                return new PdfProcessor(ioManager, backupFolder, logger);
            });
            // Finally register the PdfFileWatcher that depends on the above
            services.AddSingleton<PdfFileWatcher>(sp =>
            {
                var processor = sp.GetRequiredService<PdfProcessor>();
                var logger = sp.GetRequiredService<OrderProcessorLogger>();
                var watcher = new PdfFileWatcher("C:/PDFtemp", processor, logger);
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

                logger.LogInformation("Storage factory and IO Manager initialized");

                var configFolder = new StorageFolder
                {
                    Name = "PDFProcessor_Config",
                    Description = "PDFProcessor configuration storage",
                    Type = StorageType.Json,
                    Path = "Config/PdfProcessor",  // Note the lowercase 'p' in PdfProcessor
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 5,
                    CreatedBy = "PDFProcessor"
                };

                // Use the overloaded RegisterFolder method with application name
                storageFactory.RegisterFolder(configFolder, APPLICATION_NAME);
                logger.LogInformation("Registered PDFProcessor_Config folder");

                var scansFolder = new StorageFolder
                {
                    Name = "PDFProcessor_Scans",
                    Description = "Storage for PDF scan results",
                    Type = StorageType.Json,
                    Path = "Data/PdfProcessor/Scans",  // Note the lowercase 'p' in PdfProcessor
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 5,
                    CreatedBy = "PDFProcessor"
                };

                // Use the overloaded RegisterFolder method with application name
                storageFactory.RegisterFolder(scansFolder, APPLICATION_NAME);
                logger.LogInformation("Registered PDFProcessor_Scans folder");

                var logsFolder = new StorageFolder
                {
                    Name = "Logs", // This name is important - matches what the logger is trying to use
                    Description = "Application logs storage",
                    Type = StorageType.Json,
                    Path = "Logs", // Simple path structure
                    IsShared = true, // Logs are often shared across applications
                    CreateBackups = true,
                    MaxBackupCount = 10,
                    CreatedBy = "PDFProcessor"
                };

                // Use the overloaded RegisterFolder method with application name
                storageFactory.RegisterFolder(logsFolder, APPLICATION_NAME);
                logger.LogInformation("Registered Logs folder");

                // Make sure the PDF watch folder exists
                string pdfWatchFolder = "C:/PDFtemp";
                if (!Directory.Exists(pdfWatchFolder))
                {
                    Directory.CreateDirectory(pdfWatchFolder);
                    logger.LogInformation($"Created PDF watch folder: {pdfWatchFolder}");
                }

                // Get and start the file watcher
                var fileWatcher = serviceProvider.GetRequiredService<PdfFileWatcher>();
                fileWatcher.Start();
                logger.LogInformation("Started PDF file watcher for directory: {WatchFolder}", pdfWatchFolder);

                string pdfBackupFolder = "C:/PDFtemp/PDFbackup";
                if (!Directory.Exists(pdfBackupFolder))
                {
                    Directory.CreateDirectory(pdfBackupFolder);
                    logger.LogInformation($"Created PDF backup folder: {pdfBackupFolder}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize services: {Error}", ex.Message);
                throw;
            }
        }
    }
}