// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDataService.cs
// Purpose: COMPLETED - Unified orchestration service for worker attendance data
// Returns adjusted values when available, raw values otherwise

using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WorkerAttendance.Models;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerDataService
    {
        private readonly IDataStorage<DailyWorkCycle> _rawDataStorage;
        private readonly IDataStorage<DailyWorkRecord> _adjustmentStorage;
        private readonly WorkerPaymentCalculator _calculator;
        private readonly ILogger<WorkerDataService> _logger;

        public WorkerDataService(
            StorageManager storageManager,
            WorkerPaymentCalculator calculator,
            ILogger<WorkerDataService> logger)
        {
            _rawDataStorage = storageManager.GetStorageSync<DailyWorkCycle>("WorkerAttendance");
            // Use WorkerWeeklyData folder for adjustments since it supports writing
            _adjustmentStorage = storageManager.GetStorageSync<DailyWorkRecord>("WorkerWeeklyData");
            _calculator = calculator;
            _logger = logger;
        }

        // === INDIVIDUAL FIELD ACCESS ===

        /// <summary>
        /// Get final sign-in time (adjusted if available, otherwise raw)
        /// </summary>
        public async Task<DateTime?> GetSignInTimeAsync(string attendanceFileName)
        {
            var data = await GetCompleteDataAsync(attendanceFileName);
            return data?.SignIn;
        }

        /// <summary>
        /// Get final sign-out time (adjusted if available, otherwise raw)
        /// </summary>
        public async Task<DateTime?> GetSignOutTimeAsync(string attendanceFileName)
        {
            var data = await GetCompleteDataAsync(attendanceFileName);
            return data?.SignOut;
        }

        /// <summary>
        /// Get final OT sign-in time (adjusted if available, otherwise raw)
        /// </summary>
        public async Task<DateTime?> GetOTSignInTimeAsync(string attendanceFileName)
        {
            var data = await GetCompleteDataAsync(attendanceFileName);
            return data?.OTSignIn;
        }

        /// <summary>
        /// Get final OT sign-out time (adjusted if available, otherwise raw)
        /// </summary>
        public async Task<DateTime?> GetOTSignOutTimeAsync(string attendanceFileName)
        {
            var data = await GetCompleteDataAsync(attendanceFileName);
            return data?.OTSignOut;
        }

        /// <summary>
        /// Get final work hours (adjusted if available, otherwise calculated from raw)
        /// </summary>
        public async Task<decimal> GetWorkHoursAsync(string attendanceFileName, WorkerSettings? settings = null)
        {
            var data = await GetCompleteDataAsync(attendanceFileName, settings);
            return data?.NormalWorkHours ?? 0;
        }

        /// <summary>
        /// Get final OT hours (adjusted if available, otherwise calculated from raw)
        /// </summary>
        public async Task<decimal> GetOTHoursAsync(string attendanceFileName, WorkerSettings? settings = null)
        {
            var data = await GetCompleteDataAsync(attendanceFileName, settings);
            return data?.OTWorkHours ?? 0;
        }

        /// <summary>
        /// Get final on-time status (adjusted if available, otherwise calculated)
        /// </summary>
        public async Task<bool> GetOnTimeStatusAsync(string attendanceFileName, WorkerSettings? settings = null)
        {
            var data = await GetCompleteDataAsync(attendanceFileName, settings);
            return data?.IsOnTime ?? false;
        }

        // === COMPLETE DATA ACCESS ===

        /// <summary>
        /// Get complete final data (the 90% use case - everything calculated and merged)
        /// COMPLETED: Now calculates all derived values properly
        /// </summary>
        public async Task<WorkerDisplayData?> GetCompleteDataAsync(string attendanceFileName, WorkerSettings? settings = null)
        {
            try
            {
                // Load raw data
                var rawCycle = await _rawDataStorage.LoadAsync(attendanceFileName);
                if (rawCycle == null) return null;

                // Load adjustment data
                var adjustment = await GetAdjustmentAsync(attendanceFileName);

                // Extract worker info
                var workerId = rawCycle.WorkerId;
                var workerName = rawCycle.WorkerName ?? $"Worker {workerId}";
                var date = ExtractDateFromFileName(attendanceFileName);

                // Build complete data with final values
                var data = new WorkerDisplayData
                {
                    WorkerId = workerId,
                    WorkerName = workerName,
                    AttendanceFileName = attendanceFileName,
                    HasAdjustments = adjustment?.HasAdjustments == true,
                    AdjustmentDescription = adjustment?.AppliedAdjustment?.Description ?? ""
                };

                // Apply final times (adjusted takes precedence)
                if (adjustment?.HasAdjustments == true && adjustment.AppliedAdjustment != null)
                {
                    var adj = adjustment.AppliedAdjustment;
                    data.SignIn = adj.AdjustedSignIn ?? GetRawSignIn(rawCycle);
                    data.SignOut = adj.AdjustedSignOut ?? GetRawSignOut(rawCycle);
                    data.OTSignIn = adj.AdjustedOTSignIn ?? GetRawOTSignIn(rawCycle);
                    data.OTSignOut = adj.AdjustedOTSignOut ?? GetRawOTSignOut(rawCycle);

                    // Use adjusted calculated values
                    data.NormalWorkHours = adj.AdjustedWorkHours;
                    data.OTWorkHours = adj.AdjustedOTHours;
                    data.IsOnTime = adj.AdjustedOnTime;
                    data.LateMinutes = adj.AdjustedLateMinutes;
                    data.VarianceMinutes = adj.AdjustedVarianceMinutes;
                }
                else
                {
                    // Use raw data and calculate values
                    data.SignIn = GetRawSignIn(rawCycle);
                    data.SignOut = GetRawSignOut(rawCycle);
                    data.OTSignIn = GetRawOTSignIn(rawCycle);
                    data.OTSignOut = GetRawOTSignOut(rawCycle);

                    // Calculate work hours from raw times
                    data.NormalWorkHours = CalculateWorkHours(data.SignIn, data.SignOut);
                    data.OTWorkHours = CalculateWorkHours(data.OTSignIn, data.OTSignOut);

                    // Calculate on-time status and variance if we have settings
                    if (settings != null && data.SignIn.HasValue)
                    {
                        var expectedStart = data.SignIn.Value.Date.Add(settings.ExpectedArrivalTime);
                        data.IsOnTime = data.SignIn.Value <= expectedStart;
                        data.LateMinutes = data.IsOnTime ? 0 : (int)(data.SignIn.Value - expectedStart).TotalMinutes;
                        data.VarianceMinutes = _calculator.CalculateVarianceMinutes(data.NormalWorkHours, settings.ExpectedHoursPerDay);
                    }
                }

                // Set total hours
                data.TotalWorkHours = data.NormalWorkHours + data.OTWorkHours;

                // Set activity flags
                data.HasWorkActivity = data.SignIn.HasValue;
                data.HasOTActivity = data.OTSignIn.HasValue;

                // Determine if worker is currently working (based on having sign-in but no sign-out today)
                if (date.Date == DateTime.Today)
                {
                    data.IsCurrentlyWorking = (data.SignIn.HasValue && !data.SignOut.HasValue) ||
                                            (data.OTSignIn.HasValue && !data.OTSignOut.HasValue);
                }

                _logger.LogDebug("Complete data for {WorkerId}: Normal={NormalHours:F1}h, OT={OTHours:F1}h, HasAdjustments={HasAdjustments}",
                    workerId, data.NormalWorkHours, data.OTWorkHours, data.HasAdjustments);

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete data for {FileName}", attendanceFileName);
                return new WorkerDisplayData
                {
                    HasError = true,
                    ErrorMessage = ex.Message,
                    AttendanceFileName = attendanceFileName
                };
            }
        }

        // === PRIVATE HELPERS ===

        private async Task<DailyWorkRecord?> GetAdjustmentAsync(string attendanceFileName)
        {
            try
            {
                // Convert "2025-10-11_Worker3.json.json" to "adjustment_2025-10-11_Worker3.json"
                var adjustmentFileName = $"adjustment_{attendanceFileName.Replace(".json", "")}";

                _logger.LogDebug("Looking for adjustment file: {AdjustmentFileName}", adjustmentFileName);

                // Check if adjustment file exists first (proper IO Manager usage)
                var exists = await _adjustmentStorage.ExistsAsync(adjustmentFileName);

                if (exists)
                {
                    // File exists, load it
                    var existingAdjustment = await _adjustmentStorage.LoadAsync(adjustmentFileName);
                    _logger.LogDebug("Found existing adjustment file: {AdjustmentFileName}", adjustmentFileName);
                    return existingAdjustment;
                }

                // No adjustment file exists - create a default one with no adjustments
                _logger.LogInformation("No adjustment file found, creating default: {AdjustmentFileName}", adjustmentFileName);

                var date = ExtractDateFromFileName(attendanceFileName);
                var workerId = ExtractWorkerIdFromFileName(attendanceFileName);

                var defaultAdjustment = new DailyWorkRecord
                {
                    Date = date,
                    HasAdjustments = false,
                    AppliedAdjustment = null,
                    // These will be calculated from raw data later in GetCompleteDataAsync
                    WorkHours = 0,
                    OTHours = 0,
                    OnTime = true,
                    LateMinutes = 0,
                    VarianceMinutes = 0,
                    HasData = true
                };

                // Save the default adjustment file using IDataStorage (proper IO Manager usage)
                await _adjustmentStorage.SaveAsync(adjustmentFileName, defaultAdjustment);

                _logger.LogInformation("✅ Created default adjustment file: {AdjustmentFileName}", adjustmentFileName);

                return defaultAdjustment;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error loading/creating adjustment for {FileName}", attendanceFileName);
                return null;
            }
        }

        private int ExtractWorkerIdFromFileName(string fileName)
        {
            try
            {
                // Extract from "2025-10-11_Worker3.json.json" -> "3"
                var parts = fileName.Split('_');
                if (parts.Length >= 2)
                {
                    var workerPart = parts[1].Replace(".json", "").Replace("Worker", "");
                    return int.Parse(workerPart);
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private DateTime ExtractDateFromFileName(string fileName)
        {
            try
            {
                var datePart = fileName.Split('_')[0];
                return DateTime.ParseExact(datePart, "yyyy-MM-dd", null);
            }
            catch
            {
                return DateTime.Today;
            }
        }

        private DateTime? GetRawSignIn(DailyWorkCycle cycle)
        {
            return cycle.Records
                .Where(r => r.Type == AttendanceType.CheckIn && r.WorkCycle == WorkCycle.Normal)
                .FirstOrDefault()?.Timestamp;
        }

        private DateTime? GetRawSignOut(DailyWorkCycle cycle)
        {
            return cycle.Records
                .Where(r => r.Type == AttendanceType.CheckOut && r.WorkCycle == WorkCycle.Normal)
                .LastOrDefault()?.Timestamp;
        }

        private DateTime? GetRawOTSignIn(DailyWorkCycle cycle)
        {
            return cycle.Records
                .Where(r => r.Type == AttendanceType.CheckIn && r.WorkCycle == WorkCycle.OT)
                .FirstOrDefault()?.Timestamp;
        }

        private DateTime? GetRawOTSignOut(DailyWorkCycle cycle)
        {
            return cycle.Records
                .Where(r => r.Type == AttendanceType.CheckOut && r.WorkCycle == WorkCycle.OT)
                .LastOrDefault()?.Timestamp;
        }

        private decimal CalculateWorkHours(DateTime? signIn, DateTime? signOut)
        {
            if (!signIn.HasValue || !signOut.HasValue) return 0;
            var duration = signOut.Value - signIn.Value;
            return (decimal)duration.TotalHours;
        }
    }
}