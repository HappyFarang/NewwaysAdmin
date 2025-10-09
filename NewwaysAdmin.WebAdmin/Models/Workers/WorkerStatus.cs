// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerStatus.cs
// Purpose: Represents a worker's current status for dashboard display
// IMPROVED: Shows cycle date when displaying historical data

using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class WorkerStatus
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime LastActivity { get; set; }
        public DateTime CycleDate { get; set; } // The date of the work cycle
        public WorkCycle CurrentCycle { get; set; }
        public bool HasOT { get; set; }
        public TimeSpan? CurrentDuration { get; set; }
        public bool ShowDate { get; set; } // Flag to show date in UI for historical data

        // Additional details for inactive workers
        public DateTime? NormalSignIn { get; set; }
        public DateTime? NormalSignOut { get; set; }
        public TimeSpan? NormalHoursWorked { get; set; }

        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }
        public TimeSpan? OTHoursWorked { get; set; }

        // Helper property to check if worker has ANY activity
        public bool HasActivity => LastActivity != DateTime.MinValue;

        // Helper property to check if normal shift was completed
        public bool HasCompletedNormalShift => NormalSignIn.HasValue && NormalSignOut.HasValue;

        // Helper property to check if OT shift was completed
        public bool HasCompletedOTShift => OTSignIn.HasValue && OTSignOut.HasValue;

        // Helper to check if this is today's data
        public bool IsToday => CycleDate.Date == DateTime.Today;

        // Formatted helpers for UI
        public string LastActivityFormatted => HasActivity
            ? LastActivity.ToString("HH:mm")
            : "No activity";

        /// <summary>
        /// Whether this worker has adjustments applied for today
        /// </summary>
        public bool HasAdjustments { get; set; }

        /// <summary>
        /// Tooltip showing adjustment details
        /// </summary>
        public string AdjustmentTooltip { get; set; } = string.Empty;

        public string CycleDateFormatted => CycleDate.ToString("MMM dd, yyyy");

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