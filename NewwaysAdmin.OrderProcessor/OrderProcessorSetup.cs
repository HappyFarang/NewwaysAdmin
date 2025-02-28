using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.OrderProcessor;
using NewwaysAdmin.Shared.Configuration;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.Config;

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

        // Register EnhancedStorageFactory
        services.AddSingleton<EnhancedStorageFactory>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<EnhancedStorageFactory>();
            return new EnhancedStorageFactory(logger);
        });

        // Add configuration providers with the correct storage factory
        services.AddSingleton<ConfigProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<ConfigProvider>();
            var storageFactory = sp.GetRequiredService<EnhancedStorageFactory>();
            return new ConfigProvider(logger, storageFactory);
        });

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
            return new OrderProcessorLogger(ioManager, "OrderLogs");
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
    private static async Task EnsurePlatformConfigFileExists(ILogger logger)
    {
        try
        {
            // Get the paths
            string configPath = Path.Combine("C:/NewwaysData", "Config", "PDFProcessor");
            string platformFile = Path.Combine(configPath, "platform.json");

            logger.LogInformation("Checking for platform config at {Path}", platformFile);

            // If file exists, we're good
            if (File.Exists(platformFile))
            {
                logger.LogInformation("Platform config file exists");
                return;
            }

            // Check if directory exists, create if not
            if (!Directory.Exists(configPath))
            {
                logger.LogInformation("Creating config directory: {Path}", configPath);
                Directory.CreateDirectory(configPath);
            }

            // Check for the file in alternate locations
            string[] possibleLocations = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "platform.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "platform.json"),
            "C:/PDFtemp/platform.json",
            "C:/PDFtemp/Config/platform.json",
            "C:/NewwaysAdmin/PDFProcessor/platform.json"
        };

            foreach (var loc in possibleLocations)
            {
                logger.LogInformation("Checking alternate location: {Path}", loc);

                if (File.Exists(loc))
                {
                    logger.LogInformation("Found platform config at {Path}, copying to {DestPath}", loc, platformFile);

                    // Copy file to the correct location
                    await Task.Run(() => File.Copy(loc, platformFile, true));
                    return;
                }
            }

            logger.LogWarning("Could not find platform.json in any expected location");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking/copying platform config file");
        }
    }
    public static async Task InitializeServicesAsync(IServiceProvider serviceProvider)
    {
        // Get the logger factory and create a logger specifically for initialization
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("OrderProcessor.Initialization");

        try
        {
            logger.LogInformation("Starting service initialization...");

            // Ensure the storage factory is properly initialized
            var storageFactory = serviceProvider.GetRequiredService<EnhancedStorageFactory>();
            logger.LogInformation("Storage factory initialized");

            // Ensure storage configuration exists early on
            var ioManager = serviceProvider.GetRequiredService<IOManager>();

            // Register the PDFProcessor_Config folder if it doesn't exist
            var folder = new StorageFolder
            {
                Name = "PDFProcessor_Config",
                Description = "PDFProcessor configuration storage",
                Type = StorageType.Json,
                Path = "Config/PDFProcessor", // This is the physical path where files will be stored
                IsShared = false,
                CreateBackups = true,
                MaxBackupCount = 5,
                CreatedBy = "System"
            };

            // The RegisterFolder method handles it if the folder already exists
            storageFactory.RegisterFolder(folder);
            logger.LogInformation("Registered PDFProcessor_Config folder");

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