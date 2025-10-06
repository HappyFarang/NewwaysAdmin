// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerPaymentCalculator.cs
// Purpose: Pure calculation logic for worker payments and variance tracking

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
        /// Calculate daily payment based on work hours and settings
        /// We pay full daily rate regardless of minutes variance
        /// </summary>
        public decimal CalculateDailyPay(
            decimal workHours,
            decimal otHours,
            WorkerSettings settings)
        {
            // Base daily pay (full amount regardless of small variance)
            var basePay = settings.DailyPayRate;

            // OT pay (rounded to nearest hour, paid per hour)
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
        /// Calculate work hours from a DailyWorkCycle
        /// </summary>
        public decimal CalculateWorkHours(DailyWorkCycle cycle)
        {
            // Get Normal work records
            var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
            if (!normalRecords.Any()) return 0;

            var normalCheckIn = normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn);
            var normalCheckOut = normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckOut);

            if (normalCheckIn == null || normalCheckOut == null) return 0;

            var duration = normalCheckOut.Timestamp - normalCheckIn.Timestamp;
            return (decimal)duration.TotalHours;
        }

        /// <summary>
        /// Calculate OT hours from a DailyWorkCycle
        /// </summary>
        public decimal CalculateOTHours(DailyWorkCycle cycle)
        {
            if (!cycle.HasOT) return 0;

            // Get OT work records
            var otRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.OT).ToList();
            if (!otRecords.Any()) return 0;

            var otCheckIn = otRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn);
            var otCheckOut = otRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckOut);

            if (otCheckIn == null || otCheckOut == null) return 0;

            var duration = otCheckOut.Timestamp - otCheckIn.Timestamp;
            return (decimal)duration.TotalHours;
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
            var dailyPay = CalculateDailyPay(workHours, otHours, settings);

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