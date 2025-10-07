// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerPaymentCalculator.cs
// Purpose: Pure calculation logic for worker payments and variance tracking
// FIXED: 1) Handles active workers who haven't signed out yet
//        2) Pays FULL daily rate for any Normal work activity (per day, not per hour)

using NewwaysAdmin.WebAdmin.Models.Workers;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerPaymentCalculator
    {
        private readonly ILogger<WorkerPaymentCalculator> _logger;

        public WorkerPaymentCalculator(ILogger<WorkerPaymentCalculator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Calculate daily payment based on work activity and settings
        /// PAYMENT MODEL: Full daily rate for any Normal work activity (per day, not per hour)
        /// OT is still calculated per hour and rounded
        /// </summary>
        public decimal CalculateDailyPay(
            decimal workHours,
            decimal otHours,
            WorkerSettings settings,
            bool hasNormalWorkActivity)
        {
            // Base daily pay: FULL amount if worker had any Normal work activity
            // Later we'll add adjustments for half-days, but for now it's binary
            var basePay = hasNormalWorkActivity ? settings.DailyPayRate : 0m;

            // OT pay: rounded to nearest hour, paid per hour
            var otPay = Math.Round(otHours, 0) * settings.OvertimeHourlyRate;

            return basePay + otPay;
        }

        /// <summary>
        /// Calculate variance in minutes from expected hours
        /// Positive = worked extra, Negative = left early
        /// </summary>
        public int CalculateVarianceMinutes(
            decimal actualHours,
            decimal expectedHours)
        {
            var difference = actualHours - expectedHours;
            return (int)(difference * 60); // Convert to minutes
        }

        /// <summary>
        /// Determine if worker arrived on time
        /// </summary>
        public bool IsOnTime(DateTime? actualArrival, TimeSpan expectedArrival)
        {
            if (actualArrival == null) return false;

            var actualTime = actualArrival.Value.TimeOfDay;

            // Allow 5 minute grace period
            var gracePeriod = TimeSpan.FromMinutes(5);
            var latestAcceptable = expectedArrival.Add(gracePeriod);

            return actualTime <= latestAcceptable;
        }

        /// <summary>
        /// Calculate how many minutes late (or early if negative)
        /// </summary>
        public int CalculateLateMinutes(DateTime? actualArrival, TimeSpan expectedArrival)
        {
            if (actualArrival == null) return 0;

            var actualTime = actualArrival.Value.TimeOfDay;
            var difference = actualTime - expectedArrival;

            return (int)difference.TotalMinutes;
        }

        /// <summary>
        /// Check if there is any Normal work activity (used for daily pay calculation)
        /// </summary>
        public bool HasNormalWorkActivity(DailyWorkCycle cycle)
        {
            var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
            // Any Normal check-in means they worked (even if they're still working)
            return normalRecords.Any(r => r.Type == AttendanceType.CheckIn);
        }

        /// <summary>
        /// Calculate work hours from a DailyWorkCycle
        /// FIXED: Now handles active workers who are currently checked in
        /// </summary>
        public decimal CalculateWorkHours(DailyWorkCycle cycle)
        {
            // Get Normal work records
            var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
            if (!normalRecords.Any()) return 0;

            var normalCheckIn = normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn);
            var normalCheckOut = normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckOut);

            if (normalCheckIn == null) return 0;

            // FIXED: If checked in but not checked out yet, calculate hours up to now
            if (normalCheckOut == null)
            {
                // Worker is currently working - calculate from check-in to now
                var duration = DateTime.Now - normalCheckIn.Timestamp;
                return (decimal)duration.TotalHours;
            }

            // Normal case: both check-in and check-out exist
            var completedDuration = normalCheckOut.Timestamp - normalCheckIn.Timestamp;
            return (decimal)completedDuration.TotalHours;
        }

        /// <summary>
        /// Calculate OT hours from a DailyWorkCycle
        /// FIXED: Now handles active OT workers who are currently checked in
        /// </summary>
        public decimal CalculateOTHours(DailyWorkCycle cycle)
        {
            if (!cycle.HasOT) return 0;

            // Get OT work records
            var otRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.OT).ToList();
            if (!otRecords.Any()) return 0;

            var otCheckIn = otRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn);
            var otCheckOut = otRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckOut);

            if (otCheckIn == null) return 0;

            // FIXED: If checked in but not checked out yet, calculate hours up to now
            if (otCheckOut == null)
            {
                // Worker is currently working OT - calculate from check-in to now
                var duration = DateTime.Now - otCheckIn.Timestamp;
                return (decimal)duration.TotalHours;
            }

            // Normal case: both check-in and check-out exist
            var completedDuration = otCheckOut.Timestamp - otCheckIn.Timestamp;
            return (decimal)completedDuration.TotalHours;
        }

        /// <summary>
        /// Get normal shift sign-in time
        /// </summary>
        private DateTime? GetNormalSignIn(DailyWorkCycle cycle)
        {
            var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
            return normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn)?.Timestamp;
        }

        /// <summary>
        /// Create a complete daily record from a cycle and settings
        /// </summary>
        public DailyWorkRecord CreateDailyRecord(
                DateTime date,
                DailyWorkCycle? cycle,
                WorkerSettings settings)
        {
            // If no cycle OR cycle has no records, it's an empty day
            if (cycle == null || !cycle.Records.Any())
            {
                return new DailyWorkRecord
                {
                    Date = date,
                    HasData = false
                };
            }

            var workHours = CalculateWorkHours(cycle);
            var otHours = CalculateOTHours(cycle);
            var varianceMinutes = CalculateVarianceMinutes(workHours, settings.ExpectedHoursPerDay);
            var normalSignIn = GetNormalSignIn(cycle);
            var onTime = IsOnTime(normalSignIn, settings.ExpectedArrivalTime);
            var lateMinutes = CalculateLateMinutes(normalSignIn, settings.ExpectedArrivalTime);

            // FIXED: Pass hasNormalWorkActivity flag for proper daily pay calculation
            var hasNormalWorkActivity = HasNormalWorkActivity(cycle);
            var dailyPay = CalculateDailyPay(workHours, otHours, settings, hasNormalWorkActivity);

            return new DailyWorkRecord
            {
                Date = date,
                WorkHours = workHours,
                OTHours = otHours,
                VarianceMinutes = varianceMinutes,
                OnTime = onTime,
                LateMinutes = lateMinutes,
                DailyPay = dailyPay,
                HasData = true
            };
        }

        /// <summary>
        /// Calculate weekly statistics from daily records
        /// </summary>
        public (decimal totalWork, decimal totalOT, decimal totalPay, int daysWorked, decimal onTimePercentage)
            CalculateWeeklyTotals(List<DailyWorkRecord> dailyRecords)
        {
            var recordsWithData = dailyRecords.Where(r => r.HasData).ToList();

            var totalWork = recordsWithData.Sum(r => r.WorkHours);
            var totalOT = recordsWithData.Sum(r => r.OTHours);
            var totalPay = recordsWithData.Sum(r => r.DailyPay);
            var daysWorked = recordsWithData.Count;
            var onTimeCount = recordsWithData.Count(r => r.OnTime);
            var onTimePercentage = daysWorked > 0 ? (decimal)onTimeCount / daysWorked * 100 : 0;

            return (totalWork, totalOT, totalPay, daysWorked, onTimePercentage);
        }
    }
}