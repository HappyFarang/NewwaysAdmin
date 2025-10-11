// File: NewwaysAdmin.WebAdmin/Models/Workers/WorkerTimeData.cs
// Purpose: Standalone data models for worker attendance time processing
// Extracted from WorkerDataService to proper model location

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    /// <summary>
    /// Raw time data directly from attendance files (no adjustments)
    /// </summary>
    public class RawTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public DateTime? SignIn { get; set; }
        public DateTime? SignOut { get; set; }
        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }
        public bool HasData { get; set; }
    }

    /// <summary>
    /// Adjustment time data (only what was manually changed)
    /// </summary>
    public class AdjustmentTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public bool HasAdjustments { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime? AdjustedSignIn { get; set; }
        public DateTime? AdjustedSignOut { get; set; }
        public DateTime? AdjustedOTSignIn { get; set; }
        public DateTime? AdjustedOTSignOut { get; set; }
    }

    /// <summary>
    /// Final time data for display (adjusted times take precedence)
    /// </summary>
    public class FinalTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public bool HasAdjustments { get; set; }
        public DateTime? SignIn { get; set; }
        public DateTime? SignOut { get; set; }
        public DateTime? OTSignIn { get; set; }
        public DateTime? OTSignOut { get; set; }
    }

    /// <summary>
    /// Complete data package - everything together for analysis
    /// </summary>
    public class CompleteTimeData
    {
        public string AttendanceFileName { get; set; } = string.Empty;
        public RawTimeData Raw { get; set; } = new();
        public AdjustmentTimeData Adjustments { get; set; } = new();
        public FinalTimeData Final { get; set; } = new();
    }
}