// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerSettings.cs
// Purpose: Configuration settings for individual workers (pay rates, schedules, etc.)

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class WorkerSettings
    {
        /// <summary>
        /// Worker ID - matches the ID from WorkerAttendance system
        /// </summary>
        public int WorkerId { get; set; }

        /// <summary>
        /// Worker name - synced from WorkerAttendance for reference
        /// </summary>
        public string WorkerName { get; set; } = string.Empty;

        /// <summary>
        /// Expected hours per day (e.g., 8.0 for 8 hours)
        /// </summary>
        public decimal ExpectedHoursPerDay { get; set; } = 8.0m;

        /// <summary>
        /// Expected arrival time (e.g., 08:00:00 for 8 AM)
        /// </summary>
        public TimeSpan ExpectedArrivalTime { get; set; } = new TimeSpan(8, 0, 0);

        /// <summary>
        /// Base daily pay rate in THB
        /// </summary>
        public decimal DailyPayRate { get; set; } = 350m;

        /// <summary>
        /// Overtime pay rate per hour in THB
        /// </summary>
        public decimal OvertimeHourlyRate { get; set; } = 50m;

        /// <summary>
        /// When these settings were created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Last time settings were updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}