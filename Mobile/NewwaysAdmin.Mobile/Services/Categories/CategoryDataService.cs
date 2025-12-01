// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/CategoryDataService.cs
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Category data service - handles local cache only
    /// SignalR communication is handled by CategoryHubConnector
    /// </summary>
    public class CategoryDataService
    {
        private readonly ILogger<CategoryDataService> _logger;
        private readonly SyncState _syncState;
        private readonly ConnectionState _connectionState;

        private readonly string _cacheFilePath;

        // In-memory cache for fast access
        private FullCategoryData? _cachedData;

        // Events
        public event EventHandler<FullCategoryData>? DataUpdated;
        public event EventHandler<string>? SyncError;

        public CategoryDataService(
            ILogger<CategoryDataService> logger,
            SyncState syncState,
            ConnectionState connectionState)
        {
            _logger = logger;
            _syncState = syncState;
            _connectionState = connectionState;

            // Cache file path
            var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "CategoryCache");
            Directory.CreateDirectory(cacheDir);
            _cacheFilePath = Path.Combine(cacheDir, "category_data.json");
        }

        // ===== PUBLIC PROPERTIES =====

        public int LocalVersion => _syncState.LocalVersion;
        public bool NeedsDownload => _syncState.NeedsDownload;
        public bool HasCachedData => _cachedData != null || File.Exists(_cacheFilePath);
        public DateTime? LastSyncTime => _syncState.LastSyncTime;

        // ===== PUBLIC METHODS =====

        /// <summary>
        /// Get category data from cache
        /// </summary>
        public async Task<FullCategoryData?> GetDataAsync()
        {
            try
            {
                if (_cachedData == null)
                {
                    _cachedData = await LoadFromCacheAsync();
                }
                return _cachedData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category data");
                return _cachedData;
            }
        }

        /// <summary>
        /// Save data to cache - called by CategoryHubConnector when data is received
        /// </summary>
        public async Task SaveDataAsync(FullCategoryData data)
        {
            try
            {
                await SaveToCacheAsync(data);
                _cachedData = data;
                _logger.LogInformation(
                    "Saved data v{Version}: {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    data.DataVersion,
                    data.Categories.Count,
                    data.Locations.Count,
                    data.Persons.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data");
                throw;
            }
        }

        /// <summary>
        /// Called by CategoryHubConnector when new data is received from server
        /// </summary>
        public void NotifyDataChanged(FullCategoryData data)
        {
            _cachedData = data;
            _logger.LogInformation("Data updated externally - v{Version}", data.DataVersion);
            DataUpdated?.Invoke(this, data);
        }

        /// <summary>
        /// Notify listeners of sync error
        /// </summary>
        public void NotifySyncError(string message)
        {
            SyncError?.Invoke(this, message);
        }

        // ===== PRIVATE METHODS =====

        private async Task<FullCategoryData?> LoadFromCacheAsync()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _logger.LogDebug("No cache file found - first run");
                    return null;
                }

                var json = await File.ReadAllTextAsync(_cacheFilePath);
                var data = JsonSerializer.Deserialize<FullCategoryData>(json);

                if (data != null)
                {
                    _logger.LogInformation(
                        "Loaded from cache: v{Version} - {CatCount} categories, {LocCount} locations, {PerCount} persons",
                        data.DataVersion,
                        data.Categories.Count,
                        data.Locations.Count,
                        data.Persons.Count);
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading from cache");
                return null;
            }
        }

        private async Task SaveToCacheAsync(FullCategoryData data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_cacheFilePath, json);
                _logger.LogDebug("Saved to cache: v{Version}", data.DataVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving to cache");
                throw;
            }
        }
    }
}