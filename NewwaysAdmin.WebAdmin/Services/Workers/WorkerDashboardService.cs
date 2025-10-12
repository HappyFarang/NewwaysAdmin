// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDashboardService.cs
// Purpose: Load and process worker attendance data for dashboard display
// UPDATED: Fixed GetTodaysAdjustmentsAsync to work with current working cycles instead of just "today's date"

using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WorkerAttendance.Models;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerDashboardService
    {
        private readonly IDataStorage<DailyWorkCycle> _cycleStorage;
        private readonly IDataStorage<Worker> _workerStorage;
        private readonly ILogger<WorkerDashboardService> _logger;
        private readonly WorkerWeeklyService _weeklyService;

        public WorkerDashboardService(
            StorageManager storageManager,
            WorkerWeeklyService weeklyService,
            ILogger<WorkerDashboardService> logger)
        {
            // WorkerAttendance folder = daily cycle files (2025-10-02_Worker3.json)
            _cycleStorage = storageManager.GetStorageSync<DailyWorkCycle>("WorkerAttendance");

            // Workers folder = worker profile files (3.json with face encodings)
            _workerStorage = storageManager.GetStorageSync<Worker>("WorkerAttendance");
            _weeklyService = weeklyService;

            _logger = logger;
        }

        /// <summary>
        /// Get adjustments for current working cycles (CYCLE-BASED, NOT DATE-BASED)
        /// CRITICAL FIX: Look for adjustments based on the worker's current cycle date, not today's date
        /// </summary>
        public async Task<Dictionary<int, DailyWorkRecord>> GetTodaysAdjustmentsAsync()
        {
            var adjustments = new Dictionary<int, DailyWorkRecord>();

            try
            {
                // STEP 1: First get the current dashboard data to understand each worker's current cycle
                var dashboardData = await GetTodaysDashboardDataAsync();
                var allWorkers = dashboardData.ActiveWorkers.Concat(dashboardData.InactiveWorkers);

                _logger.LogInformation("Checking adjustments for {WorkerCount} workers based on their current working cycles", allWorkers.Count());

                foreach (var worker in allWorkers)
                {
                    try
                    {
                        // CRITICAL FIX: Use the worker's cycle date, not today's date
                        var workerCycleDate = worker.CycleDate.Date;

                        _logger.LogDebug("Checking adjustments for worker {WorkerId} on cycle date {CycleDate}",
                            worker.WorkerId, workerCycleDate.ToString("yyyy-MM-dd"));

                        // Find the week that contains this worker's cycle date
                        var weekStartDate = GetWeekStartDate(workerCycleDate);
                        var weeklyData = await _weeklyService.LoadWeeklyDataAsync(worker.WorkerId, weekStartDate);

                        if (weeklyData != null)
                        {
                            // Find the record for this worker's cycle date (not today's date)
                            var cycleRecord = weeklyData.DailyRecords.FirstOrDefault(d => d.Date.Date == workerCycleDate);

                            // If the cycle record has adjustments, add it to our dictionary
                            if (cycleRecord?.HasAdjustments == true)
                            {
                                adjustments[worker.WorkerId] = cycleRecord;
                                _logger.LogInformation("Found adjustment for worker {WorkerId} on cycle date {CycleDate}: {Description}",
                                    worker.WorkerId,
                                    workerCycleDate.ToString("yyyy-MM-dd"),
                                    cycleRecord.AppliedAdjustment?.Description ?? "Unknown");
                            }
                            else if (cycleRecord?.HasData == true)
                            {
                                _logger.LogDebug("Worker {WorkerId} has data for cycle date {CycleDate} but no adjustments",
                                    worker.WorkerId, workerCycleDate.ToString("yyyy-MM-dd"));
                            }
                            else
                            {
                                _logger.LogDebug("No data found for worker {WorkerId} on cycle date {CycleDate}",
                                    worker.WorkerId, workerCycleDate.ToString("yyyy-MM-dd"));
                            }
                        }
                        else
                        {
                            _logger.LogDebug("No weekly data found for worker {WorkerId}, week starting {WeekStart}",
                                worker.WorkerId, weekStartDate.ToString("yyyy-MM-dd"));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail the whole operation for one worker
                        _logger.LogWarning(ex, "Failed to check adjustments for worker {WorkerId} on cycle date {CycleDate}",
                            worker.WorkerId, worker.CycleDate.ToString("yyyy-MM-dd"));
                    }
                }

                _logger.LogInformation("Found {AdjustmentCount} workers with current cycle adjustments", adjustments.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get current cycle adjustments");
            }

            return adjustments;
        }

        private DateTime GetWeekStartDate(DateTime date)
        {
            var daysSinceSunday = (int)date.DayOfWeek;
            return date.AddDays(-daysSinceSunday);
        }

        /// <summary>
        /// Get ALL registered workers, grouped by active status
        /// Shows latest activity data regardless of date
        /// </summary>
        /// <summary>
        /// Get ALL registered workers, grouped by active status
        /// FIXED: Properly filter out invalid/empty workers to prevent ghost entries
        /// </summary>
        public async Task<DashboardData> GetTodaysDashboardDataAsync()
        {
            var today = DateTime.Today;

            // Get all registered workers (with better filtering)
            var allWorkers = await GetAllRegisteredWorkersAsync();

            _logger.LogInformation("Found {WorkerCount} valid registered workers", allWorkers.Count);

            // Load cycles from last 2 days to catch night shifts that started yesterday
            var todaysCyclesWithFiles = await LoadCyclesForDateWithFileNamesAsync(today);
            var yesterdaysCyclesWithFiles = await LoadCyclesForDateWithFileNamesAsync(today.AddDays(-1));
            var allRecentCyclesWithFiles = todaysCyclesWithFiles.Concat(yesterdaysCyclesWithFiles).ToList();

            var activeWorkers = new List<WorkerStatus>();
            var inactiveWorkers = new List<WorkerStatus>();

            // Process each registered worker
            foreach (var worker in allWorkers)
            {
                // CRITICAL FIX: Skip any worker with invalid data
                if (worker.Id <= 0 || string.IsNullOrWhiteSpace(worker.Name))
                {
                    _logger.LogWarning("Skipping invalid worker: ID={WorkerId}, Name='{WorkerName}'", worker.Id, worker.Name ?? "NULL");
                    continue;
                }

                // Find the most recent cycle for this worker (could be from today or yesterday)
                var recentCycles = allRecentCyclesWithFiles
                    .Where(c => c.Cycle.WorkerId == worker.Id)
                    .OrderByDescending(c => c.Cycle.LastRecord?.Timestamp ?? DateTime.MinValue)
                    .ToList();

                if (recentCycles.Any())
                {
                    var mostRecentCycleWithFile = recentCycles.First();
                    var workerStatus = CreateWorkerStatus(worker, mostRecentCycleWithFile.Cycle, mostRecentCycleWithFile.FileName);

                    // Determine if worker is currently active (checked in)
                    if (mostRecentCycleWithFile.Cycle.IsCurrentlyCheckedIn)
                    {
                        activeWorkers.Add(workerStatus);
                    }
                    else
                    {
                        inactiveWorkers.Add(workerStatus);
                    }
                }
                else
                {
                    // Worker has no recent activity - create inactive status with no cycle data
                    var workerStatus = new WorkerStatus
                    {
                        WorkerId = worker.Id,
                        WorkerName = worker.Name,
                        IsActive = false,
                        LastActivity = DateTime.MinValue,
                        CycleDate = today, // Default to today if no cycle data
                        ShowDate = false
                    };
                    inactiveWorkers.Add(workerStatus);
                    _logger.LogDebug("Worker {WorkerId} ({WorkerName}) has no recent activity", worker.Id, worker.Name);
                }
            }

            _logger.LogInformation("Dashboard loaded: {ActiveCount} active workers, {InactiveCount} inactive workers",
                activeWorkers.Count, inactiveWorkers.Count);

            return new DashboardData
            {
                ActiveWorkers = activeWorkers.OrderBy(w => w.WorkerName).ToList(),
                InactiveWorkers = inactiveWorkers.OrderBy(w => w.WorkerName).ToList(),
                RefreshTime = DateTime.Now
            };
        }

        private WorkerStatus CreateWorkerStatus(Worker worker, DailyWorkCycle cycle, string fileName)
        {
            var lastRecord = cycle.LastRecord;
            var cycleDate = ExtractCycleDateFromFileName(fileName);

            var status = new WorkerStatus
            {
                WorkerId = worker.Id,
                WorkerName = worker.Name,
                IsActive = cycle.IsCurrentlyCheckedIn,
                LastActivity = lastRecord?.Timestamp ?? DateTime.MinValue,
                CycleDate = cycleDate,
                CurrentCycle = lastRecord?.WorkCycle ?? WorkCycle.Normal,
                ShowDate = cycleDate.Date != DateTime.Today // Show date if not today's cycle
            };

            // Calculate current duration for active workers
            if (cycle.IsCurrentlyCheckedIn && lastRecord != null)
            {
                status.CurrentDuration = DateTime.Now - lastRecord.Timestamp;
            }

            // Set OT flag
            status.HasOT = cycle.Records.Any(r => r.WorkCycle == WorkCycle.OT);

            // Set timing details for completed shifts
            var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
            var otRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.OT).ToList();

            if (normalRecords.Any())
            {
                var firstNormalSignIn = normalRecords.Where(r => r.Type == AttendanceType.CheckIn).FirstOrDefault();
                var lastNormalSignOut = normalRecords.Where(r => r.Type == AttendanceType.CheckOut).LastOrDefault();

                status.NormalSignIn = firstNormalSignIn?.Timestamp;
                status.NormalSignOut = lastNormalSignOut?.Timestamp;

                if (status.NormalSignIn.HasValue && status.NormalSignOut.HasValue)
                {
                    status.NormalHoursWorked = status.NormalSignOut.Value - status.NormalSignIn.Value;
                }
            }

            if (otRecords.Any())
            {
                var firstOTSignIn = otRecords.Where(r => r.Type == AttendanceType.CheckIn).FirstOrDefault();
                var lastOTSignOut = otRecords.Where(r => r.Type == AttendanceType.CheckOut).LastOrDefault();

                status.OTSignIn = firstOTSignIn?.Timestamp;
                status.OTSignOut = lastOTSignOut?.Timestamp;

                if (status.OTSignIn.HasValue && status.OTSignOut.HasValue)
                {
                    status.OTHoursWorked = status.OTSignOut.Value - status.OTSignIn.Value;
                }
            }

            return status;
        }

        private DateTime ExtractCycleDateFromFileName(string fileName)
        {
            // Extract date from filename like "2025-10-02_Worker3.json"
            try
            {
                var datePart = fileName.Split('_')[0];
                return DateTime.ParseExact(datePart, "yyyy-MM-dd", null);
            }
            catch
            {
                // Fallback to today if can't parse
                return DateTime.Today;
            }
        }

        private async Task<List<Worker>> GetAllRegisteredWorkersAsync()
        {
            var workers = new List<Worker>();
            try
            {
                var identifiers = await _workerStorage.ListIdentifiersAsync();

                // FILTER: Only load worker profile files, not attendance files
                // Worker profiles: "3.json", "5.json", etc.
                // Attendance files: "2025-10-11_Worker3.json.json" (skip these)
                var workerProfileFiles = identifiers.Where(id =>
                {
                    // Skip attendance files with .json.json extension
                    if (id.EndsWith(".json.json")) return false;

                    // Skip files with date prefixes (attendance files)
                    if (id.Contains("_Worker")) return false;

                    // Skip adjustment files
                    if (id.StartsWith("adjustment_")) return false;

                    // Only load files that are just numbers with .json extension (worker profiles)
                    var nameWithoutExtension = id.Replace(".json", "");
                    return int.TryParse(nameWithoutExtension, out var workerId) && workerId > 0;
                }).ToList();

                _logger.LogDebug("Found {ProfileCount} worker profile files out of {TotalCount} total files",
                    workerProfileFiles.Count, identifiers.Count());

                foreach (var identifier in workerProfileFiles)
                {
                    try
                    {
                        var worker = await _workerStorage.LoadAsync(identifier);
                        if (worker != null && worker.Id > 0 && !string.IsNullOrWhiteSpace(worker.Name))
                        {
                            workers.Add(worker);
                            _logger.LogDebug("Loaded worker profile: ID={WorkerId}, Name={WorkerName}", worker.Id, worker.Name);
                        }
                        else
                        {
                            _logger.LogWarning("Invalid worker data in file {Identifier}: ID={WorkerId}, Name='{WorkerName}'",
                                identifier, worker?.Id ?? 0, worker?.Name ?? "NULL");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load worker profile file {Identifier}", identifier);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get registered workers");
            }

            _logger.LogInformation("Successfully loaded {WorkerCount} valid worker profiles", workers.Count);
            return workers.OrderBy(w => w.Id).ToList();
        }

        private async Task<List<DailyWorkCycle>> LoadCyclesForDateAsync(DateTime date)
        {
            var cycles = new List<DailyWorkCycle>();
            var datePrefix = date.ToString("yyyy-MM-dd");

            try
            {
                var identifiers = await _cycleStorage.ListIdentifiersAsync();
                var dateFiles = identifiers.Where(id => id.StartsWith(datePrefix)).ToList();

                foreach (var fileName in dateFiles)
                {
                    try
                    {
                        var cycle = await _cycleStorage.LoadAsync(fileName);
                        if (cycle != null)
                        {
                            cycles.Add(cycle);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load cycle file {FileName}", fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load cycles for date {Date}", date.ToString("yyyy-MM-dd"));
            }

            return cycles;
        }

        /// <summary>
        /// Load cycles for a date and return both cycle and filename for date extraction
        /// </summary>
        private async Task<List<(DailyWorkCycle Cycle, string FileName)>> LoadCyclesForDateWithFileNamesAsync(DateTime date)
        {
            var cyclesWithFiles = new List<(DailyWorkCycle Cycle, string FileName)>();
            var datePrefix = date.ToString("yyyy-MM-dd");

            try
            {
                var identifiers = await _cycleStorage.ListIdentifiersAsync();
                var dateFiles = identifiers.Where(id => id.StartsWith(datePrefix)).ToList();

                foreach (var fileName in dateFiles)
                {
                    try
                    {
                        var cycle = await _cycleStorage.LoadAsync(fileName);
                        if (cycle != null)
                        {
                            cyclesWithFiles.Add((cycle, fileName));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load cycle file {FileName}", fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load cycles for date {Date}", date.ToString("yyyy-MM-dd"));
            }

            return cyclesWithFiles;
        }
    }
}