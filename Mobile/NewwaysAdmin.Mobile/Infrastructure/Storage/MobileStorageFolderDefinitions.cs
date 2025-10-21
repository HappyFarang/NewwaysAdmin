// File: Mobile/NewwaysAdmin.Mobile/Infrastructure/Storage/MobileStorageFolderDefinitions.cs
using System.Collections.Immutable;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Mobile.Infrastructure.Storage
{
    /// <summary>
    /// Defines all storage folders for the mobile application
    /// Following the same pattern as WebAdmin for consistency
    /// </summary>
    public static class MobileStorageFolderDefinitions
    {
        public static ImmutableList<StorageFolder> GetAllFolders()
        {
            return ImmutableList.Create(

                // ===== AUTHENTICATION & SECURITY =====

                // Mobile Authentication - credentials and session data
                new StorageFolder
                {
                    Name = "MobileAuth",
                    Description = "Mobile app authentication credentials and session data",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Authentication",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = false, // Don't backup credentials for security
                    MaxBackupCount = 0
                },

                // Mobile Sessions - user session tracking
                new StorageFolder
                {
                    Name = "MobileSessions",
                    Description = "Mobile app session tracking and state",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Authentication",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = false, // Temporary data
                    MaxBackupCount = 0
                },

                // ===== USER DATA & PREFERENCES =====

                // Mobile User Settings - app preferences and configuration
                new StorageFolder
                {
                    Name = "MobileUserSettings",
                    Description = "User preferences, app settings, and configuration",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "UserData",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 5
                },

                // Mobile Cache - temporary data and cache
                new StorageFolder
                {
                    Name = "MobileCache",
                    Description = "Temporary data cache and offline storage",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Cache",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = false,
                    MaxBackupCount = 0
                },

                // ===== PHOTO & MEDIA =====

                // Photo Uploads - receipt/bill photos before upload
                new StorageFolder
                {
                    Name = "PhotoUploads",
                    Description = "Receipt and bill photos pending upload to server",
                    Type = StorageType.Binary,
                    IsShared = false,
                    Path = "Media/Photos",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 10 // Keep photos for recovery
                },

                // Photo Metadata - photo information and processing data
                new StorageFolder
                {
                    Name = "PhotoMetadata",
                    Description = "Photo metadata, processing status, and upload tracking",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Media",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 10
                },

                // ===== SYNC & OFFLINE =====

                // Offline Data - cached server data for offline use
                new StorageFolder
                {
                    Name = "OfflineData",
                    Description = "Cached server data for offline functionality",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Sync",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = false, // Cache data, can be regenerated
                    MaxBackupCount = 0
                },

                // Sync Queue - pending operations to sync with server
                new StorageFolder
                {
                    Name = "SyncQueue",
                    Description = "Pending operations waiting to sync with server",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Sync",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 5 // Important to preserve sync operations
                },

                // ===== CATEGORIES & BUSINESS DATA =====

                // Mobile Categories - cached/synced category data
                new StorageFolder
                {
                    Name = "MobileCategories",
                    Description = "Category data synchronized from server",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "BusinessData",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 10
                },

                // Transaction Links - links between photos and transactions
                new StorageFolder
                {
                    Name = "TransactionLinks",
                    Description = "Links between photos and business transactions",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "BusinessData",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 20 // Financial data - keep more backups
                },

                // ===== LOGS & DIAGNOSTICS =====

                // Mobile Logs - app logs and diagnostic data
                new StorageFolder
                {
                    Name = "MobileLogs",
                    Description = "Mobile app logs and diagnostic information",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Diagnostics",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = false,
                    MaxBackupCount = 0
                },

                // Error Reports - crash reports and error tracking
                new StorageFolder
                {
                    Name = "ErrorReports",
                    Description = "Crash reports and error diagnostic data",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "Diagnostics",
                    CreatedBy = "NewwaysAdmin.Mobile",
                    CreateBackups = true,
                    MaxBackupCount = 20 // Keep error reports for analysis
                }

                // Add new folders here as features are developed...
            );
        }

        /// <summary>
        /// Get a specific folder configuration by name
        /// </summary>
        public static StorageFolder? GetFolder(string name)
        {
            return GetAllFolders().FirstOrDefault(f => f.Name == name);
        }

        /// <summary>
        /// Get all folders for a specific path category
        /// </summary>
        public static IEnumerable<StorageFolder> GetFoldersByPath(string pathPrefix)
        {
            return GetAllFolders().Where(f => f.Path?.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>
        /// Get all essential folders needed for basic app functionality
        /// </summary>
        public static IEnumerable<StorageFolder> GetEssentialFolders()
        {
            return GetAllFolders().Where(f => f.Name is
                "MobileAuth" or
                "MobileSessions" or
                "MobileUserSettings" or
                "SyncQueue");
        }
    }
}