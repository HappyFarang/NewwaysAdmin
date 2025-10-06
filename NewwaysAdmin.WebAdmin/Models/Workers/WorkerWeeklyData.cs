// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerWeeklyData.cs
// Purpose: Weekly summary for a worker including all daily records and totals

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
        /// </summary>
        public List<DailyWorkRecord> DailyRecords { get; set; } = new();

        /// <summary>
        /// Total work hours for the week
        /// </summary>
        public decimal TotalWorkHours { get; set; }

        /// <summary>
        /// Total overtime hours for the week
        /// </summary>
        public decimal TotalOTHours { get; set; }

        /// <summary>
        /// Total pay for the week (all days combined)
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
        /// On-time percentage for the week
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
    }
}