// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDataService.cs
// Purpose: Main service for orchestrating raw data + adjustments
// Returns exactly what the UI needs without business logic complexity

using NewwaysAdmin.WebAdmin.Models.Workers.Adjustments;
using NewwaysAdmin.WebAdmin.Services.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    /// <summary>
    /// Main service for getting worker data - orchestrates raw calculations + adjustments
    /// This is the single source of truth for all worker display data
    /// </summary>
    public class WorkerDataService
    {
        private readonly DailyAdjustmentStorage _adjustmentStorage;
        private readonly RawDataCalculator _rawCalculator;
        private readonly ILogger<WorkerDataService> _logger;

        public WorkerDataService(
            DailyAdjustmentStorage adjustmentStorage,
            RawDataCalculator rawCalculator,
            ILogger<WorkerDataService> logger)
        {
            _adjustmentStorage = adjustmentStorage;
            _rawCalculator = rawCalculator;
            _logger = logger;
        }

        /// <summary>
        /// Get raw attendance times for a specific attendance file
        /// Returns the pure calculations without any adjustments
        /// </summary>
        public async Task<RawTimeData> GetRawTimesAsync(string attendanceFileName)
        {
            try
            {
                var rawTimes = await _rawCalculator.LoadRawTimesAsync(attendanceFileName);

                _logger.LogDebug("Got raw times for {AttendanceFileName}", attendanceFileName);

                return rawTimes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting raw times for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Get adjustment times for a specific attendance file
        /// Returns only the adjusted values (null if no adjustment made)
        /// </summary>
        public async Task<AdjustmentTimeData> GetAdjustmentTimesAsync(string attendanceFileName)
        {
            try
            {
                var adjustment = await _adjustmentStorage.EnsureAndLoadAdjustmentAsync(attendanceFileName);

                _logger.LogDebug("Getting adjustment times for {AttendanceFileName}: HasAdjustments={HasAdjustments}",
                    attendanceFileName, adjustment.HasAdjustments);

                return new AdjustmentTimeData
                {
                    AttendanceFileName = attendanceFileName,
                    HasAdjustments = adjustment.HasAdjustments,
                    Description = adjustment.Description,
                    AdjustedSignIn = adjustment.AdjustedSignIn,
                    AdjustedSignOut = adjustment.AdjustedSignOut,
                    AdjustedOTSignIn = adjustment.AdjustedOTSignIn,
                    AdjustedOTSignOut = adjustment.AdjustedOTSignOut
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting adjustment times for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Get final display times - adjustments take precedence over raw times
        /// This is what the UI should show: adjusted time if exists, otherwise raw time
        /// </summary>
        public async Task<FinalTimeData> GetFinalTimesAsync(string attendanceFileName)
        {
            try
            {
                var rawTimes = await GetRawTimesAsync(attendanceFileName);
                var adjustmentTimes = await GetAdjustmentTimesAsync(attendanceFileName);

                _logger.LogDebug("Getting final times for {AttendanceFileName}", attendanceFileName);

                return new FinalTimeData
                {
                    AttendanceFileName = attendanceFileName,
                    HasAdjustments = adjustmentTimes.HasAdjustments,

                    // Use adjusted time if available, otherwise raw time
                    SignIn = adjustmentTimes.AdjustedSignIn ?? rawTimes.SignIn,
                    SignOut = adjustmentTimes.AdjustedSignOut ?? rawTimes.SignOut,
                    OTSignIn = adjustmentTimes.AdjustedOTSignIn ?? rawTimes.OTSignIn,
                    OTSignOut = adjustmentTimes.AdjustedOTSignOut ?? rawTimes.OTSignOut
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting final times for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Get complete data package - raw, adjustments, and final values all together
        /// Use this when you need to show comparisons or full data analysis
        /// </summary>
        public async Task<CompleteTimeData> GetCompleteDataAsync(string attendanceFileName)
        {
            try
            {
                var rawTimes = await GetRawTimesAsync(attendanceFileName);
                var adjustmentTimes = await GetAdjustmentTimesAsync(attendanceFileName);
                var finalTimes = await GetFinalTimesAsync(attendanceFileName);

                _logger.LogDebug("Getting complete data for {AttendanceFileName}", attendanceFileName);

                return new CompleteTimeData
                {
                    AttendanceFileName = attendanceFileName,
                    Raw = rawTimes,
                    Adjustments = adjustmentTimes,
                    Final = finalTimes
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete data for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Save an adjustment for an attendance file
        /// </summary>
        public async Task SaveAdjustmentAsync(string attendanceFileName, DailyAdjustmentRecord adjustment)
        {
            try
            {
                // Ensure the filename is set correctly
                adjustment.AttendanceFileName = attendanceFileName;

                await _adjustmentStorage.SaveAdjustmentAsync(adjustment);

                _logger.LogInformation("Saved adjustment for {AttendanceFileName}: HasAdjustments={HasAdjustments}",
                    attendanceFileName, adjustment.HasAdjustments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving adjustment for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Get a specific time field for display
        /// Returns adjusted time if exists, otherwise raw time
        /// Perfect for populating individual table cells
        /// </summary>
        public async Task<DateTime?> GetFinalTimeFieldAsync(string attendanceFileName, TimeField field)
        {
            try
            {
                var finalTimes = await GetFinalTimesAsync(attendanceFileName);

                var value = field switch
                {
                    TimeField.SignIn => finalTimes.SignIn,
                    TimeField.SignOut => finalTimes.SignOut,
                    TimeField.OTSignIn => finalTimes.OTSignIn,
                    TimeField.OTSignOut => finalTimes.OTSignOut,
                    _ => throw new ArgumentException($"Unknown time field: {field}")
                };

                _logger.LogDebug("Got {Field} for {AttendanceFileName}: {Value}",
                    field, attendanceFileName, value?.ToString("HH:mm") ?? "null");

                return value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting {Field} for {AttendanceFileName}", field, attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Get a specific raw time field (no adjustments applied)
        /// Perfect for statistics and analysis of unmodified data
        /// </summary>
        public async Task<DateTime?> GetRawTimeFieldAsync(string attendanceFileName, TimeField field)
        {
            try
            {
                var rawTimes = await GetRawTimesAsync(attendanceFileName);

                var value = field switch
                {
                    TimeField.SignIn => rawTimes.SignIn,
                    TimeField.SignOut => rawTimes.SignOut,
                    TimeField.OTSignIn => rawTimes.OTSignIn,
                    TimeField.OTSignOut => rawTimes.OTSignOut,
                    _ => throw new ArgumentException($"Unknown time field: {field}")
                };

                _logger.LogDebug("Got raw {Field} for {AttendanceFileName}: {Value}",
                    field, attendanceFileName, value?.ToString("HH:mm") ?? "null");

                return value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting raw {Field} for {AttendanceFileName}", field, attendanceFileName);
                throw;
            }
        }

        /// <summary>
        /// Get a specific adjustment time field (only the manual override)
        /// Returns null if no adjustment was made for that field
        /// </summary>
        public async Task<DateTime?> GetAdjustmentTimeFieldAsync(string attendanceFileName, TimeField field)
        {
            try
            {
                var adjustmentTimes = await GetAdjustmentTimesAsync(attendanceFileName);

                var value = field switch
                {
                    TimeField.SignIn => adjustmentTimes.AdjustedSignIn,
                    TimeField.SignOut => adjustmentTimes.AdjustedSignOut,
                    TimeField.OTSignIn => adjustmentTimes.AdjustedOTSignIn,
                    TimeField.OTSignOut => adjustmentTimes.AdjustedOTSignOut,
                    _ => throw new ArgumentException($"Unknown time field: {field}")
                };

                _logger.LogDebug("Got adjustment {Field} for {AttendanceFileName}: {Value}",
                    field, attendanceFileName, value?.ToString("HH:mm") ?? "null");

                return value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting adjustment {Field} for {AttendanceFileName}", field, attendanceFileName);
                throw;
            }
        }
        public async Task<DailyAdjustmentRecord> ClearAdjustmentsAsync(string attendanceFileName)
        {
            try
            {
                var clearedRecord = await _adjustmentStorage.ClearAdjustmentsAsync(attendanceFileName);

                _logger.LogInformation("Cleared adjustments for {AttendanceFileName}", attendanceFileName);

                return clearedRecord;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing adjustments for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }
    }

    // === ENUMS FOR FIELD ACCESS ===

    /// <summary>
    /// Enum for the four main time fields we track
    /// </summary>
    public enum TimeField
    {
        SignIn,
        SignOut,
        OTSignIn,
        OTSignOut
    }

    // === DATA MODELS FOR DIFFERENT VIEWS ===

    /// <summary>
    /// Raw time data from attendance file (no adjustments)
    /// </summary>
    public class RawTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public DateTime? SignIn { get; set; }
        public DateTime? SignOut { get; set; }
        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }
    }

    /// <summary>
    /// Adjustment time data (only what was manually changed)
    /// </summary>
    public class AdjustmentTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public bool HasAdjustments { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime? AdjustedSignIn { get; set; }
        public DateTime? AdjustedSignOut { get; set; }
        public DateTime? AdjustedOTSignIn { get; set; }
        public DateTime? AdjustedOTSignOut { get; set; }
    }

    /// <summary>
    /// Final time data for display (adjusted times take precedence)
    /// </summary>
    public class FinalTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public bool HasAdjustments { get; set; }
        public DateTime? SignIn { get; set; }
        public DateTime? SignOut { get; set; }
        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }
    }

    /// <summary>
    /// Complete data package - everything together for analysis
    /// </summary>
    public class CompleteTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public RawTimeData Raw { get; set; } = new();
        public AdjustmentTimeData Adjustments { get; set; } = new();
        public FinalTimeData Final { get; set; } = new();
    }
}