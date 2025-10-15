// File: NewwaysAdmin.WebAdmin/Models/Workers/TableColumn.cs
// Purpose: Column definition that delegates calculations to service

using NewwaysAdmin.WebAdmin.Services.Workers;

namespace NewwaysAdmin.WebAdmin.Models.Workers
{
    public enum ColumnDataSource
    {
        DirectService,    // From WorkerDataService
        Calculated,       // Computed from service data + settings  
        Display          // Pure display/formatting
    }

    /// <summary>
    /// Column definition with display logic that uses calculation service
    /// </summary>
    public class TableColumn
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool DefaultVisible { get; set; }
        public int SortOrder { get; set; }
        public ColumnDataSource DataSource { get; set; }
        public bool IsAdjustable { get; set; }
        public string? Icon { get; set; }
        public string? CssClass { get; set; }
        public string? Description { get; set; }

        /// <summary>
        /// Get formatted display value using calculation service
        /// </summary>
        public string GetDisplayValue(WeeklyTableRow row, IWeeklyTableCalculationService calculationService)
        {
            return Key switch
            {
                // Direct data - no calculations needed
                "date" => row.Date.ToString("MMM dd"),
                "dayName" => row.DayName,
                "signIn" => row.WorkerData?.SignIn?.ToString("HH:mm") ?? "--:--",
                "signOut" => row.WorkerData?.SignOut?.ToString("HH:mm") ?? "--:--",
                "otSignIn" => row.WorkerData?.OTSignIn?.ToString("HH:mm") ?? "--:--",
                "otSignOut" => row.WorkerData?.OTSignOut?.ToString("HH:mm") ?? "--:--",
                "normalHours" => (row.HasData && row.WorkerData?.SignIn != null) ? $"{row.WorkerData.NormalWorkHours:F1}h" : "--",
                "otHours" => (row.HasData && row.WorkerData?.SignIn != null) ? $"{row.WorkerData.OTWorkHours:F1}h" : "--",
                "onTimeStatus" => row.HasData ? FormatOnTimeStatus(row) : "--",
                "adjustmentStatus" => row.WorkerData?.HasAdjustments == true ? "Adjusted" : "Original",

                // Calculated data - use calculation service
                "totalHours" => (row.HasData && row.WorkerData?.SignIn != null) ? $"{calculationService.CalculateTotalHours(row):F1}h" : "--",
                "variance" => (row.HasData && row.WorkerData?.SignIn != null) ? FormatVariance(calculationService.CalculateVarianceMinutes(row)) : "--",
                "efficiency" => (row.HasData && row.WorkerData?.SignIn != null) ? $"{calculationService.CalculateEfficiencyPercent(row):F1}%" : "--",
                "dailyPay" => (row.HasData && row.WorkerData?.SignIn != null) ? $"฿{calculationService.CalculateDailyPay(row):F0}" : "--",
                "otPay" => (row.HasData && row.WorkerData?.SignIn != null) ? $"฿{calculationService.CalculateOTPay(row):F0}" : "--",
                "totalPay" => (row.HasData && row.WorkerData?.SignIn != null) ? $"฿{calculationService.CalculateTotalPay(row):F0}" : "--",


                // Easy to add new calculated columns:
                // "newMetric" => $"{calculationService.CalculateNewMetric(row):F2}",

                _ => ""
            };
        }

        /// <summary>
        /// Get CSS classes for this column's cell
        /// </summary>
        public string GetCellCssClass(WeeklyTableRow row)
        {
            return Key switch
            {
                "onTimeStatus" => row.HasData ? GetOnTimeStatusCss(row) : "",
                "normalHours" => row.HasData ? GetNormalHoursCss(row) : "", // NEW: Light blue for overtime
                _ => CssClass ?? ""
            };
        }

        // Private formatting helpers
        private static string FormatVariance(int varianceMinutes)
        {
            if (varianceMinutes == 0) return "0 min";
            return $"{(varianceMinutes > 0 ? "+" : "")}{varianceMinutes} min";
        }

        private static string FormatOnTimeStatus(WeeklyTableRow row)
        {
            // Only show status if there's actually a sign-in time
            if (!row.HasData || row.WorkerData?.SignIn == null || row.Settings == null) return "--";

            // FIXED: Calculate on-time status dynamically based on current displayed sign-in time
            var isOnTime = CalculateIsOnTime(row.WorkerData.SignIn.Value, row.Settings.ExpectedArrivalTime);
            return isOnTime ? "On Time" : "Late";
        }

        private static string GetOnTimeStatusCss(WeeklyTableRow row)
        {
            // Only apply badge styling if there's actually a sign-in time
            if (!row.HasData || row.WorkerData?.SignIn == null || row.Settings == null) return "";

            // FIXED: Calculate on-time status dynamically based on current displayed sign-in time
            var isOnTime = CalculateIsOnTime(row.WorkerData.SignIn.Value, row.Settings.ExpectedArrivalTime);
            var badgeClass = isOnTime ? "badge bg-success" : "badge bg-danger";
            return $"{badgeClass} d-flex justify-content-center";
        }

        // NEW: Helper method to calculate on-time status with 10-minute tolerance
        private static bool CalculateIsOnTime(DateTime actualArrival, TimeSpan expectedArrival)
        {
            var actualTime = actualArrival.TimeOfDay;

            // Allow 10 minute grace period (matching WorkerPaymentCalculator)
            var gracePeriod = TimeSpan.FromMinutes(10);
            var latestAcceptable = expectedArrival.Add(gracePeriod);

            return actualTime <= latestAcceptable;
        }

        /// <summary>
        /// Get CSS class for Normal Hours cell - light blue background if worked over expected hours
        /// </summary>
        /// <summary>
        /// Get CSS class for Normal Hours cell - light blue background if worked significantly over expected hours
        /// UPDATED: Added 20-minute grace period for hanging around after work
        /// </summary>
        private static string GetNormalHoursCss(WeeklyTableRow row)
        {
            // Only apply styling if there's actual work data and settings
            if (!row.HasData || row.WorkerData?.SignIn == null || row.Settings == null)
                return "";

            // Check if normal hours worked exceeds expected hours with 20-minute grace period
            var actualHours = row.WorkerData.NormalWorkHours;
            var expectedHours = row.Settings.ExpectedHoursPerDay;

            // Convert 20 minutes to decimal hours (20/60 = 0.333...)
            var gracePeriodHours = 20m / 60m; // 0.33 hours
            var overtimeThreshold = expectedHours + gracePeriodHours;

            if (actualHours > overtimeThreshold)
            {
                // Light blue background only if worked more than expected + 20 min grace period
                return "bg-info bg-opacity-25";
            }

            return "";
        }
    }
}