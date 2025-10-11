// File: NewwaysAdmin.WebAdmin/Services/Workers/TimeCalculator.cs
// Purpose: Pure calculation functions that work with any time data (raw or adjusted)
// Agnostic to data source - just does math on times

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    /// <summary>
    /// Pure calculation functions for worker time data
    /// Works with any time values regardless of whether they're raw or adjusted
    /// </summary>
    public class TimeCalculator
    {
        private readonly ILogger<TimeCalculator> _logger;

        public TimeCalculator(ILogger<TimeCalculator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Calculate work hours between two times
        /// Returns 0 if either time is null
        /// </summary>
        public decimal CalculateHoursBetween(DateTime? startTime, DateTime? endTime)
        {
            if (!startTime.HasValue || !endTime.HasValue)
                return 0;

            if (endTime <= startTime)
                return 0;

            var duration = endTime.Value - startTime.Value;
            return (decimal)duration.TotalHours;
        }

        /// <summary>
        /// Calculate total work hours (normal + OT)
        /// </summary>
        public decimal CalculateTotalWorkHours(DateTime? signIn, DateTime? signOut, DateTime? otSignIn, DateTime? otSignOut)
        {
            var normalHours = CalculateHoursBetween(signIn, signOut);
            var otHours = CalculateHoursBetween(otSignIn, otSignOut);
            return normalHours + otHours;
        }

        /// <summary>
        /// Calculate normal work hours only
        /// </summary>
        public decimal CalculateNormalWorkHours(DateTime? signIn, DateTime? signOut)
        {
            return CalculateHoursBetween(signIn, signOut);
        }

        /// <summary>
        /// Calculate OT work hours only
        /// </summary>
        public decimal CalculateOTWorkHours(DateTime? otSignIn, DateTime? otSignOut)
        {
            return CalculateHoursBetween(otSignIn, otSignOut);
        }

        /// <summary>
        /// Calculate variance in minutes from expected hours
        /// Positive = worked more than expected, Negative = worked less
        /// </summary>
        public int CalculateVarianceMinutes(decimal actualHours, decimal expectedHours)
        {
            var varianceHours = actualHours - expectedHours;
            return (int)(varianceHours * 60);
        }

        /// <summary>
        /// Check if worker is late based on sign-in time and expected arrival
        /// </summary>
        public bool IsLate(DateTime? signInTime, TimeSpan expectedArrivalTime)
        {
            if (!signInTime.HasValue)
                return false; // No sign-in = not late (no data)

            var expectedArrival = signInTime.Value.Date.Add(expectedArrivalTime);
            return signInTime.Value > expectedArrival;
        }

        /// <summary>
        /// Calculate late minutes if worker is late
        /// Returns 0 if not late or no sign-in time
        /// </summary>
        public int CalculateLateMinutes(DateTime? signInTime, TimeSpan expectedArrivalTime)
        {
            if (!IsLate(signInTime, expectedArrivalTime))
                return 0;

            var expectedArrival = signInTime!.Value.Date.Add(expectedArrivalTime);
            var lateDuration = signInTime.Value - expectedArrival;
            return Math.Max(0, (int)lateDuration.TotalMinutes);
        }

        /// <summary>
        /// Check if worker is on time (not late)
        /// </summary>
        public bool IsOnTime(DateTime? signInTime, TimeSpan expectedArrivalTime)
        {
            return !IsLate(signInTime, expectedArrivalTime);
        }

        /// <summary>
        /// Calculate daily pay based on work hours and settings
        /// </summary>
        public decimal CalculateDailyPay(decimal normalHours, decimal otHours, decimal hourlyRate, decimal otMultiplier)
        {
            var normalPay = normalHours * hourlyRate;
            var otPay = otHours * hourlyRate * otMultiplier;
            return normalPay + otPay;
        }

        /// <summary>
        /// Calculate normal pay portion only
        /// </summary>
        public decimal CalculateNormalPay(decimal normalHours, decimal hourlyRate)
        {
            return normalHours * hourlyRate;
        }

        /// <summary>
        /// Calculate OT pay portion only
        /// </summary>
        public decimal CalculateOTPay(decimal otHours, decimal hourlyRate, decimal otMultiplier)
        {
            return otHours * hourlyRate * otMultiplier;
        }

        /// <summary>
        /// Check if worker has any work activity (has sign-in time)
        /// </summary>
        public bool HasWorkActivity(DateTime? signInTime)
        {
            return signInTime.HasValue;
        }

        /// <summary>
        /// Check if worker has OT activity
        /// </summary>
        public bool HasOTActivity(DateTime? otSignInTime)
        {
            return otSignInTime.HasValue;
        }

        /// <summary>
        /// Check if worker is currently working (signed in but not signed out)
        /// Only works for today's date
        /// </summary>
        public bool IsCurrentlyWorking(DateTime? signInTime, DateTime? signOutTime, DateTime? otSignInTime, DateTime? otSignOutTime, DateTime date)
        {
            // Only check for today
            if (date.Date != DateTime.Today)
                return false;

            // Check normal shift
            if (signInTime.HasValue && !signOutTime.HasValue)
                return true;

            // Check OT shift
            if (otSignInTime.HasValue && !otSignOutTime.HasValue)
                return true;

            return false;
        }

        /// <summary>
        /// Calculate current work duration for active workers
        /// Returns duration from sign-in to now
        /// </summary>
        public TimeSpan CalculateCurrentWorkDuration(DateTime? signInTime, DateTime? signOutTime, DateTime? otSignInTime, DateTime? otSignOutTime)
        {
            var now = DateTime.Now;

            // If in OT and not signed out
            if (otSignInTime.HasValue && !otSignOutTime.HasValue)
            {
                return now - otSignInTime.Value;
            }

            // If in normal shift and not signed out
            if (signInTime.HasValue && !signOutTime.HasValue)
            {
                return now - signInTime.Value;
            }

            return TimeSpan.Zero;
        }
    }
}