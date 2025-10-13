// File: NewwaysAdmin.WebAdmin/Models/Workers/WeeklyTableRow.cs
// Purpose: Pure data container for one day - no calculations or formatting

using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    /// <summary>
    /// Pure data model for one row (day) in the weekly table
    /// Contains only raw data - no calculations or display logic
    /// </summary>
    public class WeeklyTableRow
    {
        // Day identification
        public DayOfWeek DayOfWeek { get; set; }
        public DateTime Date { get; set; }

        // Direct data from WorkerDataService
        public WorkerDisplayData? WorkerData { get; set; }  // Null if no data for this day

        // Settings data (needed for calculations)
        public WorkerSettings? Settings { get; set; }

        // Simple computed properties (no business logic)
        public bool HasData => WorkerData != null;
        public string DayName => DayOfWeek.ToString();
    }
}