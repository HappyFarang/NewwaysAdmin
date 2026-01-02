// File: NewwaysAdmin.Mobile/Services/BankSlip/BankSlipSettingsService.cs
// Manages bank slip monitoring settings - saves/loads the BankSlipSyncSettings
// UPDATED: Added SyncToDate for historical batch uploads

using System.Text.Json;

namespace NewwaysAdmin.Mobile.Services.BankSlip
{
    /// <summary>
    /// A folder to monitor for bank slip images
    /// </summary>
    public class MonitoredFolder
    {
        /// <summary>
        /// Full path to the folder on the device (from folder picker)
        /// e.g., "/storage/emulated/0/DCIM/KPLUS"
        /// </summary>
        public string DeviceFolderPath { get; set; } = "";

        /// <summary>
        /// User-defined pattern identifier sent to server
        /// e.g., "KPLUS_Thomas", "KBIZ_Office_Tablet"
        /// This becomes the subfolder in BankSlipsBin on the server
        /// </summary>
        public string PatternIdentifier { get; set; } = "";

        /// <summary>
        /// Display name for UI (derived from path if not set)
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(PatternIdentifier)
            ? Path.GetFileName(DeviceFolderPath)
            : PatternIdentifier;
    }

    /// <summary>
    /// User-configurable settings for bank slip monitoring
    /// </summary>
    public class BankSlipSyncSettings
    {
        /// <summary>
        /// Folders to monitor with their pattern identifiers
        /// </summary>
        public List<MonitoredFolder> MonitoredFolders { get; set; } = new();

        /// <summary>
        /// Is auto-sync enabled?
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Only sync images newer than this (prevents uploading old history)
        /// </summary>
        public DateTime SyncFromDate { get; set; } = DateTime.Now;

        /// <summary>
        /// Only sync images older than this (for historical batch uploads)
        /// null = no upper limit (realtime mode)
        /// </summary>
        public DateTime? SyncToDate { get; set; } = null;
    }

    public class BankSlipSettingsService
    {
        private readonly string _settingsFilePath;
        private BankSlipSyncSettings? _cachedSettings;

        public BankSlipSettingsService()
        {
            var folder = Path.Combine(FileSystem.AppDataDirectory, "BankSlipSync");
            Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, "sync_settings.json");
        }

        /// <summary>
        /// Load settings from storage
        /// </summary>
        public async Task<BankSlipSyncSettings> LoadSettingsAsync()
        {
            if (_cachedSettings is not null)
                return _cachedSettings;

            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _cachedSettings = JsonSerializer.Deserialize<BankSlipSyncSettings>(json);
                }
            }
            catch
            {
                // If load fails, use defaults
            }

            if (_cachedSettings is null)
                _cachedSettings = new BankSlipSyncSettings();

            return _cachedSettings;
        }

        /// <summary>
        /// Save settings to storage
        /// </summary>
        public async Task SaveSettingsAsync(BankSlipSyncSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_settingsFilePath, json);
                _cachedSettings = settings;
            }
            catch
            {
                // Log error but don't throw
            }
        }

        /// <summary>
        /// Add a folder to monitor
        /// </summary>
        public async Task AddFolderAsync(string deviceFolderPath, string patternIdentifier)
        {
            var settings = await LoadSettingsAsync();

            // Check if pattern identifier already exists
            if (settings.MonitoredFolders.Any(f =>
                f.PatternIdentifier.Equals(patternIdentifier, StringComparison.OrdinalIgnoreCase)))
            {
                return; // Already exists
            }

            settings.MonitoredFolders.Add(new MonitoredFolder
            {
                DeviceFolderPath = deviceFolderPath,
                PatternIdentifier = patternIdentifier
            });

            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Remove a folder from monitoring by pattern identifier
        /// </summary>
        public async Task RemoveFolderAsync(string patternIdentifier)
        {
            var settings = await LoadSettingsAsync();
            settings.MonitoredFolders.RemoveAll(f =>
                f.PatternIdentifier.Equals(patternIdentifier, StringComparison.OrdinalIgnoreCase));
            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Toggle the entire sync system
        /// </summary>
        public async Task SetEnabledAsync(bool enabled)
        {
            var settings = await LoadSettingsAsync();
            settings.IsEnabled = enabled;
            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Update the sync-from date
        /// </summary>
        public async Task SetSyncFromDateAsync(DateTime date)
        {
            var settings = await LoadSettingsAsync();
            settings.SyncFromDate = date;
            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Update the sync-to date (for historical batch uploads)
        /// </summary>
        public async Task SetSyncToDateAsync(DateTime? date)
        {
            var settings = await LoadSettingsAsync();
            settings.SyncToDate = date;
            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Set date range for batch uploads
        /// </summary>
        public async Task SetDateRangeAsync(DateTime fromDate, DateTime? toDate)
        {
            var settings = await LoadSettingsAsync();
            settings.SyncFromDate = fromDate;
            settings.SyncToDate = toDate;
            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Clear the sync-to date (switch to realtime mode)
        /// </summary>
        public async Task ClearSyncToDateAsync()
        {
            var settings = await LoadSettingsAsync();
            settings.SyncToDate = null;
            await SaveSettingsAsync(settings);
        }

        /// <summary>
        /// Clear cached settings (force reload on next access)
        /// </summary>
        public void ClearCache()
        {
            _cachedSettings = null;
        }
    }
}