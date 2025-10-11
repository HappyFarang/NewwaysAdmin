// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDisplayService.cs
// Purpose: Simple helper service for 90% of UI needs
// Orchestrates WorkerDataService + TimeCalculator to return exactly what tables need

using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    /// <summary>
    /// Helper service that provides simple calls for most UI needs
    /// Handles the complexity of getting data and calculating values
    /// 90% of table views should only need to call this service
    /// </summary>
    public class WorkerDisplayService
    {
        private readonly WorkerDataService _dataService;
        private readonly TimeCalculator _calculator;
        private readonly ILogger<WorkerDisplayService> _logger;

        public WorkerDisplayService(
            WorkerDataService dataService,
            TimeCalculator calculator,
            ILogger<WorkerDisplayService> logger)
        {
            _dataService = dataService;
            _calculator = calculator;
            _logger = logger;
        }

        // === BASIC TIME GETTERS (ADJUSTED VALUES) ===

        /// <summary>
        /// Get adjusted sign-in time (or raw if no adjustment)
        /// </summary>
        public async Task<DateTime?> GetAdjustedSignInAsync(string attendanceFileName)
        {
            return await _dataService.GetFinalTimeFieldAsync(attendanceFileName, TimeField.SignIn);
        }

        /// <summary>
        /// Get adjusted sign-out time (or raw if no adjustment)
        /// </summary>
        public async Task<DateTime?> GetAdjustedSignOutAsync(string attendanceFileName)
        {
            return await _dataService.GetFinalTimeFieldAsync(attendanceFileName, TimeField.SignOut);
        }

        /// <summary>
        /// Get adjusted OT sign-in time (or raw if no adjustment)
        /// </summary>
        public async Task<DateTime?> GetAdjustedOTSignInAsync(string attendanceFileName)
        {
            return await _dataService.GetFinalTimeFieldAsync(attendanceFileName, TimeField.OTSignIn);
        }

        /// <summary>
        /// Get adjusted OT sign-out time (or raw if no adjustment)
        /// </summary>
        public async Task<DateTime?> GetAdjustedOTSignOutAsync(string attendanceFileName)
        {
            return await _dataService.GetFinalTimeFieldAsync(attendanceFileName, TimeField.OTSignOut);
        }

        // === CALCULATED VALUES ===

        /// <summary>
        /// Get total work hours (normal + OT) using adjusted times
        /// </summary>
        public async Task<decimal> GetAdjustedTotalWorkHoursAsync(string attendanceFileName)
        {
            var finalTimes = await _dataService.GetFinalTimesAsync(attendanceFileName);
            return _calculator.CalculateTotalWorkHours(
                finalTimes.SignIn,
                finalTimes.SignOut,
                finalTimes.OTSignIn,
                finalTimes.OTSignOut);
        }

        /// <summary>
        /// Get normal work hours using adjusted times
        /// </summary>
        public async Task<decimal> GetAdjustedNormalWorkHoursAsync(string attendanceFileName)
        {
            var finalTimes = await _dataService.GetFinalTimesAsync(attendanceFileName);
            return _calculator.CalculateNormalWorkHours(finalTimes.SignIn, finalTimes.SignOut);
        }

        /// <summary>
        /// Get OT work hours using adjusted times
        /// </summary>
        public async Task<decimal> GetAdjustedOTWorkHoursAsync(string attendanceFileName)
        {
            var finalTimes = await _dataService.GetFinalTimesAsync(attendanceFileName);
            return _calculator.CalculateOTWorkHours(finalTimes.OTSignIn, finalTimes.OTSignOut);
        }

        /// <summary>
        /// Check if worker is late using adjusted sign-in time
        /// </summary>
        public async Task<bool> IsWorkerLateAsync(string attendanceFileName, WorkerSettings settings)
        {
            var signIn = await GetAdjustedSignInAsync(attendanceFileName);
            return _calculator.IsLate(signIn, settings.ExpectedArrivalTime);
        }

        /// <summary>
        /// Get late minutes using adjusted sign-in time
        /// </summary>
        public async Task<int> GetLateMinutesAsync(string attendanceFileName, WorkerSettings settings)
        {
            var signIn = await GetAdjustedSignInAsync(attendanceFileName);
            return _calculator.CalculateLateMinutes(signIn, settings.ExpectedArrivalTime);
        }

        /// <summary>
        /// Check if worker is on time using adjusted sign-in time
        /// </summary>
        public async Task<bool> IsWorkerOnTimeAsync(string attendanceFileName, WorkerSettings settings)
        {
            return !(await IsWorkerLateAsync(attendanceFileName, settings));
        }

        /// <summary>
        /// Get variance in minutes from expected hours using adjusted work hours
        /// </summary>
        public async Task<int> GetVarianceMinutesAsync(string attendanceFileName, WorkerSettings settings)
        {
            var normalHours = await GetAdjustedNormalWorkHoursAsync(attendanceFileName);
            return _calculator.CalculateVarianceMinutes(normalHours, settings.ExpectedHoursPerDay);
        }

        // === ADJUSTMENT STATUS ===

        /// <summary>
        /// Check if this attendance file has any adjustments applied
        /// Perfect for marking rows in tables
        /// </summary>
        public async Task<bool> HasAdjustmentsAsync(string attendanceFileName)
        {
            var adjustmentTimes = await _dataService.GetAdjustmentTimesAsync(attendanceFileName);
            return adjustmentTimes.HasAdjustments;
        }

        /// <summary>
        /// Get adjustment description if any adjustments exist
        /// </summary>
        public async Task<string> GetAdjustmentDescriptionAsync(string attendanceFileName)
        {
            var adjustmentTimes = await _dataService.GetAdjustmentTimesAsync(attendanceFileName);
            return adjustmentTimes.HasAdjustments ? adjustmentTimes.Description : string.Empty;
        }

        // === ACTIVITY STATUS ===

        /// <summary>
        /// Check if worker has any work activity (has sign-in)
        /// </summary>
        public async Task<bool> HasWorkActivityAsync(string attendanceFileName)
        {
            var signIn = await GetAdjustedSignInAsync(attendanceFileName);
            return _calculator.HasWorkActivity(signIn);
        }

        /// <summary>
        /// Check if worker has OT activity
        /// </summary>
        public async Task<bool> HasOTActivityAsync(string attendanceFileName)
        {
            var otSignIn = await GetAdjustedOTSignInAsync(attendanceFileName);
            return _calculator.HasOTActivity(otSignIn);
        }

        /// <summary>
        /// Check if worker is currently working (for today's date only)
        /// </summary>
        public async Task<bool> IsCurrentlyWorkingAsync(string attendanceFileName, DateTime date)
        {
            var finalTimes = await _dataService.GetFinalTimesAsync(attendanceFileName);
            return _calculator.IsCurrentlyWorking(
                finalTimes.SignIn,
                finalTimes.SignOut,
                finalTimes.OTSignIn,
                finalTimes.OTSignOut,
                date);
        }

        /// <summary>
        /// Get current work duration for active workers
        /// </summary>
        public async Task<TimeSpan> GetCurrentWorkDurationAsync(string attendanceFileName)
        {
            var finalTimes = await _dataService.GetFinalTimesAsync(attendanceFileName);
            return _calculator.CalculateCurrentWorkDuration(
                finalTimes.SignIn,
                finalTimes.SignOut,
                finalTimes.OTSignIn,
                finalTimes.OTSignOut);
        }

        // === COMPLETE DATA FOR COMPLEX VIEWS ===

        /// <summary>
        /// Get all basic display data needed for a table row
        /// Perfect for populating weekly tables
        /// </summary>
        public async Task<WorkerDisplayData> GetDisplayDataAsync(string attendanceFileName, WorkerSettings settings)
        {
            try
            {
                var finalTimes = await _dataService.GetFinalTimesAsync(attendanceFileName);
                var hasAdjustments = await HasAdjustmentsAsync(attendanceFileName);
                var adjustmentDescription = hasAdjustments ? await GetAdjustmentDescriptionAsync(attendanceFileName) : string.Empty;

                var normalHours = _calculator.CalculateNormalWorkHours(finalTimes.SignIn, finalTimes.SignOut);
                var otHours = _calculator.CalculateOTWorkHours(finalTimes.OTSignIn, finalTimes.OTSignOut);
                var totalHours = normalHours + otHours;

                var isLate = _calculator.IsLate(finalTimes.SignIn, settings.ExpectedArrivalTime);
                var lateMinutes = _calculator.CalculateLateMinutes(finalTimes.SignIn, settings.ExpectedArrivalTime);
                var varianceMinutes = _calculator.CalculateVarianceMinutes(normalHours, settings.ExpectedHoursPerDay);

                return new WorkerDisplayData
                {
                    AttendanceFileName = attendanceFileName,
                    SignIn = finalTimes.SignIn,
                    SignOut = finalTimes.SignOut,
                    OTSignIn = finalTimes.OTSignIn,
                    OTSignOut = finalTimes.OTSignOut,
                    NormalWorkHours = normalHours,
                    OTWorkHours = otHours,
                    TotalWorkHours = totalHours,
                    IsLate = isLate,
                    LateMinutes = lateMinutes,
                    IsOnTime = !isLate,
                    VarianceMinutes = varianceMinutes,
                    HasAdjustments = hasAdjustments,
                    AdjustmentDescription = adjustmentDescription,
                    HasWorkActivity = _calculator.HasWorkActivity(finalTimes.SignIn),
                    HasOTActivity = _calculator.HasOTActivity(finalTimes.OTSignIn)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting display data for {AttendanceFileName}", attendanceFileName);
                throw;
            }
        }
    }
}