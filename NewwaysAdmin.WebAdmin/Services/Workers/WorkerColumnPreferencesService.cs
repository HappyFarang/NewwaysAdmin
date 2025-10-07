// File: NewwaysAdmin.WebAdmin/Services/Workers/WorkerColumnPreferencesService.cs
// Purpose: Save and load user-specific column visibility preferences

using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.WebAdmin.Models.Workers;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerColumnPreferencesService
    {
        private readonly IDataStorage<Dictionary<string, WeeklyTableColumnVisibility>> _storage;
        private readonly ILogger<WorkerColumnPreferencesService> _logger;

        public WorkerColumnPreferencesService(
            StorageManager storageManager,
            ILogger<WorkerColumnPreferencesService> logger)
        {
            // Store in System subfolder to keep it organized
           // _storage = storageManager.GetStorageSync<Dictionary<string, WeeklyTableColumnVisibility>>("System");
            _logger = logger;
        }

        /// <summary>
        /// Get column preferences for a user (returns default if none saved)
        /// </summary>
        private static readonly Dictionary<string, WeeklyTableColumnVisibility> _memoryPreferences = new();

        public async Task<WeeklyTableColumnVisibility> GetUserColumnPreferencesAsync(string username)
        {
            try
            {
                if (_memoryPreferences.TryGetValue(username, out var userPrefs))
                {
                    _logger.LogDebug("Loaded column preferences for user {Username}", username);
                    return userPrefs;
                }

                _logger.LogDebug("No saved preferences for user {Username}, using defaults", username);
                return new WeeklyTableColumnVisibility();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading column preferences for user {Username}", username);
                return new WeeklyTableColumnVisibility();
            }
        }

        /// <summary>
        /// Save column preferences for a user
        /// </summary>
        public async Task SaveUserColumnPreferencesAsync(string username, WeeklyTableColumnVisibility preferences)
        {
            try
            {
                _memoryPreferences[username] = preferences;
                _logger.LogInformation("Saved column preferences for user {Username}", username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving column preferences for user {Username}", username);
                throw;
            }
        }
    }
}