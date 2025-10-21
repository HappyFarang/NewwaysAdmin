// File: Mobile/NewwaysAdmin.Mobile/Infrastructure/Storage/MobileStorageManager.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.IOConfiguration;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.Mobile.Infrastructure.Storage
{
    /// <summary>
    /// Mobile storage manager that configures the EnhancedStorageFactory
    /// Follows the WorkerAttendance pattern - simple and direct
    /// </summary>
    public class MobileStorageManager
    {
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly ILogger<MobileStorageManager> _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private bool _isInitialized = false;

        public MobileStorageManager(
            EnhancedStorageFactory storageFactory,
            ILogger<MobileStorageManager> logger)
        {
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initialize mobile storage folders using the simple WorkerAttendance pattern
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_isInitialized)
                    return;

                // Set mobile base path BEFORE configuring folders
                var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
                StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

                _logger.LogInformation("Set mobile base directory to: {Path}", mobileBasePath);
                _logger.LogInformation("Configuring mobile storage folders...");

                // Now configure folders - they'll use the mobile path
                MobileStorageFolderConfiguration.ConfigureStorageFolders(_storageFactory);

                _isInitialized = true;
                _logger.LogInformation("Mobile storage manager initialized successfully");
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Get storage for a specific folder by name
        /// </summary>
        public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Storage manager not initialized. Call InitializeAsync first.");
            }

            return _storageFactory.GetStorage<T>(folderName);
        }

        /// <summary>
        /// Check if the storage system is initialized
        /// </summary>
        public bool IsInitialized => _isInitialized;

        public void Dispose()
        {
            _initLock?.Dispose();
        }
    }
}