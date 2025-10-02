// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDashboardService.cs
// Purpose: Load and process worker attendance data for dashboard display
// IMPROVED: Shows ALL registered workers with their latest activity (any date)

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

        public WorkerDashboardService(
            StorageManager storageManager,
            ILogger<WorkerDashboardService> logger)
        {
            // WorkerAttendance folder = daily cycle files (2025-10-02_Worker3.json)
            _cycleStorage = storageManager.GetStorageSync<DailyWorkCycle>("WorkerAttendance");

            // Workers folder = worker profile files (3.json with face encodings)
            _workerStorage = storageManager.GetStorageSync<Worker>("Workers");

            _logger = logger;
        }

        /// <summary>
        /// Get ALL registered workers, grouped by active status
        /// Shows latest activity data regardless of date
        /// </summary>
        public async Task<DashboardData> GetTodaysDashboardDataAsync()
        {
            var today = DateTime.Today;

            // Get all registered workers
            var allWorkers = await GetAllRegisteredWorkersAsync();

            // Get today's cycles
            var todaysCycles = await LoadCyclesForDateAsync(today);

            var activeWorkers = new List<WorkerStatus>();
            var inactiveWorkers = new List<WorkerStatus>();

            // Process each registered worker
            foreach (var worker in allWorkers)
            {
                // Check if worker has activity today
                var todayCycle = todaysCycles.FirstOrDefault(c => c.WorkerId == worker.Id);

                if (todayCycle != null)
                {
                    var status = DetermineWorkerStatus(todayCycle);

                    if (status.IsActive)
                    {
                        // Currently signed in
                        activeWorkers.Add(status);
                    }
                    else
                    {
                        // Signed out today - show today's data
                        inactiveWorkers.Add(status);
                    }
                }
                else
                {
                    // No activity today - find their LATEST activity from any date
                    var latestCycle = await FindLatestCycleForWorkerAsync(worker.Id);

                    if (latestCycle != null)
                    {
                        // Show latest completed work with date
                        var status = DetermineWorkerStatus(latestCycle);
                        status.IsActive = false; // Force inactive
                        status.ShowDate = true; // Flag to show the date in UI
                        inactiveWorkers.Add(status);
                    }
                    else
                    {
                        // No activity ever - show as empty
                        inactiveWorkers.Add(CreateEmptyWorkerStatus(worker));
                    }
                }
            }

            return new DashboardData
            {
                ActiveWorkers = activeWorkers.OrderBy(w => w.WorkerName).ToList(),
                InactiveWorkers = inactiveWorkers.OrderBy(w => w.WorkerName).ToList(),
                RefreshTime = DateTime.Now
            };
        }

        /// <summary>
        /// Get all registered workers from the Workers folder
        /// Worker files are named like: 3.json, 5.json (numeric IDs only)
        /// NOT the date-based cycle files like: 2025-10-02_Worker3.json
        /// </summary>
        private async Task<List<Worker>> GetAllRegisteredWorkersAsync()
        {
            var workers = new List<Worker>();

            try
            {
                var identifiers = await _workerStorage.ListIdentifiersAsync();

                // Filter to only numeric worker files (3.json, 5.json, etc.)
                // Exclude date-based cycle files (2025-10-02_Worker3.json)
                var workerFiles = identifiers
                    .Where(id =>
                    {
                        // Get filename without extension
                        var fileName = Path.GetFileNameWithoutExtension(id);
                        // Check if it's purely numeric (worker ID files)
                        return int.TryParse(fileName, out _);
                    })
                    .ToList();

                _logger.LogInformation("Found {Count} registered worker files", workerFiles.Count);

                foreach (var id in workerFiles)
                {
                    try
                    {
                        var worker = await _workerStorage.LoadAsync(id);
                        if (worker != null && !string.IsNullOrWhiteSpace(worker.Name))
                        {
                            workers.Add(worker);
                        }
                        else
                        {
                            _logger.LogWarning("Skipping invalid worker with ID: {Identifier} (empty or null name)", id);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load worker: {Identifier}", id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading registered workers");
            }

            return workers.OrderBy(w => w.Id).ToList();
        }

        /// <summary>
        /// Find the most recent work cycle for a worker (from any date)
        /// </summary>
        private async Task<DailyWorkCycle?> FindLatestCycleForWorkerAsync(int workerId)
        {
            try
            {
                var allIdentifiers = await _cycleStorage.ListIdentifiersAsync();
                var workerFiles = allIdentifiers
                    .Where(id => id.Contains($"_Worker{workerId}.json"))
                    .OrderByDescending(id => id) // Newest first (by filename date)
                    .ToList();

                if (!workerFiles.Any())
                    return null;

                // Get the most recent file
                var latestFile = workerFiles.First();

                _logger.LogDebug("Found latest cycle for Worker {WorkerId}: {FileName}", workerId, latestFile);

                return await _cycleStorage.LoadAsync(latestFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding latest cycle for Worker {WorkerId}", workerId);
                return null;
            }
        }

        /// <summary>
        /// Load all work cycles for a specific date
        /// </summary>
        private async Task<List<DailyWorkCycle>> LoadCyclesForDateAsync(DateTime date)
        {
            var cycles = new List<DailyWorkCycle>();

            try
            {
                var allIdentifiers = await _cycleStorage.ListIdentifiersAsync();
                var datePrefix = date.ToString("yyyy-MM-dd");
                var dateIdentifiers = allIdentifiers
                    .Where(id => id.StartsWith(datePrefix))
                    .ToList();

                _logger.LogDebug("Found {Count} work cycles for {Date}",
                    dateIdentifiers.Count, date.ToString("yyyy-MM-dd"));

                foreach (var identifier in dateIdentifiers)
                {
                    try
                    {
                        var cycle = await _cycleStorage.LoadAsync(identifier);
                        if (cycle != null)
                        {
                            cycles.Add(cycle);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load cycle: {Identifier}", identifier);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cycles for date {Date}", date);
            }

            return cycles;
        }

        /// <summary>
        /// Create an empty worker status for workers with no recent activity
        /// </summary>
        private WorkerStatus CreateEmptyWorkerStatus(Worker worker)
        {
            return new WorkerStatus
            {
                WorkerId = worker.Id,
                WorkerName = worker.Name,
                IsActive = false,
                LastActivity = DateTime.MinValue,
                CurrentCycle = WorkCycle.Normal,
                HasOT = false,
                CurrentDuration = null,
                NormalSignIn = null,
                NormalSignOut = null,
                NormalHoursWorked = null,
                OTSignIn = null,
                OTSignOut = null,
                OTHoursWorked = null,
                ShowDate = false
            };
        }

        /// <summary>
        /// Determine if a worker is currently active based on their last record
        /// </summary>
        private WorkerStatus DetermineWorkerStatus(DailyWorkCycle cycle)
        {
            var lastRecord = cycle.LastRecord;
            var isActive = lastRecord?.Type == AttendanceType.CheckIn;

            var status = new WorkerStatus
            {
                WorkerId = cycle.WorkerId,
                WorkerName = cycle.WorkerName,
                IsActive = isActive,
                LastActivity = lastRecord?.Timestamp ?? cycle.CycleDate,
                CycleDate = cycle.CycleDate, // Store the cycle date for display
                CurrentCycle = lastRecord?.WorkCycle ?? WorkCycle.Normal,
                HasOT = cycle.HasOT,
                ShowDate = false // Will be set to true if showing old data
            };

            // Calculate duration if currently active
            if (isActive && lastRecord != null)
            {
                status.CurrentDuration = DateTime.Now - lastRecord.Timestamp;
            }

            // Calculate detailed work hours for both Normal and OT cycles
            CalculateWorkHours(cycle, status);

            return status;
        }

        /// <summary>
        /// Calculate detailed work hours for Normal and OT cycles
        /// </summary>
        private void CalculateWorkHours(DailyWorkCycle cycle, WorkerStatus status)
        {
            // Get Normal work records (CheckIn/CheckOut pairs)
            var normalRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.Normal).ToList();
            if (normalRecords.Any())
            {
                var normalCheckIn = normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn);
                var normalCheckOut = normalRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckOut);

                status.NormalSignIn = normalCheckIn?.Timestamp;
                status.NormalSignOut = normalCheckOut?.Timestamp;

                // Calculate hours worked for Normal shift
                if (normalCheckIn != null && normalCheckOut != null)
                {
                    status.NormalHoursWorked = normalCheckOut.Timestamp - normalCheckIn.Timestamp;
                }
                else if (normalCheckIn != null && !status.IsActive)
                {
                    // Signed in but never signed out (and not currently active) - show as incomplete
                    status.NormalHoursWorked = null;
                }
            }

            // Get OT work records (CheckIn/CheckOut pairs)
            var otRecords = cycle.Records.Where(r => r.WorkCycle == WorkCycle.OT).ToList();
            if (otRecords.Any())
            {
                var otCheckIn = otRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckIn);
                var otCheckOut = otRecords.FirstOrDefault(r => r.Type == AttendanceType.CheckOut);

                status.OTSignIn = otCheckIn?.Timestamp;
                status.OTSignOut = otCheckOut?.Timestamp;

                // Calculate hours worked for OT shift
                if (otCheckIn != null && otCheckOut != null)
                {
                    status.OTHoursWorked = otCheckOut.Timestamp - otCheckIn.Timestamp;
                }
                else if (otCheckIn != null && status.IsActive && status.CurrentCycle == WorkCycle.OT)
                {
                    // Currently working OT - calculate current duration
                    status.OTHoursWorked = DateTime.Now - otCheckIn.Timestamp;
                }
            }
        }
    }
}