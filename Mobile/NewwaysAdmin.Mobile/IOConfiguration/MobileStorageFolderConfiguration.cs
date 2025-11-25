// File: Mobile/NewwaysAdmin.Mobile/IOConfiguration/MobileStorageFolderConfiguration.cs
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Mobile.IOConfiguration
{
    /// <summary>
    /// Mobile-specific storage folder configuration
    /// This ensures IOManager uses the correct mobile paths and folders
    /// </summary>
    public static class MobileStorageFolderConfiguration
    {
        public static void ConfigureStorageFolders(EnhancedStorageFactory factory)
        {
            // Helper method to safely register folders
            void RegisterFolderIfNotExists(StorageFolder folder)
            {
                try
                {
                    factory.RegisterFolder(folder);  // FIXED: Use single-parameter method
                }
                catch (StorageException ex) when (ex.Operation == StorageOperation.Validate)
                {
                    // Folder already exists, we can ignore this
                }
            }

            // ===== ESSENTIAL MOBILE FOLDERS =====

            // Mobile Authentication - credentials and session data
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "MobileAuth",
                Description = "Mobile app authentication credentials and session data",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Authentication",
                CreateBackups = false, // Don't backup credentials for security
                MaxBackupCount = 0
            });

            // Mobile Sessions - user session tracking
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "MobileSessions",
                Description = "Mobile app session tracking and state",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Authentication",
                CreateBackups = false, // Temporary data
                MaxBackupCount = 0
            });

            // Mobile User Settings - app preferences
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "MobileUserSettings",
                Description = "User preferences, app settings, and configuration",
                Type = StorageType.Json,
                IsShared = false,
                Path = "UserData",
                CreateBackups = true,
                MaxBackupCount = 5
            });

            // ===== CACHE AND TEMPORARY DATA =====

            // Mobile Cache - temporary data and cache
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "MobileCache",
                Description = "Temporary data cache and offline storage",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Cache",
                CreateBackups = false,
                MaxBackupCount = 0
            });

            // Sync Queue - pending operations to sync with server
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "SyncQueue",
                Description = "Pending operations waiting to sync with server",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Sync",
                CreateBackups = true,
                MaxBackupCount = 5 // Important to preserve sync operations
            });

            // ===== MEDIA AND PHOTOS =====

            // Photo Uploads - receipt/bill photos before upload
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "PhotoUploads",
                Description = "Receipt and bill photos pending upload to server",
                Type = StorageType.Binary,
                IsShared = false,
                Path = "Media/Photos",
                CreateBackups = true,
                MaxBackupCount = 10 // Keep photos for recovery
            });

            // Photo Metadata - photo information and processing data
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "PhotoMetadata",
                Description = "Photo metadata, processing status, and upload tracking",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Media",
                CreateBackups = true,
                MaxBackupCount = 10
            });

            // ===== BUSINESS DATA =====

            // Mobile Categories - cached/synced category data
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "MobileCategories",
                Description = "Category data synchronized from server",
                Type = StorageType.Json,
                IsShared = false,
                Path = "BusinessData",
                CreateBackups = true,
                MaxBackupCount = 10
            });

            // Transaction Links - links between photos and transactions
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "TransactionLinks",
                Description = "Links between photos and business transactions",
                Type = StorageType.Json,
                IsShared = false,
                Path = "BusinessData",
                CreateBackups = true,
                MaxBackupCount = 20 // Financial data - keep more backups
            });

            // ===== OFFLINE AND SYNC =====

            // Offline Data - cached server data for offline use
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "OfflineData",
                Description = "Cached server data for offline functionality",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Sync",
                CreateBackups = false, // Cache data, can be regenerated
                MaxBackupCount = 0
            });

            // ===== LOGS AND DIAGNOSTICS =====

            // Mobile Logs - app logs and diagnostic data
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "MobileLogs",
                Description = "Mobile app logs and diagnostic information",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Diagnostics",
                CreateBackups = false,
                MaxBackupCount = 0
            });

            // Error Reports - crash reports and error tracking
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "ErrorReports",
                Description = "Crash reports and error diagnostic data",
                Type = StorageType.Json,
                IsShared = false,
                Path = "Diagnostics",
                CreateBackups = true,
                MaxBackupCount = 20 // Keep error reports for analysis
            });
        }
    }
}