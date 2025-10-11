// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDataService.cs
// Purpose: Unified orchestration service for worker attendance data
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
            _adjustmentStorage = storageManager.GetStorageSync<DailyWorkRecord>("WorkerWeeklyRecords");
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
            var data = await GetCompleteDataAsync(attendanceFileName);
            if (data == null) return 0;

            // If we have adjusted work hours, use them
            var adjustment = await GetAdjustmentAsync(attendanceFileName);
            if (adjustment?.HasAdjustments == true && adjustment.AppliedAdjustment != null)
            {
                return adjustment.AppliedAdjustment.AdjustedWorkHours;
            }

            // Otherwise calculate from final times
            if (data.SignIn.HasValue && data.SignOut.HasValue)
            {
                var duration = data.SignOut.Value - data.SignIn.Value;
                return (decimal)duration.TotalHours;
            }

            return 0;
        }

        /// <summary>
        /// Get final OT hours (adjusted if available, otherwise calculated from raw)
        /// </summary>
        public async Task<decimal> GetOTHoursAsync(string attendanceFileName, WorkerSettings? settings = null)
        {
            var data = await GetCompleteDataAsync(attendanceFileName);
            if (data == null) return 0;

            // If we have adjusted OT hours, use them
            var adjustment = await GetAdjustmentAsync(attendanceFileName);
            if (adjustment?.HasAdjustments == true && adjustment.AppliedAdjustment != null)
            {
                return adjustment.AppliedAdjustment.AdjustedOTHours;
            }

            // Otherwise calculate from final times
            if (data.OTSignIn.HasValue && data.OTSignOut.HasValue)
            {
                var duration = data.OTSignOut.Value - data.OTSignIn.Value;
                return (decimal)duration.TotalHours;
            }

            return 0;
        }

        /// <summary>
        /// Get final on-time status (adjusted if available, otherwise calculated)
        /// </summary>
        public async Task<bool> GetOnTimeStatusAsync(string attendanceFileName, WorkerSettings? settings = null)
        {
            var adjustment = await GetAdjustmentAsync(attendanceFileName);
            if (adjustment?.HasAdjustments == true && adjustment.AppliedAdjustment != null)
            {
                return adjustment.AppliedAdjustment.AdjustedOnTime;
            }

            // Calculate from raw data
            var data = await GetCompleteDataAsync(attendanceFileName);
            if (data?.SignIn == null || settings == null) return false;

            var expectedStart = data.SignIn.Value.Date.Add(settings.ExpectedArrivalTime);
            return data.SignIn.Value <= expectedStart;
        }

        // === COMPLETE DATA ACCESS ===

        /// <summary>
        /// Get complete final data (the 90% use case - everything calculated and merged)
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
                    HasAdjustments = adjustment?.HasAdjustments == true
                };

                // Apply final times (adjusted takes precedence)
                if (adjustment?.HasAdjustments == true && adjustment.AppliedAdjustment != null)
                {
                    var adj = adjustment.AppliedAdjustment;
                    data.SignIn = adj.AdjustedSignIn ?? GetRawSignIn(rawCycle);
                    data.SignOut = adj.AdjustedSignOut ?? GetRawSignOut(rawCycle);
                    data.OTSignIn = adj.AdjustedOTSignIn ?? GetRawOTSignIn(rawCycle);
                    data.OTSignOut = adj.AdjustedOTSignOut ?? GetRawOTSignOut(rawCycle);
                }
                else
                {
                    data.SignIn = GetRawSignIn(rawCycle);
                    data.SignOut = GetRawSignOut(rawCycle);
                    data.OTSignIn = GetRawOTSignIn(rawCycle);
                    data.OTSignOut = GetRawOTSignOut(rawCycle);
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete data for {FileName}", attendanceFileName);
                return null;
            }
        }

        // === PRIVATE HELPERS ===

        private async Task<DailyWorkRecord?> GetAdjustmentAsync(string attendanceFileName)
        {
            try
            {
                // Convert "2025-01-15_Worker3.json" to "adjustments_2025-01-15_Worker3.json"
                var adjustmentFileName = $"adjustments_{attendanceFileName}";
                return await _adjustmentStorage.LoadAsync(adjustmentFileName);
            }
            catch
            {
                return null;
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
    }
}