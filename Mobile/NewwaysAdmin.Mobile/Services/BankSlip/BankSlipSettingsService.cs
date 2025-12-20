// File: NewwaysAdmin.Mobile/Services/BankSlip/BankSlipSettingsService.cs
// Manages bank slip monitoring settings - saves/loads the BankSlipSyncSettings

using System.Text.Json;
using Microsoft.Maui.Storage;

namespace NewwaysAdmin.Mobile.Services.BankSlip
{
    /// <summary>
    /// User-configurable settings for bank slip monitoring
    /// </summary>
    public class BankSlipSyncSettings
    {
        /// <summary>
        /// Folder names to monitor (relative to DCIM/Pictures)
        /// e.g. "kbiz", "kplus", "bangkokbank"
        /// </summary>
        public List<string> MonitoredFolders { get; set; } = new();

        /// <summary>
        /// Is auto-sync enabled?
        /// </summary>
        public bool IsEnabled { get; set; } = false;

        /// <summary>
        /// Only sync images newer than this (prevents uploading old history)
        /// </summary>
        public DateTime SyncFromDate { get; set; } = DateTime.UtcNow;
    }

    public class BankSlipSettingsService
    {
        private readonly string _settingsFilePath;
        private BankSlipSyncSettings? _cachedSettings;

        // Default folder names that banking apps commonly use
        public static readonly List<FolderOption> AvailableFolders = new()
        {
            new FolderOption("kbiz", "KBIZ", "K-Bank Business App"),
            new FolderOption("kplus", "KPlus", "K-Bank Plus App"),
            new FolderOption("scb", "SCB", "SCB Easy App"),
            new FolderOption("bangkokbank", "BangkokBank", "Bangkok Bank Mobile"),
            new FolderOption("bills", "Bills", "General receipts/bills")
        };

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
        public async Task AddFolderAsync(string folderName)
        {
            var settings = await LoadSettingsAsync();

            if (!settings.MonitoredFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
            {
                settings.MonitoredFolders.Add(folderName);
                await SaveSettingsAsync(settings);
            }
        }

        /// <summary>
        /// Remove a folder from monitoring
        /// </summary>
        public async Task RemoveFolderAsync(string folderName)
        {
            var settings = await LoadSettingsAsync();
            settings.MonitoredFolders.RemoveAll(f =>
                f.Equals(folderName, StringComparison.OrdinalIgnoreCase));
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
        /// Clear cached settings (force reload on next access)
        /// </summary>
        public void ClearCache()
        {
            _cachedSettings = null;
        }
    }

    /// <summary>
    /// Display info for available folder options
    /// </summary>
    public class FolderOption
    {
        public string FolderName { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public FolderOption(string folderName, string displayName, string description)
        {
            FolderName = folderName;
            DisplayName = displayName;
            Description = description;
        }
    }
}