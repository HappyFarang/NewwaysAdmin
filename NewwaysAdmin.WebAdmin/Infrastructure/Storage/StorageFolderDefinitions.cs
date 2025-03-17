using Microsoft.Extensions.Logging;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Services.Auth;
using System.Collections.Immutable;
using NewwaysAdmin.IO.Manager;

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
        private readonly IOManager _ioManager;
        private readonly ILogger<StorageManager> _logger;
        private readonly Dictionary<string, IDataStorageBase> _storageCache = new();
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isInitialized = false;

        public StorageManager(
            IOManager ioManager,
            ILogger<StorageManager> logger)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                // Register all folders defined in StorageFolderDefinitions
                var folders = StorageFolderDefinitions.GetAllFolders();

                foreach (var folder in folders)
                {
                    try
                    {
                        // For initialization, use the EnhancedStorageFactory directly
                        // to register folders - this only happens once at startup
                        var factory = new EnhancedStorageFactory(_logger);
                        factory.RegisterFolder(folder, "NewwaysAdmin.WebAdmin");

                        _logger.LogInformation("Registered folder: {FolderName} at {Path}",
                            folder.Name,
                            string.IsNullOrEmpty(folder.Path) ? folder.Name : $"{folder.Path}/{folder.Name}");
                    }
                    catch (StorageException ex) when (ex.Operation == StorageOperation.Validate)
                    {
                        // Folder already exists, this is fine
                        _logger.LogDebug("Folder {FolderName} already registered", folder.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error registering folder {FolderName}, continuing...", folder.Name);
                    }
                }

                _isInitialized = true;
                _logger.LogInformation("StorageManager initialized successfully");
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<IDataStorage<T>> GetStorage<T>(string folderName) where T : class, new()
        {
            // Ensure storage is initialized
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            // Check if the folder is defined
            var folder = StorageFolderDefinitions.GetFolder(folderName);
            if (folder == null)
            {
                throw new ArgumentException($"Folder '{folderName}' is not defined in StorageFolderDefinitions");
            }

            // Check for cached storage instance
            string cacheKey = $"{folderName}_{typeof(T).Name}";

            if (_storageCache.TryGetValue(cacheKey, out var cachedStorage))
            {
                return (IDataStorage<T>)cachedStorage;
            }

            // Get from IOManager
            try
            {
                var storage = await _ioManager.GetStorageAsync<T>(folderName);

                // Cache for future use
                _storageCache[cacheKey] = storage;

                return storage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting storage for folder {Folder}", folderName);
                throw;
            }
        }

        // Synchronous wrapper for use in constructors
        public IDataStorage<T> GetStorageSync<T>(string folderName) where T : class, new()
        {            
            return _ioManager.GetStorageAsync<T>(folderName).GetAwaiter().GetResult();
        }
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