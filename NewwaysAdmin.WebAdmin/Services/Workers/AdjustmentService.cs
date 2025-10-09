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
        /// FIXED: Properly handle active workers when adjusting sign-in time
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

                // CRITICAL FIX: Recalculate work hours for both completed and active workers
                if (originalRecord.NormalSignOut.HasValue)
                {
                    // Completed worker: use actual sign-out time
                    var adjustedDuration = originalRecord.NormalSignOut.Value - expectedSignInTime;
                    adjustedWorkHours = (decimal)adjustedDuration.TotalHours;
                    _logger.LogDebug("Worker on time - completed worker: {Hours:F1}h (Expected SignIn: {SignIn}, SignOut: {SignOut})",
                        adjustedWorkHours, expectedSignInTime.ToString("HH:mm"), originalRecord.NormalSignOut.Value.ToString("HH:mm"));
                }
                else if (originalRecord.Date.Date == DateTime.Today)
                {
                    // CRITICAL FIX: Active worker (still working today) - calculate from expected sign-in to now
                    var currentTime = DateTime.Now;
                    var adjustedDuration = currentTime - expectedSignInTime;
                    adjustedWorkHours = (decimal)adjustedDuration.TotalHours;
                    _logger.LogInformation("Worker on time - ACTIVE worker: {Hours:F1}h (Expected SignIn: {SignIn}, Current: {CurrentTime})",
                        adjustedWorkHours, expectedSignInTime.ToString("HH:mm"), currentTime.ToString("HH:mm"));
                }
                else
                {
                    // Past date worker with no sign-out - keep original hours
                    _logger.LogDebug("Worker on time - past date worker with no sign-out, keeping original work hours: {Hours:F1}h", originalRecord.WorkHours);
                }

                // Recalculate variance and pay based on new work hours
                adjustedVarianceMinutes = _calculator.CalculateVarianceMinutes(adjustedWorkHours, settings.ExpectedHoursPerDay);

                var hasNormalActivity = true; // Since we have sign-in time
                adjustedDailyPay = _calculator.CalculateDailyPay(
                    adjustedWorkHours,
                    originalRecord.OTHours,
                    settings,
                    hasNormalActivity);
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
                AdjustedWorkHours = adjustedWorkHours,                // RECALCULATED for active workers
                AdjustedOTHours = originalRecord.OTHours,             // No change to OT
                AdjustedOnTime = true,                                // Override to on-time
                AdjustedLateMinutes = 0,                              // Override to 0 (on time)
                AdjustedVarianceMinutes = adjustedVarianceMinutes,    // RECALCULATED
                AdjustedSignOut = originalRecord.NormalSignOut,       // No change to sign-out
                AdjustedSignIn = adjustedSignInTime ?? originalRecord.NormalSignIn // ADJUSTED sign-in time
            };

            // Apply adjustment to record - ONLY change the calculated values, preserve original timing
            // DO NOT change NormalSignIn/NormalSignOut - keep original for analytics
            originalRecord.WorkHours = adjustedWorkHours;  // CRITICAL: Apply the recalculated work hours
            originalRecord.VarianceMinutes = adjustedVarianceMinutes;
            originalRecord.DailyPay = adjustedDailyPay;
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
                adjustedWorkHours,
                adjustment.OriginalVarianceMinutes,
                adjustedVarianceMinutes);

            return originalRecord;
        }

        /// <summary>
        /// Convert positive variance to overtime hours
        /// PRESERVES TIMING DELTA: Maintains the relationship between work hours and expected hours
        /// </summary>
        /// <summary>
        /// Convert positive variance to overtime hours with proper time splitting
        /// FIXED: Handles both scenarios - new OT period creation and existing OT period extension
        /// </summary>
        public async Task<DailyWorkRecord> ApplyVarianceToOTAdjustmentAsync(
            DailyWorkRecord originalRecord,
            WorkerSettings settings)
        {
            if (!originalRecord.HasData)
                throw new ArgumentException("Cannot adjust a day with no data");

            if (originalRecord.VarianceMinutes <= 0)
                throw new ArgumentException("Can only convert positive variance to OT");

            if (!originalRecord.NormalSignIn.HasValue || !originalRecord.NormalSignOut.HasValue)
                throw new ArgumentException("Both sign-in and sign-out times required for variance to OT conversion");

            var currentUser = await _authService.GetCurrentSessionAsync();
            var username = currentUser?.Username ?? "System";

            var originalSignIn = originalRecord.NormalSignIn.Value;
            var originalSignOut = originalRecord.NormalSignOut.Value;
            var varianceTimeSpan = TimeSpan.FromMinutes(originalRecord.VarianceMinutes);
            var varianceHours = (decimal)originalRecord.VarianceMinutes / 60;

            // Calculate when normal work should end (expected hours from sign-in)
            var expectedNormalWorkHours = settings.ExpectedHoursPerDay;
            var expectedNormalSignOut = originalSignIn.AddHours((double)expectedNormalWorkHours);

            DailyAdjustment adjustment;

            // SCENARIO CHECK: Does worker already have OT sign-in?
            if (originalRecord.OTSignIn.HasValue)
            {
                // SCENARIO 2: Worker already signed into OT (late OT sign-in)
                // Just add the variance to existing OT hours, don't change timing
                var newOTHours = originalRecord.OTHours + varianceHours;
                var newNormalWorkHours = expectedNormalWorkHours; // Keep normal work at expected hours
                var newNormalSignOut = expectedNormalSignOut;     // Adjust normal sign-out to expected time
                var newVarianceMinutes = 0;

                // Recalculate pay
                var newDailyPay = _calculator.CalculateDailyPay(newNormalWorkHours, newOTHours, settings, true);

                adjustment = new DailyAdjustment
                {
                    Type = AdjustmentType.VarianceToOT,
                    Description = $"Add {originalRecord.VarianceMinutes} min variance to existing OT ({varianceHours:F1}h)",
                    AppliedAt = DateTime.Now,
                    AppliedBy = username,

                    // Store original values
                    OriginalWorkHours = originalRecord.WorkHours,
                    OriginalOTHours = originalRecord.OTHours,
                    OriginalOnTime = originalRecord.OnTime,
                    OriginalLateMinutes = originalRecord.LateMinutes,
                    OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                    OriginalSignOut = originalRecord.NormalSignOut,
                    OriginalSignIn = originalRecord.NormalSignIn,

                    // Adjusted values - keep existing OT timing, just increase hours
                    AdjustedWorkHours = newNormalWorkHours,
                    AdjustedOTHours = newOTHours,
                    AdjustedOnTime = originalRecord.OnTime,
                    AdjustedLateMinutes = originalRecord.LateMinutes,
                    AdjustedVarianceMinutes = newVarianceMinutes,
                    AdjustedSignOut = newNormalSignOut,
                    AdjustedSignIn = originalRecord.NormalSignIn
                };

                // Apply changes - keep existing OT timing
                originalRecord.WorkHours = newNormalWorkHours;
                originalRecord.OTHours = newOTHours;
                originalRecord.VarianceMinutes = newVarianceMinutes;
                originalRecord.DailyPay = newDailyPay;
                originalRecord.NormalSignOut = newNormalSignOut;
                // DON'T change OT timing - worker already has OT sign-in/out

                _logger.LogInformation(
                    "Added variance to existing OT for {Date} by {User}: {VarianceMinutes} min → +{VarianceHours:F1}h OT. " +
                    "Existing OT timing preserved: {OTSignIn}-{OTSignOut}",
                    originalRecord.Date.ToString("yyyy-MM-dd"),
                    username,
                    adjustment.OriginalVarianceMinutes,
                    varianceHours,
                    originalRecord.OTSignIn?.ToString("HH:mm") ?? "N/A",
                    originalRecord.OTSignOut?.ToString("HH:mm") ?? "N/A");
            }
            else
            {
                // SCENARIO 1: No existing OT sign-in - create new OT period by splitting time
                var newNormalSignOut = expectedNormalSignOut;
                var newNormalWorkHours = expectedNormalWorkHours;
                var newVarianceMinutes = 0;

                // Create OT period from end of normal work to actual sign-out
                var newOTSignIn = expectedNormalSignOut;
                var newOTSignOut = originalSignOut;
                var otDuration = newOTSignOut - newOTSignIn;
                var newOTHours = (decimal)otDuration.TotalHours;

                // Recalculate pay
                var newDailyPay = _calculator.CalculateDailyPay(newNormalWorkHours, newOTHours, settings, true);

                adjustment = new DailyAdjustment
                {
                    Type = AdjustmentType.VarianceToOT,
                    Description = $"Split variance into OT period: {newOTHours:F1}h ({originalRecord.VarianceMinutes} min)",
                    AppliedAt = DateTime.Now,
                    AppliedBy = username,

                    // Store original values
                    OriginalWorkHours = originalRecord.WorkHours,
                    OriginalOTHours = originalRecord.OTHours,
                    OriginalOnTime = originalRecord.OnTime,
                    OriginalLateMinutes = originalRecord.LateMinutes,
                    OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                    OriginalSignOut = originalRecord.NormalSignOut,
                    OriginalSignIn = originalRecord.NormalSignIn,

                    // Adjusted values with time split
                    AdjustedWorkHours = newNormalWorkHours,
                    AdjustedOTHours = newOTHours,
                    AdjustedOnTime = originalRecord.OnTime,
                    AdjustedLateMinutes = originalRecord.LateMinutes,
                    AdjustedVarianceMinutes = newVarianceMinutes,
                    AdjustedSignOut = newNormalSignOut,
                    AdjustedSignIn = originalRecord.NormalSignIn
                };

                // Apply changes with new OT timing
                originalRecord.WorkHours = newNormalWorkHours;
                originalRecord.OTHours = newOTHours;
                originalRecord.VarianceMinutes = newVarianceMinutes;
                originalRecord.DailyPay = newDailyPay;
                originalRecord.NormalSignOut = newNormalSignOut;

                // Set new OT timing
                originalRecord.OTSignIn = newOTSignIn;
                originalRecord.OTSignOut = newOTSignOut;

                _logger.LogInformation(
                    "Created new OT period from variance for {Date} by {User}: {VarianceMinutes} min variance split. " +
                    "Normal: {SignIn}-{NormalSignOut} ({NormalHours:F1}h), OT: {OTSignIn}-{OTSignOut} ({OTHours:F1}h)",
                    originalRecord.Date.ToString("yyyy-MM-dd"),
                    username,
                    adjustment.OriginalVarianceMinutes,
                    originalSignIn.ToString("HH:mm"),
                    newNormalSignOut.ToString("HH:mm"),
                    newNormalWorkHours,
                    newOTSignIn.ToString("HH:mm"),
                    newOTSignOut.ToString("HH:mm"),
                    newOTHours);
            }

            originalRecord.HasAdjustments = true;
            originalRecord.AppliedAdjustment = adjustment;

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
        /// FIXED: Properly handle active workers (no sign-out) when adjusting sign-in time
        /// </summary>
        public async Task<DailyWorkRecord> ApplyCustomAdjustmentAsync(
            DailyWorkRecord originalRecord,
            WorkerSettings settings,
            string description,
            DateTime? customSignInTime = null,   // NEW: Custom sign-in time
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
            var newSignInTime = customSignInTime ?? originalRecord.NormalSignIn;   // Use custom or original
            var newSignOutTime = customSignOutTime ?? originalRecord.NormalSignOut;
            var newOnTime = originalRecord.OnTime;
            var newLateMinutes = originalRecord.LateMinutes;

            // If custom sign-in time is provided, recalculate lateness and work hours
            if (customSignInTime.HasValue)
            {
                // Recalculate lateness based on new sign-in time
                var expectedArrival = originalRecord.Date.Add(settings.ExpectedArrivalTime);
                var timeDifference = customSignInTime.Value - expectedArrival;
                newLateMinutes = (int)timeDifference.TotalMinutes;
                newOnTime = newLateMinutes <= 0;

                // CRITICAL FIX: Recalculate work hours for both completed and active workers
                if (newSignOutTime.HasValue)
                {
                    // Completed worker: use actual sign-out time
                    var duration = newSignOutTime.Value - customSignInTime.Value;
                    newWorkHours = (decimal)duration.TotalHours;
                    _logger.LogDebug("Recalculated work hours for completed worker: {Hours:F1}h (SignIn: {SignIn}, SignOut: {SignOut})",
                        newWorkHours, customSignInTime.Value.ToString("HH:mm"), newSignOutTime.Value.ToString("HH:mm"));
                }
                else if (originalRecord.Date.Date == DateTime.Today)
                {
                    // CRITICAL FIX: Active worker (still working today) - calculate from adjusted sign-in to now
                    var currentTime = DateTime.Now;
                    var duration = currentTime - customSignInTime.Value;
                    newWorkHours = (decimal)duration.TotalHours;
                    _logger.LogInformation("Recalculated work hours for ACTIVE worker: {Hours:F1}h (SignIn: {SignIn}, Current: {CurrentTime})",
                        newWorkHours, customSignInTime.Value.ToString("HH:mm"), currentTime.ToString("HH:mm"));
                }
                else
                {
                    // Worker from a past date with no sign-out - keep original hours
                    _logger.LogDebug("Past date worker with no sign-out, keeping original work hours: {Hours:F1}h", originalRecord.WorkHours);
                }

                // Recalculate variance based on new work hours
                newVarianceMinutes = _calculator.CalculateVarianceMinutes(newWorkHours, settings.ExpectedHoursPerDay);
            }

            // If custom sign-out time is provided (and we have sign-in), recalculate work hours
            if (customSignOutTime.HasValue && newSignInTime.HasValue)
            {
                var duration = customSignOutTime.Value - newSignInTime.Value;
                newWorkHours = (decimal)duration.TotalHours;
                newVarianceMinutes = _calculator.CalculateVarianceMinutes(newWorkHours, settings.ExpectedHoursPerDay);
                _logger.LogDebug("Recalculated work hours from custom sign-out: {Hours:F1}h", newWorkHours);
            }

            // Apply on-time override if provided (this overrides calculated on-time status)
            if (overrideOnTime.HasValue)
            {
                newOnTime = overrideOnTime.Value;
                newLateMinutes = newOnTime ? 0 : newLateMinutes;
            }

            // Recalculate pay with new values
            var hasNormalActivity = newSignInTime.HasValue;
            var newDailyPay = _calculator.CalculateDailyPay(
                newWorkHours,
                newOTHours,
                settings,
                hasNormalActivity);

            // Build change summary
            var changes = new List<string>();
            if (customSignInTime.HasValue) changes.Add($"Sign-in: {originalRecord.NormalSignIn?.ToString("HH:mm") ?? "N/A"} → {customSignInTime.Value:HH:mm}");
            if (customSignOutTime.HasValue) changes.Add($"Sign-out: {originalRecord.NormalSignOut?.ToString("HH:mm") ?? "N/A"} → {customSignOutTime.Value:HH:mm}");
            if (customOTHours.HasValue) changes.Add($"OT Hours: {originalRecord.OTHours:F1} → {customOTHours.Value:F1}");
            if (overrideOnTime.HasValue) changes.Add($"On-time: {originalRecord.OnTime} → {overrideOnTime.Value}");
            if (Math.Abs(newWorkHours - originalRecord.WorkHours) > 0.1m) changes.Add($"Work Hours: {originalRecord.WorkHours:F1} → {newWorkHours:F1}");

            // Create adjustment record
            var adjustment = new DailyAdjustment
            {
                Type = AdjustmentType.CustomAdjustment,
                Description = description,
                AppliedAt = DateTime.Now,
                AppliedBy = username,

                // Store ALL original values
                OriginalWorkHours = originalRecord.WorkHours,
                OriginalOTHours = originalRecord.OTHours,
                OriginalOnTime = originalRecord.OnTime,
                OriginalLateMinutes = originalRecord.LateMinutes,
                OriginalVarianceMinutes = originalRecord.VarianceMinutes,
                OriginalSignOut = originalRecord.NormalSignOut,
                OriginalSignIn = originalRecord.NormalSignIn,

                // Set adjusted values
                AdjustedWorkHours = newWorkHours,
                AdjustedOTHours = newOTHours,
                AdjustedOnTime = newOnTime,
                AdjustedLateMinutes = newLateMinutes,
                AdjustedVarianceMinutes = newVarianceMinutes,
                AdjustedSignOut = newSignOutTime,
                AdjustedSignIn = newSignInTime
                // ✅ ChangeSummary will be automatically calculated by the property
            };

            // Apply all changes to the record
            originalRecord.WorkHours = newWorkHours;
            originalRecord.OTHours = newOTHours;
            originalRecord.VarianceMinutes = newVarianceMinutes;
            originalRecord.OnTime = newOnTime;
            originalRecord.LateMinutes = newLateMinutes;
            originalRecord.DailyPay = newDailyPay;
            // NOTE: DO NOT change NormalSignIn/NormalSignOut - preserve originals for analytics
            originalRecord.HasAdjustments = true;
            originalRecord.AppliedAdjustment = adjustment;

            _logger.LogInformation(
                "Applied custom adjustment for {Date} by {User}: {Description}. " +
                "Changes: {Changes}. Work hours: {OriginalHours:F1} → {NewHours:F1}",
                originalRecord.Date.ToString("yyyy-MM-dd"),
                username,
                description,
                string.Join(", ", changes),
                adjustment.OriginalWorkHours,
                newWorkHours);

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