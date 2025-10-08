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
        /// Display sign-in time - shows adjusted time if adjustment exists, otherwise original time
        /// </summary>
        public string DisplaySignInFormatted
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment?.AdjustedSignIn.HasValue == true)
                {
                    return AppliedAdjustment.AdjustedSignIn.Value.ToString("HH:mm");
                }
                return NormalSignIn?.ToString("HH:mm") ?? "--:--";
            }
        }

        /// <summary>
        /// Display sign-out time - shows adjusted time if adjustment exists, otherwise original time
        /// </summary>
        public string DisplaySignOutFormatted
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment?.AdjustedSignOut.HasValue == true)
                {
                    return AppliedAdjustment.AdjustedSignOut.Value.ToString("HH:mm");
                }
                return NormalSignOut?.ToString("HH:mm") ?? "--:--";
            }
        }

        /// <summary>
        /// Display sign-in time (actual DateTime) - returns adjusted time if available, otherwise original
        /// </summary>
        public DateTime? DisplaySignIn
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment?.AdjustedSignIn.HasValue == true)
                {
                    return AppliedAdjustment.AdjustedSignIn.Value;
                }
                return NormalSignIn;
            }
        }

        /// <summary>
        /// Display sign-out time (actual DateTime) - returns adjusted time if available, otherwise original
        /// </summary>
        public DateTime? DisplaySignOut
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment?.AdjustedSignOut.HasValue == true)
                {
                    return AppliedAdjustment.AdjustedSignOut.Value;
                }
                return NormalSignOut;
            }
        }
        /// <summary>
        /// Display work hours - shows adjusted hours if adjustment exists, otherwise original hours
        /// </summary>
        public decimal DisplayWorkHours
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment != null)
                {
                    return AppliedAdjustment.AdjustedWorkHours;
                }
                return WorkHours;
            }
        }

        /// <summary>
        /// Display OT hours - shows adjusted OT hours if adjustment exists, otherwise original OT hours
        /// </summary>
        public decimal DisplayOTHours
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment != null)
                {
                    return AppliedAdjustment.AdjustedOTHours;
                }
                return OTHours;
            }
        }

        /// <summary>
        /// Display variance minutes - shows adjusted variance if adjustment exists, otherwise original variance
        /// </summary>
        public int DisplayVarianceMinutes
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment != null)
                {
                    return AppliedAdjustment.AdjustedVarianceMinutes;
                }
                return VarianceMinutes;
            }
        }

        /// <summary>
        /// Display variance string - shows adjusted variance formatted if adjustment exists, otherwise original variance
        /// </summary>
        public string DisplayVarianceFormatted
        {
            get
            {
                if (!HasData) return "--";

                var varianceToShow = DisplayVarianceMinutes;
                if (varianceToShow == 0) return "0 min";

                var sign = varianceToShow > 0 ? "+" : "";
                return $"{sign}{varianceToShow} min";
            }
        }

        /// <summary>
        /// Display on-time status - shows adjusted on-time status if adjustment exists, otherwise original status
        /// </summary>
        public bool DisplayOnTime
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment != null)
                {
                    return AppliedAdjustment.AdjustedOnTime;
                }
                return OnTime;
            }
        }

        /// <summary>
        /// Display late minutes - shows adjusted late minutes if adjustment exists, otherwise original late minutes
        /// </summary>
        public int DisplayLateMinutes
        {
            get
            {
                if (HasAdjustments && AppliedAdjustment != null)
                {
                    return AppliedAdjustment.AdjustedLateMinutes;
                }
                return LateMinutes;
            }
        }

        /// <summary>
        /// Display formatted on-time status for display
        /// </summary>
        public string DisplayOnTimeFormatted => HasData ? (DisplayOnTime ? "Yes" : $"Late {DisplayLateMinutes} min") : "--";

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