// File: NewwaysAdmin.WebAdmin/Models/Workers/DailyWorkRecord.cs
// Purpose: Single day work record with hours, variance, and payment

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public class DailyWorkRecord
    {
        /// <summary>
        /// The date of this work record
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Day of week for display
        /// </summary>
        public DayOfWeek DayOfWeek => Date.DayOfWeek;

        /// <summary>
        /// Total regular work hours for the day
        /// </summary>
        public decimal WorkHours { get; set; }

        /// <summary>
        /// Total overtime hours for the day
        /// </summary>
        public decimal OTHours { get; set; }

        /// <summary>
        /// Variance in minutes from expected hours (can be negative or positive)
        /// Positive = worked extra, Negative = left early
        /// </summary>
        public int VarianceMinutes { get; set; }

        /// <summary>
        /// Whether worker arrived on time
        /// </summary>
        public bool OnTime { get; set; }

        /// <summary>
        /// How many minutes late (positive) or early (negative) they arrived
        /// </summary>
        public int LateMinutes { get; set; }

        /// <summary>
        /// Total pay for this day (base + OT)
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
        /// Normal shift sign-out time
        /// </summary>
        public DateTime? NormalSignOut { get; set; }

        /// <summary>
        /// Overtime shift sign-in time
        /// </summary>
        public DateTime? OTSignIn { get; set; }

        /// <summary>
        /// Overtime shift sign-out time
        /// </summary>
        public DateTime? OTSignOut { get; set; }

        // NEW: Formatted timestamp helpers (24-hour format as you requested)
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
    }
}