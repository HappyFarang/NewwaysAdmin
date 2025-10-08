// File: NewwaysAdmin.WebAdmin/Models/Workers/DailyWorkRecord.cs
// Purpose: Single day work record with hours, variance, payment, and adjustment tracking

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class DailyWorkRecord
    {
        /// <summary>
        /// The date of this work record - CRITICAL for date-range queries
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Day of week for display
        /// </summary>
        public DayOfWeek DayOfWeek => Date.DayOfWeek;

        /// <summary>
        /// Total regular work hours for the day (FINAL adjusted values)
        /// </summary>
        public decimal WorkHours { get; set; }

        /// <summary>
        /// Total overtime hours for the day (FINAL adjusted values)
        /// </summary>
        public decimal OTHours { get; set; }

        /// <summary>
        /// Variance in minutes from expected hours (FINAL adjusted values)
        /// Positive = worked extra, Negative = left early
        /// </summary>
        public int VarianceMinutes { get; set; }

        /// <summary>
        /// Whether worker arrived on time (FINAL adjusted values)
        /// </summary>
        public bool OnTime { get; set; }

        /// <summary>
        /// How many minutes late (positive) or early (negative) they arrived
        /// </summary>
        public int LateMinutes { get; set; }

        /// <summary>
        /// Total pay for this day (base + OT) - calculated from final adjusted values
        /// </summary>
        public decimal DailyPay { get; set; }

        /// <summary>
        /// Whether this day has actual data or is empty/future
        /// </summary>
        public bool HasData { get; set; }

        /// <summary>
        /// Normal shift sign-in time
        /// </summary>
        public DateTime? NormalSignIn { get; set; }

        /// <summary>
        /// Normal shift sign-out time (may be adjusted)
        /// </summary>
        public DateTime? NormalSignOut { get; set; }

        /// <summary>
        /// Overtime shift sign-in time
        /// </summary>
        public DateTime? OTSignIn { get; set; }

        /// <summary>
        /// Overtime shift sign-out time (may be adjusted)
        /// </summary>
        public DateTime? OTSignOut { get; set; }

        // === NEW: ADJUSTMENT TRACKING ===
        /// <summary>
        /// Whether this day has been manually adjusted
        /// </summary>
        public bool HasAdjustments { get; set; }

        /// <summary>
        /// Details of the adjustment made (null if no adjustments)
        /// </summary>
        public DailyAdjustment? AppliedAdjustment { get; set; }

        // === EXISTING: FORMATTED HELPERS ===
        /// <summary>
        /// Formatted timestamp helpers (24-hour format)
        /// </summary>
        public string NormalSignInFormatted => NormalSignIn?.ToString("HH:mm") ?? "--:--";
        public string NormalSignOutFormatted => NormalSignOut?.ToString("HH:mm") ?? "--:--";
        public string OTSignInFormatted => OTSignIn?.ToString("HH:mm") ?? "--:--";
        public string OTSignOutFormatted => OTSignOut?.ToString("HH:mm") ?? "--:--";

        /// <summary>
        /// Formatted variance string for display (e.g., "+15 min", "-7 min")
        /// </summary>
        public string VarianceDisplay
        {
            get
            {
                if (!HasData) return "--";
                if (VarianceMinutes == 0) return "0 min";
                var sign = VarianceMinutes > 0 ? "+" : "";
                return $"{sign}{VarianceMinutes} min";
            }
        }

        /// <summary>
        /// Formatted on-time status for display
        /// </summary>
        public string OnTimeDisplay => HasData ? (OnTime ? "Yes" : $"Late {LateMinutes} min") : "--";

        // === NEW: ADJUSTMENT DISPLAY HELPERS ===
        /// <summary>
        /// Display indicator for adjusted days
        /// </summary>
        public string AdjustmentIndicator => HasAdjustments ? "🔧" : "";

        /// <summary>
        /// CSS class for styling adjusted rows
        /// </summary>
        public string AdjustmentCssClass => HasAdjustments ? "adjusted-day" : "";

        /// <summary>
        /// Tooltip text showing adjustment details
        /// </summary>
        public string AdjustmentTooltip
        {
            get
            {
                if (!HasAdjustments || AppliedAdjustment == null)
                    return "";

                return $"Adjusted: {AppliedAdjustment.Description}\n" +
                       $"Applied: {AppliedAdjustment.AppliedAt:yyyy-MM-dd HH:mm}\n" +
                       $"By: {AppliedAdjustment.AppliedBy}";
            }
        }
    }
}