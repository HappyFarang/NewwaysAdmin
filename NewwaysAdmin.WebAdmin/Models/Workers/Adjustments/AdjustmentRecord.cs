// File: NewwaysAdmin.WebAdmin/Models/Workers/Adjustments/DailyAdjustmentRecord.cs
// Purpose: Simple adjustment record - only stores what was manually adjusted

namespace NewwaysAdmin.WebAdmin.Models.Workers.Adjustments
{
    /// <summary>
    /// Simple daily adjustment record - only stores manual time changes
    /// Links to raw attendance data via filename ID
    /// </summary>
    public class DailyAdjustmentRecord
    {
        /// <summary>
        /// Full filename of the raw attendance file this adjustment links to
        /// Example: "2024-10-09_Worker3.json.json"
        /// </summary>
        public string AttendanceFileName { get; set; } = string.Empty;

        /// <summary>
        /// Whether any adjustments have been made to this day
        /// </summary>
        public bool HasAdjustments { get; set; } = false;

        /// <summary>
        /// Description of what was adjusted (optional, for reference)
        /// </summary>
        public string Description { get; set; } = string.Empty;

        // === ADJUSTED TIMES (only what was manually changed) ===

        /// <summary>
        /// Adjusted sign-in time (null = no change)
        /// </summary>
        public DateTime? AdjustedSignIn { get; set; }

        /// <summary>
        /// Adjusted sign-out time (null = no change)
        /// </summary>
        public DateTime? AdjustedSignOut { get; set; }

        /// <summary>
        /// Adjusted OT sign-in time (null = no change)
        /// </summary>
        public DateTime? AdjustedOTSignIn { get; set; }

        /// <summary>
        /// Adjusted OT sign-out time (null = no change)
        /// </summary>
        public DateTime? AdjustedOTSignOut { get; set; }

        // === HELPER METHODS ===

        /// <summary>
        /// Get the file ID (filename without .json.json extension) for linking
        /// Example: "2024-10-09_Worker3.json.json" → "2024-10-09_Worker3"
        /// </summary>
        public string GetAttendanceFileId()
        {
            return AttendanceFileName.Replace(".json.json", "");
        }

        /// <summary>
        /// Get filename for storing this adjustment
        /// Format: adjustments_{attendanceFileId}.json
        /// Example: "adjustments_2024-10-09_Worker3.json"
        /// </summary>
        public string GetFileName()
        {
            return $"adjustments_{GetAttendanceFileId()}.json";
        }

        /// <summary>
        /// Get filename for storing adjustment (static version)
        /// Format: adjustments_{attendanceFileId}.json
        /// </summary>
        public static string GetFileName(string attendanceFileName)
        {
            var fileId = attendanceFileName.Replace(".json.json", "");
            return $"adjustments_{fileId}.json";
        }

        /// <summary>
        /// Create a default (empty) adjustment record
        /// </summary>
        public static DailyAdjustmentRecord CreateDefault(string attendanceFileName)
        {
            return new DailyAdjustmentRecord
            {
                AttendanceFileName = attendanceFileName,
                HasAdjustments = false
            };
        }

        /// <summary>
        /// Clear all adjustments and reset to default state
        /// </summary>
        public void ClearAdjustments()
        {
            HasAdjustments = false;
            Description = string.Empty;
            AdjustedSignIn = null;
            AdjustedSignOut = null;
            AdjustedOTSignIn = null;
            AdjustedOTSignOut = null;
        }

        /// <summary>
        /// Check if any times have been adjusted
        /// </summary>
        public bool HasAnyTimeAdjustments()
        {
            return AdjustedSignIn.HasValue ||
                   AdjustedSignOut.HasValue ||
                   AdjustedOTSignIn.HasValue ||
                   AdjustedOTSignOut.HasValue;
        }

        /// <summary>
        /// Get a simple summary of what was changed
        /// </summary>
        public string GetChangeSummary()
        {
            if (!HasAdjustments) return "No adjustments";

            var changes = new List<string>();

            if (AdjustedSignIn.HasValue)
                changes.Add($"Sign-in: {AdjustedSignIn.Value:HH:mm}");

            if (AdjustedSignOut.HasValue)
                changes.Add($"Sign-out: {AdjustedSignOut.Value:HH:mm}");

            if (AdjustedOTSignIn.HasValue)
                changes.Add($"OT Sign-in: {AdjustedOTSignIn.Value:HH:mm}");

            if (AdjustedOTSignOut.HasValue)
                changes.Add($"OT Sign-out: {AdjustedOTSignOut.Value:HH:mm}");

            return changes.Count > 0 ? string.Join(", ", changes) : Description;
        }
    }
}