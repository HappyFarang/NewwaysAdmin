// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerSettingsService.cs
// Purpose: Manage worker settings (load, save, update)

using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerSettingsService
    {
        private readonly IDataStorage<WorkerSettings> _storage;
        private readonly ILogger<WorkerSettingsService> _logger;

        public WorkerSettingsService(
            StorageManager storageManager,
            ILogger<WorkerSettingsService> logger)
        {
            _storage = storageManager.GetStorageSync<WorkerSettings>("WorkerSettings");
            _logger = logger;
        }

        /// <summary>
        /// Get settings for a specific worker
        /// Returns default settings if none exist
        /// </summary>
        public async Task<WorkerSettings> GetSettingsAsync(int workerId, string workerName)
        {
            try
            {
                var settings = await _storage.LoadAsync(workerId.ToString());
                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogInformation("No settings found for worker {WorkerId}, returning defaults", workerId);

                // Return default settings
                return new WorkerSettings
                {
                    WorkerId = workerId,
                    WorkerName = workerName,
                    ExpectedHoursPerDay = 8.0m,
                    ExpectedArrivalTime = new TimeSpan(8, 0, 0),
                    DailyPayRate = 350m,
                    OvertimeHourlyRate = 50m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
            }
        }

        /// <summary>
        /// Save or update worker settings
        /// </summary>
        public async Task SaveSettingsAsync(WorkerSettings settings)
        {
            try
            {
                settings.UpdatedAt = DateTime.Now;
                await _storage.SaveAsync(settings.WorkerId.ToString(), settings);
                _logger.LogInformation("Saved settings for worker {WorkerId}", settings.WorkerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save settings for worker {WorkerId}", settings.WorkerId);
                throw;
            }
        }

        /// <summary>
        /// Check if settings exist for a worker
        /// </summary>
        public async Task<bool> SettingsExistAsync(int workerId)
        {
            try
            {
                var identifiers = await _storage.ListIdentifiersAsync();
                return identifiers.Contains(workerId.ToString());
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get all workers that have settings configured
        /// </summary>
        public async Task<List<WorkerSettings>> GetAllSettingsAsync()
        {
            var allSettings = new List<WorkerSettings>();

            try
            {
                var identifiers = await _storage.ListIdentifiersAsync();

                foreach (var id in identifiers)
                {
                    try
                    {
                        var settings = await _storage.LoadAsync(id);
                        allSettings.Add(settings);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load settings for worker {WorkerId}", id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load all worker settings");
            }

            return allSettings.OrderBy(s => s.WorkerName).ToList();
        }

        /// <summary>
        /// Delete settings for a worker
        /// </summary>
        public async Task DeleteSettingsAsync(int workerId)
        {
            try
            {
                await _storage.DeleteAsync(workerId.ToString());
                _logger.LogInformation("Deleted settings for worker {WorkerId}", workerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete settings for worker {WorkerId}", workerId);
                throw;
            }
        }
    }
}