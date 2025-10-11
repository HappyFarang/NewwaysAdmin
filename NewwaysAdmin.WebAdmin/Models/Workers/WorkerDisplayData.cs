// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerDisplayData.cs
// Purpose: Official display model for worker data - used across all views

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    /// <summary>
    /// Complete display data for a worker's day - everything a table row needs
    /// Used by WorkerActivityMain, WorkerDetails, and other display components
    /// </summary>
    public class WorkerDisplayData
    {
        // Worker identification
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = string.Empty;
        public string AttendanceFileName { get; set; } = string.Empty;

        // Times (adjusted if available, otherwise raw)
        public DateTime? SignIn { get; set; }
        public DateTime? SignOut { get; set; }
        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }

        // Calculated values
        public decimal NormalWorkHours { get; set; }
        public decimal OTWorkHours { get; set; }
        public decimal TotalWorkHours { get; set; }
        public bool IsLate { get; set; }
        public int LateMinutes { get; set; }
        public bool IsOnTime { get; set; }
        public int VarianceMinutes { get; set; }

        // Status flags
        public bool HasAdjustments { get; set; }
        public string AdjustmentDescription { get; set; } = string.Empty;
        public bool HasWorkActivity { get; set; }
        public bool HasOTActivity { get; set; }

        // Activity-specific fields (for WorkerActivityMain)
        public TimeSpan CurrentDuration { get; set; }
        public bool IsCurrentlyWorking { get; set; }

        // Error handling
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        // Formatted helpers for UI display
        public string SignInFormatted => SignIn?.ToString("HH:mm") ?? "--:--";
        public string SignOutFormatted => SignOut?.ToString("HH:mm") ?? "--:--";
        public string OTSignInFormatted => OTSignIn?.ToString("HH:mm") ?? "--:--";
        public string OTSignOutFormatted => OTSignOut?.ToString("HH:mm") ?? "--:--";
        public string VarianceFormatted => VarianceMinutes == 0 ? "0 min" : $"{(VarianceMinutes > 0 ? "+" : "")}{VarianceMinutes} min";

        /// <summary>
        /// Get CSS class for table rows based on status
        /// </summary>
        public string GetRowCssClass()
        {
            if (HasError) return "table-danger";
            if (HasAdjustments) return "table-warning";
            return "";
        }

        /// <summary>
        /// Get status badge text for current activity
        /// </summary>
        public string GetStatusBadge()
        {
            if (!HasWorkActivity) return "No Activity";
            if (IsCurrentlyWorking)
            {
                return HasOTActivity ? "Working OT" : "Working Normal";
            }
            return "Completed";
        }

        /// <summary>
        /// Get status badge CSS class
        /// </summary>
        public string GetStatusBadgeClass()
        {
            if (!HasWorkActivity) return "badge bg-secondary";
            if (IsCurrentlyWorking)
            {
                return HasOTActivity ? "badge bg-info" : "badge bg-success";
            }
            return "badge bg-light text-dark";
        }
    }
}