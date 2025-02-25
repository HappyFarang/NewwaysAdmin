using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.Configuration;
using NewwaysAdmin.SharedModels.Config;

namespace NewwaysAdmin.OrderProcessor
{
    public static class OrderProcessorSetup
    {
        public static IServiceCollection AddOrderProcessor(this IServiceCollection services)
        {
            // Add machine config provider with correct path
            services.AddSingleton(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<MachineConfigProvider>>();
                return new MachineConfigProvider(logger);
            });

            // Add configuration providers
            services.AddSingleton<ConfigProvider>();
            services.AddSingleton<IOConfigLoader>();

            // Configure IOManager options first
            services.AddSingleton(sp =>
            {
                var configLoader = sp.GetRequiredService<IOConfigLoader>();
                var config = configLoader.LoadConfigAsync().GetAwaiter().GetResult();
                return new IOManagerOptions
                {
                    LocalBaseFolder = config.LocalBaseFolder,
                    ServerDefinitionsPath = config.ServerDefinitionsPath,
                    ApplicationName = "PDFProcessor"
                };
            });

            // Add ConfigSyncTracker with proper parameters
            services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOManagerOptions>();
                var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
                return new ConfigSyncTracker(options.LocalBaseFolder, logger);
            });

            // Add sync services
            services.AddHostedService<FileSyncService>();

            // Add IOManager
            services.AddSingleton<IOManager>();

            // Add application services
            services.AddSingleton(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                return new OrderProcessorLogger(ioManager, "OrderLogs");  // Changed to match your shared folder name
            });

            services.AddSingleton<PdfProcessor>(sp =>
            {
                var ioManager = sp.GetRequiredService<IOManager>();
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PdfProcessor>();
                var machineConfig = sp.GetRequiredService<MachineConfigProvider>()
                    .LoadConfigAsync().GetAwaiter().GetResult();
                var backupFolder = machineConfig.Apps["PDFProcessor"].LocalPaths["BackupFolder"];
                return new PdfProcessor(ioManager, backupFolder, logger);
            });

            services.AddSingleton<PdfFileWatcher>(sp =>
            {
                var machineConfig = sp.GetRequiredService<MachineConfigProvider>()
                    .LoadConfigAsync().GetAwaiter().GetResult();
                var watchFolder = machineConfig.Apps["PDFProcessor"].LocalPaths["WatchFolder"];
                var processor = sp.GetRequiredService<PdfProcessor>();
                var logger = sp.GetRequiredService<OrderProcessorLogger>();
                return new PdfFileWatcher(watchFolder, processor, logger);
            });

            return services;
        }

        public static async Task InitializeServicesAsync(IServiceProvider serviceProvider)
        {
            // Get the logger factory and create a logger specifically for initialization
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("OrderProcessor.Initialization");

            try
            {
                logger.LogInformation("Starting service initialization...");

                var machineConfig = serviceProvider.GetRequiredService<MachineConfigProvider>();
                await machineConfig.LoadConfigAsync(); // Ensure config exists
                logger.LogInformation("Machine config loaded");

                var fileWatcher = serviceProvider.GetRequiredService<PdfFileWatcher>();
                var processLogger = serviceProvider.GetRequiredService<OrderProcessorLogger>();

                fileWatcher.Start();
                logger.LogInformation("File watcher started");

                // Use console directly as a fallback
                Console.WriteLine("Application services initialized successfully");

                try
                {
                    await processLogger.LogAsync("Application services initialized successfully");
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Could not log to OrderProcessorLogger: {Error}", ex.Message);
                }
            }
            catch (FileNotFoundException ex)
            {
                logger.LogError("Configuration file not found: {Error}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError("Failed to initialize services: {Error}", ex.Message);
                throw;
            }
        }
    }
}