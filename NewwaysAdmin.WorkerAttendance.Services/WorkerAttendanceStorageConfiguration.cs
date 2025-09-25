// File: NewwaysAdmin.WorkerAttendance.Services/WorkerAttendanceStorageConfiguration.cs
// Purpose: Configure and register storage folders for Worker Attendance system

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.WorkerAttendance.Services
{
    public static class WorkerAttendanceStorageConfiguration
    {
        public static void ConfigureStorageFolders(EnhancedStorageFactory factory, ILogger logger)
        {
            logger.LogInformation("Configuring Worker Attendance storage folders...");

            // Helper method to safely register folders
            void RegisterFolderIfNotExists(StorageFolder folder)
            {
                try
                {
                    factory.RegisterFolder(folder, "WorkerAttendance");
                    logger.LogInformation("Registered folder: {FolderName}", folder.Name);
                }
                catch (Exception ex)
                {
                    // Folder might already exist, log but continue
                    logger.LogDebug("Folder {FolderName} registration issue: {Message}", folder.Name, ex.Message);
                }
            }

            // Workers folder - stores worker profiles and face data
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "Workers",
                Description = "Worker profiles with face recognition data",
                Type = StorageType.Json,      // Face encodings are json data so python can read it
                Path = "WorkerAttendance",      // Subfolder organization
                IsShared = true,                // Can sync with main NewwaysAdmin later
                CreateBackups = true,
                MaxBackupCount = 10
            });

            // AttendanceRecords folder - stores daily check-in/out records
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "AttendanceRecords",
                Description = "Daily worker check-in and check-out records",
                Type = StorageType.Json,        // Human-readable for debugging
                Path = "WorkerAttendance",      // Same subfolder
                IsShared = true,                // Sync with main app
                CreateBackups = true,
                MaxBackupCount = 50             // Keep more records
            });

            // AttendanceConfig folder - local system configuration
            RegisterFolderIfNotExists(new StorageFolder
            {
                Name = "AttendanceConfig",
                Description = "Camera settings, detection thresholds, and local config",
                Type = StorageType.Json,
                Path = "WorkerAttendance",
                IsShared = false,               // Local to each attendance station
                CreateBackups = true,
                MaxBackupCount = 5
            });

            logger.LogInformation("Worker Attendance storage configuration completed");
        }
    }
}