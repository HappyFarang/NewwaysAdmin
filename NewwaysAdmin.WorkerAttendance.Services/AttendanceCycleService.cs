// NewwaysAdmin.WorkerAttendance.Services/AttendanceCycleService.cs
// Purpose: Handle work cycle creation and OT detection logic (CYCLE-BASED, NOT DATE-BASED)
// UPDATED: MAX_OT_GAP reduced to 8 hours

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

        private readonly TimeSpan MAX_OT_GAP = TimeSpan.FromHours(8); // CHANGED from 12 to 8 hours

        public AttendanceCycleService(EnhancedStorageFactory factory, ILogger<AttendanceCycleService> logger)
        {
            _cycleStorage = factory.GetStorage<DailyWorkCycle>("Workers");
            _logger = logger;
        }

        /// <summary>
        /// Check if the next action for a worker would be OT (for confirmation dialog)
        /// </summary>
        public async Task<bool> WouldBeOTSignInAsync(int workerId)
        {
            var now = DateTime.Now;
            var activeCycle = await FindActiveWorkCycleAsync(workerId, now);
            var (actionType, workCycle) = DetermineActionType(activeCycle, now);

            return actionType == AttendanceType.CheckIn && workCycle == WorkCycle.OT;
        }

        /// <summary>
        /// Process worker sign-in/out action - CYCLE-BASED, NOT DATE-BASED
        /// </summary>
        public async Task<AttendanceRecord> ProcessWorkerActionAsync(int workerId, string workerName, double confidence)
        {
            var now = DateTime.Now;

            // Find the active work cycle, not today's date
            var activeCycle = await FindActiveWorkCycleAsync(workerId, now);

            // Determine what type of action this is
            var (actionType, workCycle) = DetermineActionType(activeCycle, now);

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

            // If no active cycle exists, create a NEW work cycle
            if (activeCycle == null)
            {
                activeCycle = new DailyWorkCycle
                {
                    WorkerId = workerId,
                    WorkerName = workerName,
                    CycleDate = now.Date, // Lock to the date of first sign-in
                    Records = new List<AttendanceRecord>()
                };

                _logger.LogInformation("Creating NEW work cycle for {WorkerName} starting {Date}",
                    workerName, now.Date.ToString("yyyy-MM-dd"));
            }

            // Add record to cycle
            activeCycle.Records.Add(record);

            // Save the cycle
            await _cycleStorage.SaveAsync(activeCycle.GetFileName(), activeCycle);

            _logger.LogInformation("Recorded {Type} for {WorkerName} in {Cycle} cycle at {Time}",
                actionType, workerName, workCycle, now.ToString("HH:mm"));

            return record;
        }

        /// <summary>
        /// Admin function: Force sign-out a worker who forgot to sign out
        /// </summary>
        public async Task<AttendanceRecord> AdminSignOutWorkerAsync(int workerId, string workerName)
        {
            var now = DateTime.Now;

            // Find the active work cycle
            var activeCycle = await FindActiveWorkCycleAsync(workerId, now);

            if (activeCycle == null || activeCycle.Records.Count == 0)
            {
                throw new InvalidOperationException($"No active work cycle found for worker {workerName}.");
            }

            var lastRecord = activeCycle.LastRecord;
            if (lastRecord == null || lastRecord.Type == AttendanceType.CheckOut)
            {
                throw new InvalidOperationException($"Worker {workerName} is already signed out.");
            }

            // Create sign-out record in the same cycle as the last check-in
            var signOutRecord = new AttendanceRecord
            {
                Id = GenerateId(),
                WorkerId = workerId,
                Timestamp = now,
                Type = AttendanceType.CheckOut,
                WorkCycle = lastRecord.WorkCycle, // Use the same cycle
                RecognitionConfidence = 100.0, // Admin action = 100% confidence
                IsSynced = false
            };

            // Add the sign-out record
            activeCycle.Records.Add(signOutRecord);

            // Save the updated cycle
            await _cycleStorage.SaveAsync(activeCycle.GetFileName(), activeCycle);

            _logger.LogWarning("Admin sign-out: {WorkerName} signed out remotely from {Cycle} cycle",
                workerName, lastRecord.WorkCycle);

            return signOutRecord;
        }

        /// <summary>
        /// Generate unique ID for attendance record
        /// </summary>
        private int GenerateId()
        {
            return (int)(DateTime.Now.Ticks % int.MaxValue);
        }

        /// <summary>
        /// Find the active work cycle for a worker (cycle-based, not date-based)
        /// Returns the cycle if the worker is currently checked in OR if they checked out recently (within MAX_OT_GAP)
        /// </summary>
        private async Task<DailyWorkCycle?> FindActiveWorkCycleAsync(int workerId, DateTime now)
        {
            try
            {
                // Get all files for this worker
                var allIdentifiers = await _cycleStorage.ListIdentifiersAsync();
                var workerFiles = allIdentifiers
                    .Where(id => id.Contains($"_Worker{workerId}.json"))
                    .OrderByDescending(id => id) // Newest first
                    .ToList();

                // Check the most recent files (last 3 days should be enough)
                foreach (var fileName in workerFiles.Take(3))
                {
                    try
                    {
                        var cycle = await _cycleStorage.LoadAsync(fileName);

                        if (cycle == null || cycle.Records.Count == 0)
                            continue;

                        var lastRecord = cycle.LastRecord!;

                        // Case 1: Currently checked in - this is the active cycle
                        if (lastRecord.Type == AttendanceType.CheckIn)
                        {
                            _logger.LogInformation("Found active cycle for Worker {WorkerId}: {FileName} (currently checked in)",
                                workerId, fileName);
                            return cycle;
                        }

                        // Case 2: Recently checked out (within MAX_OT_GAP) - might be returning for OT
                        var timeSinceLastCheckOut = now - lastRecord.Timestamp;
                        if (timeSinceLastCheckOut <= MAX_OT_GAP)
                        {
                            _logger.LogInformation("Found recent cycle for Worker {WorkerId}: {FileName} (checked out {Hours:F1}h ago)",
                                workerId, fileName, timeSinceLastCheckOut.TotalHours);
                            return cycle;
                        }

                        // Case 3: Too old - this is not the active cycle
                        _logger.LogDebug("Cycle {FileName} too old ({Hours:F1}h since last checkout)",
                            fileName, timeSinceLastCheckOut.TotalHours);
                        break; // No need to check older files
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error loading cycle file: {FileName}", fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding active work cycle for Worker {WorkerId}", workerId);
            }

            // No active cycle found
            _logger.LogInformation("No active work cycle found for Worker {WorkerId} - will create new cycle", workerId);
            return null;
        }

        /// <summary>
        /// Determine if this is check-in/out and normal/OT
        /// </summary>
        private (AttendanceType actionType, WorkCycle workCycle) DetermineActionType(DailyWorkCycle? cycle, DateTime now)
        {
            if (cycle == null || cycle.Records.Count == 0)
            {
                // First action = Normal Check-In (new cycle)
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
                // Last was check-out → This is a new check-in
                var timeSinceLastCheckOut = now - lastRecord.Timestamp;

                if (timeSinceLastCheckOut > MAX_OT_GAP)
                {
                    // Too long ago = should have created new cycle already (shouldn't happen with new logic)
                    _logger.LogWarning("Unexpected: Long gap detected: {Hours} hours since last check-out",
                        timeSinceLastCheckOut.TotalHours);
                    return (AttendanceType.CheckIn, WorkCycle.Normal);
                }
                else
                {
                    // Returning after check-out = OT
                    return (AttendanceType.CheckIn, WorkCycle.OT);
                }
            }
        }
    }
}