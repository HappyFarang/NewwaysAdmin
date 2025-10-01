// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerStatus.cs
// Purpose: Represents a worker's current status for dashboard display

using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class WorkerStatus
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime LastActivity { get; set; }
        public WorkCycle CurrentCycle { get; set; }
        public bool HasOT { get; set; }
        public TimeSpan? CurrentDuration { get; set; }

        // Additional details for inactive workers
        public DateTime? NormalSignIn { get; set; }
        public DateTime? NormalSignOut { get; set; }
        public TimeSpan? NormalHoursWorked { get; set; }

        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }
        public TimeSpan? OTHoursWorked { get; set; }

        // Formatted helpers for UI
        public string LastActivityFormatted => LastActivity.ToString("HH:mm");
        public string DurationFormatted => CurrentDuration?.ToString(@"hh\:mm") ?? "--:--";
        public string CycleDisplay => CurrentCycle == WorkCycle.OT ? "OT" : "Normal";

        public string NormalSignInFormatted => NormalSignIn?.ToString("HH:mm") ?? "--:--";
        public string NormalSignOutFormatted => NormalSignOut?.ToString("HH:mm") ?? "--:--";
        public string NormalHoursFormatted => NormalHoursWorked?.ToString(@"hh\:mm") ?? "--:--";

        public string OTSignInFormatted => OTSignIn?.ToString("HH:mm") ?? "--:--";
        public string OTSignOutFormatted => OTSignOut?.ToString("HH:mm") ?? "--:--";
        public string OTHoursFormatted => OTHoursWorked?.ToString(@"hh\:mm") ?? "--:--";
    }
}