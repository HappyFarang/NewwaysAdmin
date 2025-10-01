// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerDashboardService.cs
// Purpose: Load and process worker attendance data for dashboard display

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
        private readonly ILogger<WorkerDashboardService> _logger;

        public WorkerDashboardService(
            StorageManager storageManager,
            ILogger<WorkerDashboardService> logger)
        {
            _cycleStorage = storageManager.GetStorageSync<DailyWorkCycle>("WorkerAttendance");
            _logger = logger;
        }

        /// <summary>
        /// Get all workers with activity today, grouped by active status
        /// </summary>
        public async Task<DashboardData> GetTodaysDashboardDataAsync()
        {
            var today = DateTime.Today;
            var allCycles = await LoadTodaysCyclesAsync(today);

            var activeWorkers = new List<WorkerStatus>();
            var inactiveWorkers = new List<WorkerStatus>();

            foreach (var cycle in allCycles)
            {
                var status = DetermineWorkerStatus(cycle);

                if (status.IsActive)
                    activeWorkers.Add(status);
                else
                    inactiveWorkers.Add(status);
            }

            return new DashboardData
            {
                ActiveWorkers = activeWorkers.OrderBy(w => w.WorkerName).ToList(),
                InactiveWorkers = inactiveWorkers.OrderBy(w => w.WorkerName).ToList(),
                RefreshTime = DateTime.Now
            };
        }

        /// <summary>
        /// Load all work cycles for a specific date
        /// </summary>
        private async Task<List<DailyWorkCycle>> LoadTodaysCyclesAsync(DateTime date)
        {
            var cycles = new List<DailyWorkCycle>();

            try
            {
                var allIdentifiers = await _cycleStorage.ListIdentifiersAsync();
                var todaysPrefix = date.ToString("yyyy-MM-dd");
                var todaysIdentifiers = allIdentifiers
                    .Where(id => id.StartsWith(todaysPrefix))
                    .ToList();

                _logger.LogInformation("Found {Count} work cycles for {Date}",
                    todaysIdentifiers.Count, date.ToShortDateString());

                foreach (var identifier in todaysIdentifiers)
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
                _logger.LogError(ex, "Error loading today's cycles");
            }

            return cycles;
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
                CurrentCycle = lastRecord?.WorkCycle ?? WorkCycle.Normal,
                HasOT = cycle.HasOT
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