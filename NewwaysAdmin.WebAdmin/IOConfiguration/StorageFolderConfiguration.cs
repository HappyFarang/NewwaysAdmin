using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

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

        // Sales folder (from PDF processor)
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

        // ===== BANK SLIP OCR FOLDERS =====

        // Bank Slip Collections (shared configuration)
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "BankSlip_Collections",
            Description = "Bank slip collection configurations",
            Type = StorageType.Json,
            Path = "BankSlips",
            IsShared = true, // Shared so admins can manage all collections
            CreatedBy = "BankSlipOCR",
            CreateBackups = true,
            MaxBackupCount = 10
        });

        // Bank Slip Data (per-user processed data)
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "BankSlip_Data",
            Description = "Processed bank slip data",
            Type = StorageType.Binary, // Using binary for efficient storage
            Path = "BankSlips",
            IsShared = false, // Per-user storage
            CreatedBy = "BankSlipOCR",
            CreateBackups = true,
            MaxBackupCount = 30 // Keep more backups for financial data
        });

        // Bank Slip Processing Logs
        RegisterFolderIfNotExists(new StorageFolder
        {
            Name = "BankSlip_Logs",
            Description = "Bank slip processing logs and audit trail",
            Type = StorageType.Json,
            Path = "BankSlips",
            IsShared = true, // Shared for audit purposes
            CreatedBy = "BankSlipOCR",
            CreateBackups = true,
            MaxBackupCount = 50
        });
    }
}