// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerWeeklyService.cs
// Purpose: Generate, calculate, and manage weekly worker data summaries

using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Workers;
using NewwaysAdmin.WorkerAttendance.Models;
using System.Globalization;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerWeeklyService
    {
        private readonly IDataStorage<DailyWorkCycle> _cycleStorage;
        private readonly IDataStorage<WorkerWeeklyData> _weeklyStorage;
        private readonly WorkerSettingsService _settingsService;
        private readonly WorkerPaymentCalculator _calculator;
        private readonly ILogger<WorkerWeeklyService> _logger;

        public WorkerWeeklyService(
            StorageManager storageManager,
            WorkerSettingsService settingsService,
            WorkerPaymentCalculator calculator,
            ILogger<WorkerWeeklyService> logger)
        {
            _cycleStorage = storageManager.GetStorageSync<DailyWorkCycle>("WorkerAttendance");
            _weeklyStorage = storageManager.GetStorageSync<WorkerWeeklyData>("WorkerWeeklyData");
            _settingsService = settingsService;
            _calculator = calculator;
            _logger = logger;
        }

        /// <summary>
        /// Generate weekly data for a worker for a specific week
        /// </summary>
        public async Task<WorkerWeeklyData> GenerateWeeklyDataAsync(
            int workerId,
            string workerName,
            DateTime weekStartDate)
        {
            // Ensure weekStartDate is a Sunday
            weekStartDate = GetWeekStartDate(weekStartDate);

            var settings = await _settingsService.GetSettingsAsync(workerId, workerName);
            var dailyRecords = new List<DailyWorkRecord>();

            // Generate records for all 7 days (Sunday to Saturday)
            for (int i = 0; i < 7; i++)
            {
                var date = weekStartDate.AddDays(i);
                var cycle = await LoadCycleForDateAsync(workerId, date);
                var dailyRecord = _calculator.CreateDailyRecord(date, cycle, settings);
                dailyRecords.Add(dailyRecord);
            }

            // Calculate weekly totals
            var (totalWork, totalOT, totalPay, daysWorked, onTimePercentage) =
                _calculator.CalculateWeeklyTotals(dailyRecords);

            var weekNumber = GetWeekNumber(weekStartDate);

            var weeklyData = new WorkerWeeklyData
            {
                WorkerId = workerId,
                WorkerName = workerName,
                WeekStartDate = weekStartDate,
                WeekNumber = weekNumber,
                Year = weekStartDate.Year,
                DailyRecords = dailyRecords,
                TotalWorkHours = totalWork,
                TotalOTHours = totalOT,
                TotalPay = totalPay,
                DaysWorked = daysWorked,
                OnTimePercentage = onTimePercentage,
                GeneratedAt = DateTime.Now,
                SettingsSnapshot = settings
            };

            return weeklyData;
        }

        /// <summary>
        /// Save weekly data to storage
        /// </summary>
        public async Task SaveWeeklyDataAsync(WorkerWeeklyData weeklyData)
        {
            try
            {
                // Build identifier: Year/WeekXX/worker-{id}
                var identifier = $"{weeklyData.Year}/Week{weeklyData.WeekNumber:D2}/worker-{weeklyData.WorkerId}";

                await _weeklyStorage.SaveAsync(identifier, weeklyData);

                _logger.LogInformation(
                    "Saved weekly data for worker {WorkerId} - Week {WeekNumber}/{Year}",
                    weeklyData.WorkerId,
                    weeklyData.WeekNumber,
                    weeklyData.Year);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save weekly data for worker {WorkerId}", weeklyData.WorkerId);
                throw;
            }
        }
        /// <summary>
        /// Ensure all completed weeks are archived for a worker
        /// Checks last 8 weeks for any missing archives
        /// </summary>
        public async Task EnsureCompletedWeeksArchivedAsync(int workerId, string workerName)
        {
            try
            {
                // Check last 8 weeks (2 months of data)
                var weeksToCheck = 8;
                var lastSaturday = GetWeekStartDate(DateTime.Today).AddDays(-1);
                var startDate = lastSaturday.AddDays(-7 * weeksToCheck);

                var checkDate = GetWeekStartDate(startDate);

                _logger.LogInformation(
                    "Checking for incomplete archives for worker {WorkerId} (last {Weeks} weeks)",
                    workerId, weeksToCheck);

                int archived = 0;
                while (checkDate < lastSaturday)
                {
                    var weekEndDate = checkDate.AddDays(6);

                    // Only archive if the week is complete
                    if (DateTime.Today > weekEndDate)
                    {
                        var existingData = await LoadWeeklyDataAsync(workerId, checkDate);

                        if (existingData == null)
                        {
                            var weeklyData = await GenerateWeeklyDataAsync(workerId, workerName, checkDate);
                            await SaveWeeklyDataAsync(weeklyData);
                            archived++;
                        }
                    }

                    checkDate = checkDate.AddDays(7);
                }

                if (archived > 0)
                {
                    _logger.LogInformation("Archived {Count} missing weeks for worker {WorkerId}", archived, workerId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure completed weeks archived for worker {WorkerId}", workerId);
            }
        }
        /// <summary>
        /// Load weekly data from storage (if exists)
        /// </summary>
        public async Task<WorkerWeeklyData?> LoadWeeklyDataAsync(
            int workerId,
            DateTime weekStartDate)
        {
            try
            {
                weekStartDate = GetWeekStartDate(weekStartDate);
                var weekNumber = GetWeekNumber(weekStartDate);
                var year = weekStartDate.Year;

                var identifier = $"{year}/Week{weekNumber:D2}/worker-{workerId}";
                var weeklyData = await _weeklyStorage.LoadAsync(identifier);

                return weeklyData;
            }
            catch
            {
                // Weekly data doesn't exist yet
                return null;
            }
        }

        /// <summary>
        /// Get or generate weekly data (loads from storage or generates fresh)
        /// </summary>
        /// <summary>
        /// Get or generate weekly data (loads from storage or generates fresh)
        /// Always regenerates if week is still in progress
        /// </summary>
        public async Task<WorkerWeeklyData> GetOrGenerateWeeklyDataAsync(
            int workerId,
            string workerName,
            DateTime weekStartDate)
        {
            weekStartDate = GetWeekStartDate(weekStartDate);
            var weekEndDate = weekStartDate.AddDays(6); // Saturday
            var isWeekComplete = DateTime.Today > weekEndDate;

            // If week is complete, load from storage (archived, don't regenerate)
            if (isWeekComplete)
            {
                var existingData = await LoadWeeklyDataAsync(workerId, weekStartDate);
                if (existingData != null && existingData.DailyRecords.Count == 7)
                {
                    _logger.LogInformation(
                        "Loaded archived weekly data for worker {WorkerId} - Week {WeekNumber}/{Year}",
                        workerId,
                        existingData.WeekNumber,
                        existingData.Year);
                    return existingData;
                }
            }

            // Week is in progress OR archived data doesn't exist - regenerate
            _logger.LogInformation(
                "Generating weekly data for worker {WorkerId} - Week starting {Date} (Week {Status})",
                workerId,
                weekStartDate.ToString("yyyy-MM-dd"),
                isWeekComplete ? "complete, archiving" : "in progress");

            var weeklyData = await GenerateWeeklyDataAsync(workerId, workerName, weekStartDate);

            // Save (creates archive for complete weeks, updates for in-progress weeks)
            await SaveWeeklyDataAsync(weeklyData);

            return weeklyData;
        }

        /// <summary>
        /// Load a daily work cycle for a specific date and worker
        /// </summary>
        private async Task<DailyWorkCycle?> LoadCycleForDateAsync(int workerId, DateTime date)
        {
            try
            {
                // Try with double extension first (current actual files)
                var identifier = $"{date:yyyy-MM-dd}_Worker{workerId}.json";
                var cycle = await _cycleStorage.LoadAsync(identifier);
                if (cycle != null && cycle.Records.Any())
                    return cycle;
            }
            catch { }

            try
            {
                // Fallback to correct naming (in case files get fixed)
                var identifier = $"{date:yyyy-MM-dd}_Worker{workerId}";
                var cycle = await _cycleStorage.LoadAsync(identifier);
                return cycle;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get the Sunday that starts the week containing the given date
        /// </summary>
        private DateTime GetWeekStartDate(DateTime date)
        {
            var daysSinceSunday = (int)date.DayOfWeek;
            return date.Date.AddDays(-daysSinceSunday);
        }

        /// <summary>
        /// Get ISO week number for a date
        /// </summary>
        private int GetWeekNumber(DateTime date)
        {
            var culture = CultureInfo.CurrentCulture;
            var calendar = culture.Calendar;
            var calendarWeekRule = culture.DateTimeFormat.CalendarWeekRule;
            var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
            return calendar.GetWeekOfYear(date, calendarWeekRule, firstDayOfWeek);
        }

        /// <summary>
        /// Delete weekly data for a specific week
        /// </summary>
        public async Task DeleteWeeklyDataAsync(int workerId, DateTime weekStartDate)
        {
            try
            {
                weekStartDate = GetWeekStartDate(weekStartDate);
                var weekNumber = GetWeekNumber(weekStartDate);
                var year = weekStartDate.Year;

                var identifier = $"{year}/Week{weekNumber:D2}/worker-{workerId}";
                await _weeklyStorage.DeleteAsync(identifier);

                _logger.LogInformation(
                    "Deleted weekly data for worker {WorkerId} - Week {WeekNumber}/{Year}",
                    workerId,
                    weekNumber,
                    year);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete weekly data for worker {WorkerId}", workerId);
                throw;
            }
        }

        /// <summary>
        /// Regenerate weekly data (useful if settings changed or data needs refresh)
        /// </summary>
        public async Task<WorkerWeeklyData> RegenerateWeeklyDataAsync(
            int workerId,
            string workerName,
            DateTime weekStartDate)
        {
            _logger.LogInformation(
                "Regenerating weekly data for worker {WorkerId} - Week starting {Date}",
                workerId,
                weekStartDate.ToString("yyyy-MM-dd"));

            // Generate fresh data
            var weeklyData = await GenerateWeeklyDataAsync(workerId, workerName, weekStartDate);

            // Save (overwrite existing)
            await SaveWeeklyDataAsync(weeklyData);

            return weeklyData;
        }
    }
}