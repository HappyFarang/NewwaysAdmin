using Microsoft.Extensions.Logging;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Services.Auth;
using System.Collections.Immutable;

namespace NewwaysAdmin.WebAdmin.Infrastructure.Storage
{
    // Define all storage folders in one place
    public static class StorageFolderDefinitions
    {
        public static ImmutableList<StorageFolder> GetAllFolders()
        {
            return ImmutableList.Create(
                // User Management
                new StorageFolder
                {
                    Name = "Users",
                    Description = "User data and authentication",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "System",
                    CreateBackups = true,
                    MaxBackupCount = 5
                },

                // PDF Processor
                new StorageFolder
                {
                    Name = "Sales",
                    Description = "Daily sales data storage",
                    Type = StorageType.Binary,
                    IsShared = true,
                    Path = "PDFProcessor",
                    CreateBackups = true,
                    MaxBackupCount = 10
                },
                new StorageFolder
                {
                    Name = "Returns",
                    Description = "Product returns data storage",
                    Type = StorageType.Binary,
                    IsShared = true,
                    Path = "PDFProcessor",
                    CreateBackups = true,
                    MaxBackupCount = 10
                },
                  new StorageFolder
                  {
                      Name = "Navigation",
                      Description = "Navigation and menu settings",
                      Type = StorageType.Json,
                      IsShared = false,
                      Path = "System",
                      CreateBackups = true,
                      MaxBackupCount = 5
                  },
                  new StorageFolder
                  {
                      Name = "Sessions",
                      Description = "User session data",
                      Type = StorageType.Json,
                      IsShared = false,
                      Path = "System",
                      CreateBackups = false,  // Temporary data, no need for backups
                      MaxBackupCount = 0
                  },
                new StorageFolder
                {
                    Name = "Logs",
                    Description = "Operation logs",
                    Type = StorageType.Json,
                    IsShared = true,
                    Path = "PDFProcessor"
                }

                // Add new folders here as needed...
            );
        }

        // Helper method to get a specific folder configuration
        public static StorageFolder? GetFolder(string name)
        {
            return GetAllFolders().FirstOrDefault(f => f.Name == name);
        }
    }

    // Enhanced storage factory that manages folder registration
    public class StorageManager
    {
        private readonly EnhancedStorageFactory _factory;
        private readonly ILogger<StorageManager> _logger;

        public StorageManager(ILogger<StorageManager> logger)
        {
            _factory = new EnhancedStorageFactory(logger);
            _logger = logger;
        }

        public Task InitializeAsync()
        {
            try
            {
                // Register all our folders
                var folders = StorageFolderDefinitions.GetAllFolders();

                foreach (var folder in folders)
                {
                    try
                    {
                        _factory.RegisterFolder(folder);
                        _logger.LogInformation("Registered folder: {FolderName} at {Path}",
                            folder.Name,
                            string.IsNullOrEmpty(folder.Path) ? folder.Name : $"{folder.Path}/{folder.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to register folder: {FolderName}, continuing...", folder.Name);
                        // Log but continue - allow partial success
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize storage system");
                throw;
            }

            return Task.CompletedTask;
        }

        public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new()
        {
            var folder = StorageFolderDefinitions.GetFolder(folderName);
            if (folder == null)
            {
                throw new ArgumentException($"Folder '{folderName}' is not defined in StorageFolderDefinitions");
            }

            return _factory.GetStorage<T>(folderName);
        }

        public string GetDirectoryStructure() => _factory.GetDirectoryStructure();
    }

    // Extension method for service registration
    public static class StorageServiceExtensions
    {
        public static IServiceCollection AddStorageServices(this IServiceCollection services)
        {
            services.AddSingleton<StorageManager>();

            // Register your data providers
            services.AddScoped<UserInitializationService>();
            // Add other services as needed...

            return services;
        }
    }
}