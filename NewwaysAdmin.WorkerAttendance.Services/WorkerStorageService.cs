// File: NewwaysAdmin.WorkerAttendance.Services/WorkerStorageService.cs
// Purpose: Storage service for worker data using IO Manager system

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;  // For IDataStorage<T>
using NewwaysAdmin.Shared.IO.Structure;  // For EnhancedStorageFactory
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.Services
{
    public class WorkerStorageService
    {
        private readonly IDataStorage<Worker> _workerStorage;
        private readonly IDataStorage<AttendanceRecord> _attendanceStorage;
        private readonly ILogger<WorkerStorageService> _logger;

        public WorkerStorageService(EnhancedStorageFactory factory, ILogger<WorkerStorageService> logger)
        {
            _workerStorage = factory.GetStorage<Worker>("Workers");
            _attendanceStorage = factory.GetStorage<AttendanceRecord>("AttendanceRecords");
            _logger = logger;
        }

        public async Task<List<Worker>> GetAllWorkersAsync()
        {
            var identifiers = await _workerStorage.ListIdentifiersAsync();
            var workers = new List<Worker>();

            foreach (var id in identifiers)
            {
                var worker = await _workerStorage.LoadAsync(id);
                workers.Add(worker);
            }

            return workers;
        }

        public async Task<Worker?> GetWorkerByIdAsync(int workerId)
        {
            try
            {
                return await _workerStorage.LoadAsync(workerId.ToString());
            }
            catch
            {
                return null;
            }
        }

        public async Task SaveWorkerAsync(Worker worker)
        {
            await _workerStorage.SaveAsync(worker.Id.ToString(), worker);
            _logger.LogInformation("Saved worker: {WorkerName} (ID: {WorkerId})", worker.Name, worker.Id);
        }

        public async Task DeleteWorkerAsync(int workerId)
        {
            try
            {
                await _workerStorage.DeleteAsync(workerId.ToString());
                _logger.LogInformation("Deleted worker with ID: {WorkerId}", workerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting worker with ID: {WorkerId}", workerId);
                throw;
            }
        }

        public async Task RecordAttendanceAsync(AttendanceRecord record)
        {
            var fileName = $"{DateTime.Now:yyyy-MM-dd}_{record.Id}";
            await _attendanceStorage.SaveAsync(fileName, record);
            _logger.LogInformation("Recorded attendance for worker {WorkerId}: {Type}", record.WorkerId, record.Type);
        }

        public async Task<List<AttendanceRecord>> GetTodaysAttendanceAsync()
        {
            var identifiers = await _attendanceStorage.ListIdentifiersAsync();
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var todaysRecords = new List<AttendanceRecord>();

            foreach (var id in identifiers.Where(i => i.StartsWith(today)))
            {
                var record = await _attendanceStorage.LoadAsync(id);
                todaysRecords.Add(record);
            }

            return todaysRecords.OrderBy(r => r.Timestamp).ToList();
        }
    }
}