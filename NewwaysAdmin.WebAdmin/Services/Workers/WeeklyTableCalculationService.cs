// File: NewwaysAdmin.WebAdmin/Services/Workers/WeeklyTableCalculationService.cs
// Purpose: All calculations for weekly table display
// FIXED: CalculateDailyPay() now properly checks for adjusted daily pay overrides

using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public interface IWeeklyTableCalculationService
    {
        decimal CalculateTotalHours(WeeklyTableRow row);
        int CalculateVarianceMinutes(WeeklyTableRow row);
        decimal CalculateEfficiencyPercent(WeeklyTableRow row);
        decimal CalculateDailyPay(WeeklyTableRow row);
        decimal CalculateOTPay(WeeklyTableRow row);
        decimal CalculateTotalPay(WeeklyTableRow row);
    }

    public class WeeklyTableCalculationService : IWeeklyTableCalculationService
    {
        private readonly ILogger<WeeklyTableCalculationService> _logger;

        public WeeklyTableCalculationService(ILogger<WeeklyTableCalculationService> logger)
        {
            _logger = logger;
        }

        public decimal CalculateTotalHours(WeeklyTableRow row)
        {
            // Only calculate total hours if there's actual sign-in data
            if (!row.HasData || row.WorkerData?.SignIn == null) return 0;

            return row.WorkerData.NormalWorkHours + row.WorkerData.OTWorkHours;
        }

        public int CalculateVarianceMinutes(WeeklyTableRow row)
        {
            // Only calculate variance if there's actual sign-in data
            if (!row.HasData || row.Settings == null || row.WorkerData?.SignIn == null) return 0;

            var actualHours = row.WorkerData.NormalWorkHours;
            var expectedHours = row.Settings.ExpectedHoursPerDay;
            var varianceHours = actualHours - expectedHours;

            return (int)(varianceHours * 60);
        }

        public decimal CalculateEfficiencyPercent(WeeklyTableRow row)
        {
            // Only calculate efficiency if there's actual sign-in data
            if (!row.HasData || row.Settings == null || row.WorkerData?.SignIn == null || row.Settings.ExpectedHoursPerDay == 0) return 0;

            var actualHours = row.WorkerData.NormalWorkHours;
            var expectedHours = row.Settings.ExpectedHoursPerDay;

            return (actualHours / expectedHours) * 100;
        }

        public decimal CalculateDailyPay(WeeklyTableRow row)
        {
            // Only calculate daily pay if there's actual sign-in data
            if (!row.HasData || row.Settings == null || row.WorkerData?.SignIn == null) return 0;

            // FIXED: Check if WorkerData already has the calculated daily pay (includes overrides)
            // WorkerDataService.GetCompleteDataAsync() already handles pay override logic
            if (row.WorkerData.DailyPay > 0)
            {
                return row.WorkerData.DailyPay;
            }

            // Fallback: Calculate from settings (should rarely happen as WorkerDataService handles this)
            return row.Settings.DailyPayRate;
        }

        public decimal CalculateOTPay(WeeklyTableRow row)
        {
            // Only calculate OT pay if there's actual sign-in data
            if (!row.HasData || row.Settings == null || row.WorkerData?.SignIn == null) return 0;

            return row.WorkerData.OTWorkHours * row.Settings.OvertimeHourlyRate;
        }

        public decimal CalculateTotalPay(WeeklyTableRow row)
        {
            // Total pay is daily pay + OT pay - but return 0 if no sign-in
            if (!row.HasData || row.WorkerData?.SignIn == null) return 0;

            return CalculateDailyPay(row) + CalculateOTPay(row);
        }
    }
}