﻿using NewwaysAdmin.Shared.IO;
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
        // Register required folders
        var salesFolder = new StorageFolder
        {
            Name = "Sales",
            Description = "Daily sales data storage",
            Type = StorageType.Binary,
            IsShared = true,
            Path = "PDFProcessor",  // Put this in the PDFProcessor subfolder
            CreatedBy = "OrderProcessor",
            CreateBackups = true,
            MaxBackupCount = 10
        };
    }
}