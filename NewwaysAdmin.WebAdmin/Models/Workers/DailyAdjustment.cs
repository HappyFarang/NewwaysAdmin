// File: NewwaysAdmin.WebAdmin/Models/Workers/DailyAdjustment.cs
// Purpose: Represents a single adjustment made to a worker's daily record

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public enum AdjustmentType
    {
        OnTimeOverride,      // "Worker on time" - override late status due to coffee collection
        VarianceToOT,        // Convert positive variance to overtime hours
        ManualSignOut,       // Admin sign-out for forgotten sign-out
        ManualTimeEntry,     // Manual adjustment of work hours/times
        CustomAdjustment     // Other manual adjustments with description
    }

    public class DailyAdjustment
    {
        /// <summary>
        /// Type of adjustment made
        /// </summary>
        public AdjustmentType Type { get; set; }

        /// <summary>
        /// Human-readable description of the adjustment
        /// Examples: "Worker on time - collecting coffee", "Convert 1hr variance to OT"
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// When this adjustment was applied
        /// </summary>
        public DateTime AppliedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Username of who made the adjustment
        /// </summary>
        public string AppliedBy { get; set; } = string.Empty;

        // === ORIGINAL VALUES (before adjustment) ===
        /// <summary>
        /// Original work hours before adjustment
        /// </summary>
        public decimal OriginalWorkHours { get; set; }

        /// <summary>
        /// Original OT hours before adjustment
        /// </summary>
        public decimal OriginalOTHours { get; set; }

        /// <summary>
        /// Original on-time status before adjustment
        /// </summary>
        public bool OriginalOnTime { get; set; }

        /// <summary>
        /// Original variance minutes before adjustment
        /// </summary>
        public int OriginalVarianceMinutes { get; set; }

        /// <summary>
        /// Original late minutes before adjustment
        /// </summary>
        public int OriginalLateMinutes { get; set; }

        /// <summary>
        /// Original sign-out time before adjustment (if applicable)
        /// </summary>
        public DateTime? OriginalSignOut { get; set; }

        /// <summary>
        /// Original sign-in time before adjustment (for timing delta preservation)
        /// </summary>
        public DateTime? OriginalSignIn { get; set; }

        // === ADJUSTED VALUES (final values after adjustment) ===
        /// <summary>
        /// Final work hours after adjustment
        /// </summary>
        public decimal AdjustedWorkHours { get; set; }

        /// <summary>
        /// Final OT hours after adjustment
        /// </summary>
        public decimal AdjustedOTHours { get; set; }

        /// <summary>
        /// Final on-time status after adjustment
        /// </summary>
        public bool AdjustedOnTime { get; set; }

        /// <summary>
        /// Final variance minutes after adjustment
        /// </summary>
        public int AdjustedVarianceMinutes { get; set; }

        /// <summary>
        /// Final late minutes after adjustment
        /// </summary>
        public int AdjustedLateMinutes { get; set; }

        /// <summary>
        /// Final sign-out time after adjustment (if applicable)
        /// </summary>
        public DateTime? AdjustedSignOut { get; set; }

        /// <summary>
        /// Final sign-in time after adjustment (for timing delta preservation)
        /// </summary>
        public DateTime? AdjustedSignIn { get; set; }

        // === DELTA CALCULATIONS (computed properties) ===
        /// <summary>
        /// How much work hours changed due to adjustment
        /// </summary>
        public decimal WorkHoursDelta => AdjustedWorkHours - OriginalWorkHours;

        /// <summary>
        /// How much OT hours changed due to adjustment
        /// </summary>
        public decimal OTHoursDelta => AdjustedOTHours - OriginalOTHours;

        /// <summary>
        /// How much late minutes changed due to adjustment
        /// </summary>
        public int LateMinutesDelta => AdjustedLateMinutes - OriginalLateMinutes;

        /// <summary>
        /// How much variance changed due to adjustment (in minutes)
        /// </summary>
        public int VarianceDelta => AdjustedVarianceMinutes - OriginalVarianceMinutes;

                
        /// <summary>
        /// Adjusted sign-in time for OT shift (if applicable)
        /// </summary>
        public DateTime? AdjustedOTSignIn { get; set; }

        /// <summary>
        /// Adjusted sign-out time for OT shift (if applicable)
        /// </summary>
        public DateTime? AdjustedOTSignOut { get; set; }

        public decimal? AdjustedDailyPay { get; set; }

        public decimal? AdjustedOTPay { get; set; }

        public string ChangeSummary
        {
            get
            {
                var changes = new List<string>();

                if (OriginalOnTime != AdjustedOnTime)
                    changes.Add($"On-time: {OriginalOnTime} → {AdjustedOnTime}");

                if (OriginalLateMinutes != AdjustedLateMinutes)
                    changes.Add($"Late minutes: {OriginalLateMinutes} → {AdjustedLateMinutes}");

                if (WorkHoursDelta != 0)
                    changes.Add($"Work hours: {OriginalWorkHours:F1} → {AdjustedWorkHours:F1}");

                if (OTHoursDelta != 0)
                    changes.Add($"OT hours: {OriginalOTHours:F1} → {AdjustedOTHours:F1}");

                if (VarianceDelta != 0)
                    changes.Add($"Variance: {OriginalVarianceMinutes} → {AdjustedVarianceMinutes} min");

                return changes.Any() ? string.Join(", ", changes) : "No changes";
            }
        }
    }
}