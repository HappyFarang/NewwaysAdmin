// NewwaysAdmin.WorkerAttendance.Services/AttendanceCycleService.cs
// Purpose: Handle daily work cycle creation and OT detection logic

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.Services
{
    public class AttendanceCycleService
    {
        private readonly IDataStorage<DailyWorkCycle> _cycleStorage;
        private readonly ILogger<AttendanceCycleService> _logger;

        private readonly TimeSpan MAX_OT_GAP = TimeSpan.FromHours(12);

        public AttendanceCycleService(EnhancedStorageFactory factory, ILogger<AttendanceCycleService> logger)
        {
            _cycleStorage = factory.GetStorage<DailyWorkCycle>("Workers");
            _logger = logger;
        }

        /// <summary>
        /// Process worker sign-in action and save to daily work cycle
        /// </summary>
        public async Task<AttendanceRecord> ProcessWorkerActionAsync(int workerId, string workerName, double confidence)
        {
            var now = DateTime.Now;
            var today = now.Date;

            // Try to get today's work cycle for this worker
            var todaysCycle = await GetTodaysCycleAsync(workerId, today);

            // Determine what type of action this is
            var (actionType, workCycle) = DetermineActionType(todaysCycle, now);

            // Create the attendance record
            var record = new AttendanceRecord
            {
                Id = GenerateId(),
                WorkerId = workerId,
                Timestamp = now,
                Type = actionType,
                WorkCycle = workCycle,
                RecognitionConfidence = confidence
            };

            // If no cycle exists, create a new one
            if (todaysCycle == null)
            {
                todaysCycle = new DailyWorkCycle
                {
                    WorkerId = workerId,
                    WorkerName = workerName,
                    CycleDate = today,
                    Records = new List<AttendanceRecord>()
                };
            }

            // Add record to cycle
            todaysCycle.Records.Add(record);

            // Save the cycle
            await _cycleStorage.SaveAsync(todaysCycle.GetFileName(), todaysCycle);

            _logger.LogInformation("Recorded {Type} for {WorkerName} in {Cycle} cycle at {Time}",
                actionType, workerName, workCycle, now.ToString("HH:mm"));

            return record;
        }

        /// <summary>
        /// Generate unique ID for attendance record
        /// </summary>
        private int GenerateId()
        {
            return (int)(DateTime.Now.Ticks % int.MaxValue);
        }

        /// <summary>
        /// Determine if this is check-in/out and normal/OT
        /// </summary>
        private (AttendanceType actionType, WorkCycle workCycle) DetermineActionType(DailyWorkCycle? cycle, DateTime now)
        {
            if (cycle == null || cycle.Records.Count == 0)
            {
                // First action of the day = Normal Check-In
                return (AttendanceType.CheckIn, WorkCycle.Normal);
            }

            var lastRecord = cycle.LastRecord!;

            if (lastRecord.Type == AttendanceType.CheckIn)
            {
                // Last was check-in → This is check-out (same cycle)
                return (AttendanceType.CheckOut, lastRecord.WorkCycle);
            }
            else
            {
                // Last was check-out → This is check-in
                var timeSinceLastCheckOut = now - lastRecord.Timestamp;

                if (timeSinceLastCheckOut > MAX_OT_GAP)
                {
                    // Too long ago = new normal cycle (should be new file, but edge case)
                    _logger.LogWarning("Long gap detected: {Hours} hours since last check-out", timeSinceLastCheckOut.TotalHours);
                    return (AttendanceType.CheckIn, WorkCycle.Normal);
                }
                else
                {
                    // Second sign-in today = OT
                    return (AttendanceType.CheckIn, WorkCycle.OT);
                }
            }
        }

        /// <summary>
        /// Get today's work cycle for a worker
        /// </summary>
        private async Task<DailyWorkCycle?> GetTodaysCycleAsync(int workerId, DateTime date)
        {
            var fileName = $"{date:yyyy-MM-dd}_Worker{workerId}.json";

            try
            {
                if (await _cycleStorage.ExistsAsync(fileName))
                {
                    return await _cycleStorage.LoadAsync(fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cycle file: {FileName}", fileName);
            }

            return null;
        }
    }
}