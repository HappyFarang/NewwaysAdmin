using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.WebAdmin.Services.Auth;

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

                // Navigation
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

                // Sessions
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

                // Sales Data
                new StorageFolder
                {
                    Name = "Sales",
                    Description = "Sales data and analytics",
                    Type = StorageType.Json,
                    Path = "Sales",
                    IsShared = true,
                    CreateBackups = true,
                    MaxBackupCount = 10
                },

                // Bank Slip Collections (admin-managed)
                new StorageFolder
                {
                    Name = "BankSlip_Collections",
                    Description = "Bank slip collections managed by admin",
                    Type = StorageType.Binary, // Using binary for efficiency
                    Path = "BankSlips",
                    IsShared = false, // Admin-only storage
                    CreateBackups = true,
                    MaxBackupCount = 10
                },

                // Bank Slip Data (per-user processed data)
                new StorageFolder
                {
                    Name = "BankSlip_Data",
                    Description = "Processed bank slip data per user",
                    Type = StorageType.Binary, // Using binary for efficient storage
                    Path = "BankSlips",
                    IsShared = false, // Per-user storage
                    CreateBackups = true,
                    MaxBackupCount = 30 // Keep more backups for financial data
                },

                // Bank Slip Processing Logs (shared for audit)
                new StorageFolder
                {
                    Name = "BankSlip_Logs",
                    Description = "Bank slip processing logs and audit trail",
                    Type = StorageType.Json,
                    Path = "BankSlips",
                    IsShared = true, // Shared for audit purposes
                    CreateBackups = true,
                    MaxBackupCount = 50
                },

                // ===== GOOGLE SHEETS FOLDERS =====

                // Google Sheets User Configurations
                new StorageFolder
                {
                    Name = "GoogleSheets_UserConfigs",
                    Description = "User checkbox preferences for Google Sheets exports",
                    Type = StorageType.Json,
                    Path = "GoogleSheets",
                    IsShared = false, // Per-admin storage, but accessible by admin interface
                    CreateBackups = true,
                    MaxBackupCount = 10
                },

                // Google Sheets Custom Column Libraries
                new StorageFolder
                {
                    Name = "GoogleSheets_CustomColumns",
                    Description = "User custom column templates and libraries",
                    Type = StorageType.Json,
                    Path = "GoogleSheets",
                    IsShared = false, // Per-user custom columns
                    CreateBackups = true,
                    MaxBackupCount = 15
                },

                // Google Sheets Export History (optional - for tracking exports)
                new StorageFolder
                {
                    Name = "GoogleSheets_ExportHistory",
                    Description = "History of Google Sheets exports for audit trail",
                    Type = StorageType.Json,
                    Path = "GoogleSheets",
                    IsShared = true, // Shared for audit purposes
                    CreateBackups = true,
                    MaxBackupCount = 30
                },

                new StorageFolder
                {
                    Name = "GoogleSheets_EmailSettings",
                    Description = "User email addresses for Google Sheets ownership transfer",
                    Type = StorageType.Json,
                    Path = "GoogleSheets",
                    IsShared = false,
                    CreateBackups = true,
                    MaxBackupCount = 5
                },

                new StorageFolder
                {
                    Name = "GoogleSheets_Templates",
                    Description = "Sheet layout templates for different data types",
                    Type = StorageType.Json,
                    Path = "GoogleSheets",
                    IsShared = true, // Templates are shared across all users
                    CreateBackups = true,
                    MaxBackupCount = 15
                },

                // Google Sheets Admin Configurations
                new StorageFolder
                {
                    Name = "GoogleSheets_AdminConfigs",
                    Description = "Admin-level Google Sheets configurations",
                    Type = StorageType.Json,
                    Path = "GoogleSheets",
                    IsShared = false, // Admin-only storage
                    CreateBackups = true,
                    MaxBackupCount = 10
                },

                // Ocr Patterns
                new StorageFolder
                {
                    Name = "OcrPatterns",
                    Description = "OCR pattern collections and search patterns",
                    Type = StorageType.Json,
                    IsShared = true,  // Other modules might use these patterns
                    Path = "Ocr",
                    CreateBackups = true,
                    MaxBackupCount = 10  // More backups since patterns are valuable
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
        public static StorageFolder Security { get; } = new StorageFolder
        {
            Name = "Security",
            Path = "Security",
            Description = "DoS protection and security monitoring data",
            Type = StorageType.Json,
            IsShared = false,
            CreatedBy = "NewwaysAdmin.WebAdmin",
            CreateBackups = true,
            MaxBackupCount = 10
        };
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