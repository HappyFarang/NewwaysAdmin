/// <summary>
/// Load adjustment record for an attendance file
/// If no adjustment file exists, returns a default record (but doesn't save it)
/// NOTE: Use EnsureAndLoadAdjustmentAsync() instead to guarantee file exists
/// </summary>// File: NewwaysAdmin.WebAdmin/Services/Workers/DailyAdjustmentStorage.cs
// Purpose: Simple service to save/load daily adjustment records

using System.Text.Json;
using NewwaysAdmin.WebAdmin.Models.Workers.Adjustments;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class DailyAdjustmentStorage
    {
        private readonly ILogger<DailyAdjustmentStorage> _logger;
        private readonly string _adjustmentsFolderPath;

        public DailyAdjustmentStorage(ILogger<DailyAdjustmentStorage> logger, IConfiguration configuration)
        {
            _logger = logger;

            // Get the WorkerAttendance folder path from configuration
            var workerAttendancePath = configuration["WorkerAttendance:FolderPath"] ?? "Data/WorkerAttendance";
            _adjustmentsFolderPath = Path.Combine(workerAttendancePath, "adjustments");

            // Ensure the adjustments folder exists
            Directory.CreateDirectory(_adjustmentsFolderPath);
        }

        /// <summary>
        /// Create and save a new default adjustment record for an attendance file
        /// </summary>
        public async Task<DailyAdjustmentRecord> CreateDefaultAdjustmentAsync(string attendanceFileName)
        {
            try
            {
                var defaultRecord = DailyAdjustmentRecord.CreateDefault(attendanceFileName);
                await SaveAdjustmentAsync(defaultRecord);

                _logger.LogDebug("Created default adjustment record for {AttendanceFileName}", attendanceFileName);

                return defaultRecord;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating default adjustment for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Ensure an adjustment record exists (create if missing), then load and return it
        /// This is the main method to use - always guarantees a record exists
        /// </summary>
        public async Task<DailyAdjustmentRecord> EnsureAndLoadAdjustmentAsync(string attendanceFileName)
        {
            try
            {
                // Check if adjustment file exists
                var exists = await AdjustmentExistsAsync(attendanceFileName);

                if (!exists)
                {
                    // Create and save default record first
                    _logger.LogDebug("Adjustment file does not exist for {AttendanceFileName}, creating default", attendanceFileName);
                    await CreateDefaultAdjustmentAsync(attendanceFileName);
                }

                // Now load and return the record (we know it exists)
                return await LoadAdjustmentAsync(attendanceFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring and loading adjustment for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }
        public async Task<DailyAdjustmentRecord> LoadAdjustmentAsync(string attendanceFileName)
        {
            try
            {
                var adjustmentFilePath = GetAdjustmentFilePath(attendanceFileName);

                if (!File.Exists(adjustmentFilePath))
                {
                    _logger.LogDebug("No adjustment file found for {AttendanceFileName}, returning default record", attendanceFileName);
                    return DailyAdjustmentRecord.CreateDefault(attendanceFileName);
                }

                var jsonContent = await File.ReadAllTextAsync(adjustmentFilePath);
                var adjustment = JsonSerializer.Deserialize<DailyAdjustmentRecord>(jsonContent);

                if (adjustment == null)
                {
                    _logger.LogWarning("Failed to deserialize adjustment file: {FilePath}", adjustmentFilePath);
                    return DailyAdjustmentRecord.CreateDefault(attendanceFileName);
                }

                // Ensure the filename is set (in case it was missing from saved data)
                adjustment.AttendanceFileName = attendanceFileName;

                _logger.LogDebug("Loaded adjustment for {AttendanceFileName}: HasAdjustments={HasAdjustments}",
                    attendanceFileName, adjustment.HasAdjustments);

                return adjustment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading adjustment for {AttendanceFileName}", attendanceFileName);
                return DailyAdjustmentRecord.CreateDefault(attendanceFileName);
            }
        }

        /// <summary>
        /// Save adjustment record to file
        /// </summary>
        public async Task SaveAdjustmentAsync(DailyAdjustmentRecord adjustment)
        {
            try
            {
                if (string.IsNullOrEmpty(adjustment.AttendanceFileName))
                {
                    throw new ArgumentException("AttendanceFileName must be set before saving");
                }

                var adjustmentFilePath = GetAdjustmentFilePath(adjustment.AttendanceFileName);

                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true  // Pretty format for debugging
                };

                var jsonContent = JsonSerializer.Serialize(adjustment, jsonOptions);
                await File.WriteAllTextAsync(adjustmentFilePath, jsonContent);

                _logger.LogInformation("Saved adjustment for {AttendanceFileName}: HasAdjustments={HasAdjustments}",
                    adjustment.AttendanceFileName, adjustment.HasAdjustments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving adjustment for {AttendanceFileName}", adjustment.AttendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Check if an adjustment file exists for the given attendance file
        /// </summary>
        public Task<bool> AdjustmentExistsAsync(string attendanceFileName)
        {
            try
            {
                var adjustmentFilePath = GetAdjustmentFilePath(attendanceFileName);
                return Task.FromResult(File.Exists(adjustmentFilePath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking adjustment existence for {AttendanceFileName}", attendanceFileName);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Clear all adjustments from a record (effectively reverting to raw data)
        /// Returns the cleared record and saves it
        /// </summary>
        public async Task<DailyAdjustmentRecord> ClearAdjustmentsAsync(DailyAdjustmentRecord record)
        {
            try
            {
                // Keep the filename but clear all adjustments
                var attendanceFileName = record.AttendanceFileName;

                // Clear the record
                record.ClearAdjustments();

                // Restore the filename (ClearAdjustments might have cleared it)
                record.AttendanceFileName = attendanceFileName;

                // Save the cleared record
                await SaveAdjustmentAsync(record);

                _logger.LogInformation("Cleared adjustments for {AttendanceFileName}", attendanceFileName);

                return record;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing adjustments for {AttendanceFileName}", record.AttendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Clear adjustments by attendance filename (loads, clears, saves)
        /// </summary>
        public async Task<DailyAdjustmentRecord> ClearAdjustmentsAsync(string attendanceFileName)
        {
            var record = await LoadAdjustmentAsync(attendanceFileName);
            return await ClearAdjustmentsAsync(record);
        }

        /// <summary>
        /// Get all adjustment files in the adjustments folder
        /// Returns attendance filenames that have adjustments
        /// </summary>
        public Task<List<string>> GetAllAdjustedAttendanceFilesAsync()
        {
            try
            {
                var adjustmentFiles = Directory.GetFiles(_adjustmentsFolderPath, "adjustments_*.json");

                var attendanceFileNames = adjustmentFiles
                    .Select(Path.GetFileName)
                    .Where(fileName => fileName != null)
                    .Select(fileName => fileName!.Replace("adjustments_", "").Replace(".json", ".json.json"))
                    .ToList();

                _logger.LogDebug("Found {Count} attendance files with adjustments", attendanceFileNames.Count);

                return Task.FromResult(attendanceFileNames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting adjusted attendance files");
                return Task.FromResult(new List<string>());
            }
        }

        // === PRIVATE HELPERS ===

        /// <summary>
        /// Get the full file path for an adjustment file
        /// </summary>
        private string GetAdjustmentFilePath(string attendanceFileName)
        {
            var adjustmentFileName = DailyAdjustmentRecord.GetFileName(attendanceFileName);
            return Path.Combine(_adjustmentsFolderPath, adjustmentFileName);
        }
    }
}