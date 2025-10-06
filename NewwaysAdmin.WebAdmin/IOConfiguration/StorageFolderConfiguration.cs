//NewwaysAdmin.WebAdmin/IOConfiguration/StorageFolderConfiguration.cs

using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using System.Diagnostics;
using System;

namespace NewwaysAdmin.WebAdmin.IOConfiguration;

public static class StorageFolderConfiguration
{
    public static void ConfigureStorageFolders(EnhancedStorageFactory factory)
    {
        // Helper method to safely register folders
        void RegisterFolderIfNotExists(StorageFolder folder)
        {
            try
            {
                factory.RegisterFolder(folder);
            }
            catch (StorageException ex) when (ex.Operation == StorageOperation.Validate)
            {
                // Folder already exists, we can ignore this
            }
        }

        // Users folder for storing user data
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "Users",
            Description = "User account storage",
            Type = StorageType.Json,
            Path = string.Empty,
            IsShared = false,
            CreateBackups = true,
            MaxBackupCount = 5
        });

        // Sessions folder for active user sessions
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "Sessions",
            Description = "Active user sessions",
            Type = StorageType.Json,
            Path = string.Empty,
            IsShared = false,
            CreateBackups = false,
            MaxBackupCount = 0
        });

        // Navigation folder for menu items
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "Navigation",
            Description = "Navigation menu configuration",
            Type = StorageType.Json,
            Path = string.Empty,
            IsShared = true,
            CreateBackups = true,
            MaxBackupCount = 3
        });
        // Register required folders
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "Sales",
            Description = "Daily sales data storage",
            Type = StorageType.Binary,
            IsShared = true,
            Path = "PDFProcessor",  // Put this in the PDFProcessor subfolder
            CreatedBy = "OrderProcessor",
            CreateBackups = true,
            MaxBackupCount = 10
        });
        // OCR Patterns folder for storing pattern collections
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "OcrPatterns",
            Description = "OCR pattern collections and search patterns",
            Type = StorageType.Json,
            Path = "Ocr",
            IsShared = true,  // Other modules might use these patterns
            CreateBackups = true,
            MaxBackupCount = 10  // More backups since patterns are valuable
        });
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "ExternalFileIndexes",
            Description = "Index data for external file collections (NAS, network drives)",
            Type = StorageType.Json,
            Path = "FileIndexing/External",
            IsShared = true,
            CreateBackups = true,
            MaxBackupCount = 10
        });
        // Processed bank slip scan results(NEW)
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "BankSlipResults",
            Description = "Processed bank slip OCR scan results with automatic background processing",
            Type = StorageType.Binary,        // .bin files for fast access
            Path = "BankSlips/ProcessedResults",
            IsShared = true,                  // Multiple users can access
            CreateBackups = true,
            MaxBackupCount = 50,              // Keep more backups for financial data
            IndexFiles = true,                // Enable indexing for fast date range queries
            IndexedExtensions = [".bin"]      // Only index our scan result files
        });
        // WorkerAttendance folder for synced remote data
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "WorkerAttendance",
            Description = "Synced worker attendance and registration data from remote face scan machines",
            Type = StorageType.Json,
            Path = "WorkerAttendance",
            IsShared = true,                  // Multiple users can access attendance data
            CreateBackups = true,
            MaxBackupCount = 50,              // Keep many backups for employee data
            IndexFiles = true,                // Enable indexing for smart differential sync
            IndexedExtensions = [".json"],    // Index all JSON files in the folder
            PassThroughMode = true            // Key: files already serialized by remote IO Manager
        });
        // Worker Settings folder - stores individual worker configuration
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "WorkerSettings",
            Description = "Worker configuration including pay rates, expected hours, and meeting times",
            Type = StorageType.Json,
            Path = "WorkerManagement",
            IsShared = true,                  // Can be accessed by multiple users
            CreateBackups = true,
            MaxBackupCount = 10,              // Keep good backup history for payroll data
            IndexFiles = false                // Small number of files, no indexing needed
        });

        // Worker Weekly Data folder - stores weekly summaries for payment tracking
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "WorkerWeeklyData",
            Description = "Weekly worker activity summaries for payment calculation and history",
            Type = StorageType.Json,
            Path = "WorkerManagement/WeeklyData",
            IsShared = true,                  // Multiple users can access
            CreateBackups = true,
            MaxBackupCount = 50,              // Keep more backups for financial records
            IndexFiles = true,                // Enable indexing for fast week/year queries
            IndexedExtensions = [".json"]     // Index weekly summary files
        });
    }
}