// File: NewwaysAdmin.WebAdmin/Services/Workers/AdjustmentService.cs
// Purpose: Handle daily work record adjustments and apply them to weekly data

using NewwaysAdmin.WebAdmin.Models.Workers;
using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class AdjustmentService
    {
        private readonly IAuthenticationService _authService;
        private readonly WorkerPaymentCalculator _calculator;
        private readonly ILogger<AdjustmentService> _logger;

        public AdjustmentService(
            IAuthenticationService authService,
            WorkerPaymentCalculator calculator,
            ILogger<AdjustmentService> logger)
        {
            _authService = authService;
            _calculator = calculator;
            _logger = logger;
        }

        /// <summary>
        /// Apply "Worker on time" quick fix - override late status due to coffee collection
        /// PRESERVES TIMING DELTA: Maintains the relationship between actual and expected times
        /// </summary>
        public async Task<DailyWorkRecord> ApplyWorkerOnTimeAdjustmentAsync(
    DailyWorkRecord originalRecord,
    WorkerSettings settings,
    string description = "Worker on time - collecting coffee")
        {
            if (!originalRecord.HasData)
                throw new ArgumentException("Cannot adjust a day with no data");

            var currentUser = await _authService.GetCurrentSessionAsync();
            var username = currentUser?.Username ?? "System";

            // Calculate the adjusted sign-in time (when they should have arrived to be "on time")
            DateTime? adjustedSignInTime = null;
            decimal adjustedWorkHours = originalRecord.WorkHours;
            int adjustedVarianceMinutes = originalRecord.VarianceMinutes;
            decimal adjustedDailyPay = originalRecord.DailyPay;

            if (originalRecord.NormalSignIn.HasValue && originalRecord.LateMinutes > 0)
            {
                // Calculate when they should have signed in to be on time
                var expectedSignInTime = originalRecord.Date.Add(settings.ExpectedArrivalTime);
                adjustedSignInTime = expectedSignInTime;

                // If there's a sign-out time, recalculate work hours based on adjusted sign-in
                if (originalRecord.NormalSignOut.HasValue)
                {
                    var adjustedDuration = originalRecord.NormalSignOut.Value - expectedSignInTime;
                    adjustedWorkHours = (decimal)adjustedDuration.TotalHours;

                    // Recalculate variance based on new work hours
                    adjustedVarianceMinutes = _calculator.CalculateVarianceMinutes(adjustedWorkHours, settings.ExpectedHoursPerDay);

                    // Recalculate pay with adjusted work hours
                    var hasNormalActivity = true; // Since we have sign-in time
                    adjustedDailyPay = _calculator.CalculateDailyPay(
                        adjustedWorkHours,
                        originalRecord.OTHours,
                        settings,
                        hasNormalActivity);
                }
            }

            // Create adjustment record with FULL timing preservation
            var adjustment = new DailyAdjustment
            {
                Type = AdjustmentType.OnTimeOverride,
                Description = description,
                AppliedAt = DateTime.Now,
                AppliedBy = username,

                // Store ALL original values for complete reversibility
                OriginalWorkHours = originalRecord.WorkHours,
                OriginalOTHours = originalRecord.OTHours,
                OriginalOnTime = originalRecord.OnTime,
                OriginalLateMinutes = originalRecord.LateMinutes,
                OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                OriginalSignOut = originalRecord.NormalSignOut,
                OriginalSignIn = originalRecord.NormalSignIn, // PRESERVE original sign-in timing

                // Set adjusted values (recalculated based on expected sign-in time)
                AdjustedWorkHours = adjustedWorkHours,                // RECALCULATED
                AdjustedOTHours = originalRecord.OTHours,             // No change to OT
                AdjustedOnTime = true,                                // Override to on-time
                AdjustedLateMinutes = 0,                              // Override to 0 (on time)
                AdjustedVarianceMinutes = adjustedVarianceMinutes,    // RECALCULATED
                AdjustedSignOut = originalRecord.NormalSignOut,       // No change to sign-out
                AdjustedSignIn = adjustedSignInTime ?? originalRecord.NormalSignIn // ADJUSTED sign-in time
            };

            // Apply adjustment to record - ONLY change the calculated values, preserve original timing
            // DO NOT change NormalSignIn/NormalSignOut - keep original for analytics
            originalRecord.OnTime = true;
            originalRecord.LateMinutes = 0;
            originalRecord.HasAdjustments = true;
            originalRecord.AppliedAdjustment = adjustment;

            _logger.LogInformation(
                "Applied 'Worker on time' adjustment for {Date} by {User}: {Description}. " +
                "Original: {OriginalSignIn} → Adjusted: {AdjustedSignIn}, Work hours: {OriginalHours:F1} → {AdjustedHours:F1}, Variance: {OriginalVariance} → {AdjustedVariance}",
                originalRecord.Date.ToString("yyyy-MM-dd"),
                username,
                description,
                adjustment.OriginalSignIn?.ToString("HH:mm") ?? "N/A",
                adjustment.AdjustedSignIn?.ToString("HH:mm") ?? "N/A",
                adjustment.OriginalWorkHours,
                adjustment.AdjustedWorkHours,
                adjustment.OriginalVarianceMinutes,
                adjustment.AdjustedVarianceMinutes);

            return originalRecord;
        }

        /// <summary>
        /// Convert positive variance to overtime hours
        /// PRESERVES TIMING DELTA: Maintains the relationship between work hours and expected hours
        /// </summary>
        public async Task<DailyWorkRecord> ApplyVarianceToOTAdjustmentAsync(
            DailyWorkRecord originalRecord,
            WorkerSettings settings)
        {
            if (!originalRecord.HasData)
                throw new ArgumentException("Cannot adjust a day with no data");

            if (originalRecord.VarianceMinutes <= 0)
                throw new ArgumentException("Can only convert positive variance to OT");

            var currentUser = await _authService.GetCurrentSessionAsync();
            var username = currentUser?.Username ?? "System";

            // Calculate OT hours from variance (preserve the math relationship)
            var varianceHours = (decimal)originalRecord.VarianceMinutes / 60;
            var newOTHours = originalRecord.OTHours + varianceHours;
            var newVarianceMinutes = 0; // Variance becomes zero after conversion

            // Recalculate pay with new OT hours
            var hasNormalActivity = originalRecord.NormalSignIn.HasValue;
            var newDailyPay = _calculator.CalculateDailyPay(
                originalRecord.WorkHours,
                newOTHours,
                settings,
                hasNormalActivity);

            // Create adjustment record with FULL timing preservation
            var adjustment = new DailyAdjustment
            {
                Type = AdjustmentType.VarianceToOT,
                Description = $"Convert {originalRecord.VarianceMinutes} min variance to OT ({varianceHours:F1}h)",
                AppliedAt = DateTime.Now,
                AppliedBy = username,

                // Store ALL original values for complete reversibility
                OriginalWorkHours = originalRecord.WorkHours,
                OriginalOTHours = originalRecord.OTHours,
                OriginalOnTime = originalRecord.OnTime,
                OriginalLateMinutes = originalRecord.LateMinutes,
                OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                OriginalSignOut = originalRecord.NormalSignOut,
                OriginalSignIn = originalRecord.NormalSignIn, // PRESERVE timing delta

                // Set adjusted values (converting variance to OT)
                AdjustedWorkHours = originalRecord.WorkHours,    // No change to regular hours
                AdjustedOTHours = newOTHours,                    // Add variance as OT
                AdjustedOnTime = originalRecord.OnTime,          // No change
                AdjustedLateMinutes = originalRecord.LateMinutes, // No change
                AdjustedVarianceMinutes = newVarianceMinutes,    // Clear variance
                AdjustedSignOut = originalRecord.NormalSignOut,  // No change to timing
                AdjustedSignIn = originalRecord.NormalSignIn     // No change to timing
            };

            // Apply adjustment to record
            originalRecord.OTHours = newOTHours;
            originalRecord.VarianceMinutes = newVarianceMinutes;
            originalRecord.DailyPay = newDailyPay;
            originalRecord.HasAdjustments = true;
            originalRecord.AppliedAdjustment = adjustment;

            _logger.LogInformation(
                "Applied variance to OT adjustment for {Date} by {User}: {VarianceMinutes} min → {OTHours:F1} OT hours. " +
                "Preserved timing delta - Work: {WorkHours:F1}h, Sign-in: {SignIn}, Sign-out: {SignOut}",
                originalRecord.Date.ToString("yyyy-MM-dd"),
                username,
                adjustment.OriginalVarianceMinutes,
                varianceHours,
                adjustment.OriginalWorkHours,
                adjustment.OriginalSignIn?.ToString("HH:mm") ?? "N/A",
                adjustment.OriginalSignOut?.ToString("HH:mm") ?? "N/A");

            return originalRecord;
        }

        /// <summary>
        /// Apply manual sign-out for workers who forgot to sign out
        /// PRESERVES TIMING DELTA: Maintains the relationship between all timing events
        /// </summary>
        public async Task<DailyWorkRecord> ApplyManualSignOutAdjustmentAsync(
            DailyWorkRecord originalRecord,
            WorkerSettings settings,
            DateTime? customSignOutTime = null)
        {
            if (!originalRecord.HasData)
                throw new ArgumentException("Cannot adjust a day with no data");

            var currentUser = await _authService.GetCurrentSessionAsync();
            var username = currentUser?.Username ?? "System";

            // Use custom time or calculate expected end time
            var signOutTime = customSignOutTime ?? CalculateExpectedSignOutTime(originalRecord, settings);

            // Recalculate hours based on new sign-out time (preserve the calculation method)
            var newWorkHours = CalculateWorkHoursWithSignOut(originalRecord, signOutTime, settings);
            var newVarianceMinutes = _calculator.CalculateVarianceMinutes(newWorkHours, settings.ExpectedHoursPerDay);

            // Recalculate pay
            var hasNormalActivity = originalRecord.NormalSignIn.HasValue;
            var newDailyPay = _calculator.CalculateDailyPay(
                newWorkHours,
                originalRecord.OTHours,
                settings,
                hasNormalActivity);

            // Create adjustment record with FULL timing preservation
            var adjustment = new DailyAdjustment
            {
                Type = AdjustmentType.ManualSignOut,
                Description = customSignOutTime.HasValue
                    ? $"Manual sign-out at {signOutTime:HH:mm}"
                    : $"Auto sign-out at expected time {signOutTime:HH:mm}",
                AppliedAt = DateTime.Now,
                AppliedBy = username,

                // Store ALL original values for complete reversibility
                OriginalWorkHours = originalRecord.WorkHours,
                OriginalOTHours = originalRecord.OTHours,
                OriginalOnTime = originalRecord.OnTime,
                OriginalLateMinutes = originalRecord.LateMinutes,
                OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                OriginalSignOut = originalRecord.NormalSignOut, // PRESERVE original (missing) sign-out
                OriginalSignIn = originalRecord.NormalSignIn,   // PRESERVE timing delta

                // Set adjusted values (new sign-out time and recalculated hours)
                AdjustedWorkHours = newWorkHours,
                AdjustedOTHours = originalRecord.OTHours,        // OT unchanged
                AdjustedOnTime = originalRecord.OnTime,          // On-time status unchanged
                AdjustedLateMinutes = originalRecord.LateMinutes, // Late minutes unchanged
                AdjustedVarianceMinutes = newVarianceMinutes,
                AdjustedSignOut = signOutTime,                   // NEW sign-out time
                AdjustedSignIn = originalRecord.NormalSignIn     // No change to timing
            };

            // Apply adjustment to record
            originalRecord.WorkHours = newWorkHours;
            originalRecord.VarianceMinutes = newVarianceMinutes;
            originalRecord.DailyPay = newDailyPay;
            originalRecord.NormalSignOut = signOutTime;
            originalRecord.HasAdjustments = true;
            originalRecord.AppliedAdjustment = adjustment;

            _logger.LogInformation(
                "Applied manual sign-out adjustment for {Date} by {User}: Sign-out at {SignOutTime}. " +
                "Preserved timing delta - Sign-in: {SignIn}, Work hours: {OriginalHours:F1}h → {NewHours:F1}h",
                originalRecord.Date.ToString("yyyy-MM-dd"),
                username,
                signOutTime.ToString("HH:mm"),
                adjustment.OriginalSignIn?.ToString("HH:mm") ?? "N/A",
                adjustment.OriginalWorkHours,
                newWorkHours);

            return originalRecord;
        }

        /// <summary>
        /// Apply a custom manual adjustment with full timing delta preservation
        /// PRESERVES ALL TIMING DELTAS: Maintains every timing relationship for complete reversibility
        /// </summary>
        public async Task<DailyWorkRecord> ApplyCustomAdjustmentAsync(
            DailyWorkRecord originalRecord,
            WorkerSettings settings,
            string description,
            DateTime? customSignOutTime = null,
            decimal? customOTHours = null,
            bool? overrideOnTime = null)
        {
            if (!originalRecord.HasData)
                throw new ArgumentException("Cannot adjust a day with no data");

            if (string.IsNullOrWhiteSpace(description))
                throw new ArgumentException("Description is required for custom adjustments");

            var currentUser = await _authService.GetCurrentSessionAsync();
            var username = currentUser?.Username ?? "System";

            // Start with current values
            var newWorkHours = originalRecord.WorkHours;
            var newOTHours = customOTHours ?? originalRecord.OTHours;
            var newVarianceMinutes = originalRecord.VarianceMinutes;
            var newSignOutTime = originalRecord.NormalSignOut;
            var newOnTime = originalRecord.OnTime;
            var newLateMinutes = originalRecord.LateMinutes;

            // Apply custom sign-out time if provided
            if (customSignOutTime.HasValue)
            {
                newSignOutTime = customSignOutTime.Value;
                newWorkHours = CalculateWorkHoursWithSignOut(originalRecord, newSignOutTime, settings);
                newVarianceMinutes = _calculator.CalculateVarianceMinutes(newWorkHours, settings.ExpectedHoursPerDay);
            }

            // Apply on-time override if provided
            if (overrideOnTime.HasValue)
            {
                newOnTime = overrideOnTime.Value;
                newLateMinutes = newOnTime ? 0 : originalRecord.LateMinutes; // Keep original late minutes if still late
            }

            // Recalculate pay based on all adjustments
            var hasNormalActivity = originalRecord.NormalSignIn.HasValue;
            var newDailyPay = _calculator.CalculateDailyPay(newWorkHours, newOTHours, settings, hasNormalActivity);

            // Create adjustment record with COMPLETE timing preservation
            var adjustment = new DailyAdjustment
            {
                Type = AdjustmentType.CustomAdjustment,
                Description = description,
                AppliedAt = DateTime.Now,
                AppliedBy = username,

                // Store EVERY original value for perfect reversibility
                OriginalWorkHours = originalRecord.WorkHours,
                OriginalOTHours = originalRecord.OTHours,
                OriginalOnTime = originalRecord.OnTime,
                OriginalLateMinutes = originalRecord.LateMinutes,
                OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                OriginalSignOut = originalRecord.NormalSignOut,
                OriginalSignIn = originalRecord.NormalSignIn,

                // Store EVERY adjusted value
                AdjustedWorkHours = newWorkHours,
                AdjustedOTHours = newOTHours,
                AdjustedOnTime = newOnTime,
                AdjustedLateMinutes = newLateMinutes,
                AdjustedVarianceMinutes = newVarianceMinutes,
                AdjustedSignOut = newSignOutTime,
                AdjustedSignIn = originalRecord.NormalSignIn // Sign-in time never changes
            };

            // Apply all adjustments to record
            originalRecord.WorkHours = newWorkHours;
            originalRecord.OTHours = newOTHours;
            originalRecord.OnTime = newOnTime;
            originalRecord.LateMinutes = newLateMinutes;
            originalRecord.VarianceMinutes = newVarianceMinutes;
            originalRecord.DailyPay = newDailyPay;
            originalRecord.NormalSignOut = newSignOutTime;
            originalRecord.HasAdjustments = true;
            originalRecord.AppliedAdjustment = adjustment;

            _logger.LogInformation(
                "Applied custom adjustment for {Date} by {User}: {Description}. " +
                "Changes: {ChangeSummary}. Timing preserved: Sign-in {SignIn}",
                originalRecord.Date.ToString("yyyy-MM-dd"),
                username,
                description,
                adjustment.ChangeSummary,
                adjustment.OriginalSignIn?.ToString("HH:mm") ?? "N/A");

            return originalRecord;
        }

        /// <summary>
        /// Reverse an adjustment and restore original values with proper timing delta
        /// </summary>
        public async Task<DailyWorkRecord> ReverseAdjustmentAsync(DailyWorkRecord adjustedRecord)
        {
            if (!adjustedRecord.HasAdjustments || adjustedRecord.AppliedAdjustment == null)
                throw new ArgumentException("Record has no adjustments to reverse");

            var currentUser = await _authService.GetCurrentSessionAsync();
            var username = currentUser?.Username ?? "System";

            var originalAdjustment = adjustedRecord.AppliedAdjustment;

            // Restore ALL original values exactly
            adjustedRecord.WorkHours = originalAdjustment.OriginalWorkHours;
            adjustedRecord.OTHours = originalAdjustment.OriginalOTHours;
            adjustedRecord.OnTime = originalAdjustment.OriginalOnTime;
            adjustedRecord.LateMinutes = originalAdjustment.OriginalLateMinutes;
            adjustedRecord.VarianceMinutes = originalAdjustment.OriginalVarianceMinutes;
            adjustedRecord.NormalSignOut = originalAdjustment.OriginalSignOut;
            adjustedRecord.NormalSignIn = originalAdjustment.OriginalSignIn;

            // Clear adjustment
            adjustedRecord.HasAdjustments = false;
            adjustedRecord.AppliedAdjustment = null;

            _logger.LogInformation(
                "Reversed adjustment for {Date} by {User}. " +
                "Restored: OnTime={OnTime}, LateMinutes={LateMinutes}, SignIn={SignIn}",
                adjustedRecord.Date.ToString("yyyy-MM-dd"),
                username,
                adjustedRecord.OnTime,
                adjustedRecord.LateMinutes,
                adjustedRecord.NormalSignIn?.ToString("HH:mm") ?? "N/A");

            return adjustedRecord;
        }

        /// <summary>
        /// Apply a weekly data adjustment and recalculate totals
        /// </summary>
        public void ApplyAdjustmentToWeeklyData(WorkerWeeklyData weeklyData, DailyWorkRecord adjustedDay)
        {
            // Find and replace the daily record
            var existingDay = weeklyData.DailyRecords.FirstOrDefault(d => d.Date.Date == adjustedDay.Date.Date);
            if (existingDay != null)
            {
                var index = weeklyData.DailyRecords.IndexOf(existingDay);
                weeklyData.DailyRecords[index] = adjustedDay;
            }

            // Recalculate weekly totals
            weeklyData.RecalculateWeeklyTotals();

            _logger.LogInformation(
                "Applied adjustment to weekly data for worker {WorkerId}, week {WeekStartDate:yyyy-MM-dd}. New totals: Work={TotalWork:F1}h, OT={TotalOT:F1}h, Pay=${TotalPay:F2}",
                weeklyData.WorkerId,
                weeklyData.WeekStartDate,
                weeklyData.TotalWorkHours,
                weeklyData.TotalOTHours,
                weeklyData.TotalPay);
        }

        // === PRIVATE HELPER METHODS ===
        private DateTime CalculateExpectedSignOutTime(DailyWorkRecord record, WorkerSettings settings)
        {
            if (record.NormalSignIn == null)
                return DateTime.Today.Add(TimeSpan.FromHours(17)); // Default 5 PM

            var expectedWorkHours = settings.ExpectedHoursPerDay;
            return record.NormalSignIn.Value.AddHours((double)expectedWorkHours);
        }

        private decimal CalculateWorkHoursWithSignOut(DailyWorkRecord record, DateTime? signOutTime, WorkerSettings settings)
        {
            if (record.NormalSignIn == null || signOutTime == null)
                return 0;

            var duration = signOutTime.Value - record.NormalSignIn.Value;
            return (decimal)duration.TotalHours;
        }
    }
}