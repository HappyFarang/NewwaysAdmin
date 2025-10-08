// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerWeeklyData.cs
// Purpose: Weekly summary for a worker including all daily records, totals, and adjustment tracking

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class WorkerWeeklyData
    {
        /// <summary>
        /// Worker ID
        /// </summary>
        public int WorkerId { get; set; }

        /// <summary>
        /// Worker name
        /// </summary>
        public string WorkerName { get; set; } = string.Empty;

        /// <summary>
        /// Week start date (always a Sunday)
        /// </summary>
        public DateTime WeekStartDate { get; set; }

        /// <summary>
        /// Week number in the year
        /// </summary>
        public int WeekNumber { get; set; }

        /// <summary>
        /// Year for this week
        /// </summary>
        public int Year { get; set; }

        /// <summary>
        /// All 7 days of the week (Sunday to Saturday)
        /// Contains final adjusted values as ground truth
        /// </summary>
        public List<DailyWorkRecord> DailyRecords { get; set; } = new();

        /// <summary>
        /// Total work hours for the week (sum of adjusted daily values)
        /// </summary>
        public decimal TotalWorkHours { get; set; }

        /// <summary>
        /// Total overtime hours for the week (sum of adjusted daily values)
        /// </summary>
        public decimal TotalOTHours { get; set; }

        /// <summary>
        /// Total pay for the week (sum of adjusted daily values)
        /// </summary>
        public decimal TotalPay { get; set; }

        /// <summary>
        /// Number of days worked this week
        /// </summary>
        public int DaysWorked { get; set; }

        /// <summary>
        /// Average work hours per day (only counting days worked)
        /// </summary>
        public decimal AverageWorkHoursPerDay => DaysWorked > 0 ? TotalWorkHours / DaysWorked : 0;

        /// <summary>
        /// On-time percentage for the week (based on adjusted on-time status)
        /// </summary>
        public decimal OnTimePercentage { get; set; }

        /// <summary>
        /// When this summary was generated
        /// </summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Settings snapshot at time of generation
        /// </summary>
        public WorkerSettings? SettingsSnapshot { get; set; }

        // === NEW: ADJUSTMENT TRACKING ===
        /// <summary>
        /// Total number of days that have been manually adjusted this week
        /// </summary>
        public int TotalAdjustmentsMade { get; set; }

        /// <summary>
        /// Brief summary of adjustments made for quick overview
        /// Examples: ["Mon: Worker on time", "Wed: Convert variance to OT", "Fri: Manual sign-out"]
        /// </summary>
        public List<string> AdjustmentSummary { get; set; } = new();

        /// <summary>
        /// Whether this week has any adjustments applied
        /// </summary>
        public bool HasAdjustments => TotalAdjustmentsMade > 0;

        /// <summary>
        /// Percentage of worked days that required adjustments
        /// </summary>
        public decimal AdjustmentPercentage => DaysWorked > 0 ? (decimal)TotalAdjustmentsMade / DaysWorked * 100 : 0;

        // === HELPER METHODS FOR ADJUSTMENT ANALYSIS ===
        /// <summary>
        /// Get all days that have been adjusted
        /// </summary>
        public List<DailyWorkRecord> GetAdjustedDays()
        {
            return DailyRecords.Where(d => d.HasAdjustments).ToList();
        }

        /// <summary>
        /// Get summary of adjustment types used this week
        /// </summary>
        public Dictionary<AdjustmentType, int> GetAdjustmentTypeBreakdown()
        {
            return DailyRecords
                .Where(d => d.HasAdjustments && d.AppliedAdjustment != null)
                .GroupBy(d => d.AppliedAdjustment!.Type)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        /// <summary>
        /// Update adjustment summary after modifications
        /// Call this whenever adjustments are added/modified
        /// </summary>
        public void RefreshAdjustmentSummary()
        {
            var adjustedDays = GetAdjustedDays();
            TotalAdjustmentsMade = adjustedDays.Count;

            AdjustmentSummary = adjustedDays
                .Select(d => $"{d.DayOfWeek}: {d.AppliedAdjustment?.Description ?? "Unknown adjustment"}")
                .ToList();
        }

        /// <summary>
        /// Recalculate weekly totals from daily records
        /// Call this after applying adjustments to ensure totals are accurate
        /// </summary>
        public void RecalculateWeeklyTotals()
        {
            var recordsWithData = DailyRecords.Where(r => r.HasData).ToList();

            TotalWorkHours = recordsWithData.Sum(r => r.WorkHours);
            TotalOTHours = recordsWithData.Sum(r => r.OTHours);
            TotalPay = recordsWithData.Sum(r => r.DailyPay);
            DaysWorked = recordsWithData.Count;

            var onTimeCount = recordsWithData.Count(r => r.OnTime);
            OnTimePercentage = DaysWorked > 0 ? (decimal)onTimeCount / DaysWorked * 100 : 0;

            // Update adjustment tracking
            RefreshAdjustmentSummary();
        }
    }
}