// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/CategoryDataService.cs
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Category data service - handles local cache and SignalR sync
    /// Offline-first: Always loads from cache, syncs when online
    /// </summary>
    public class CategoryDataService
    {
        private readonly ILogger<CategoryDataService> _logger;
        private readonly SyncState _syncState;
        private readonly ConnectionState _connectionState;

        private readonly string _cacheFilePath;

        // In-memory cache for fast access
        private FullCategoryData? _cachedData;

        // SignalR connection (injected from existing infrastructure)
        private HubConnection? _hubConnection;

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

            // Subscribe to connection changes
            _connectionState.OnConnectionChanged += OnConnectionStateChanged;
        }

        // ===== PUBLIC PROPERTIES =====

        /// <summary>
        /// Current local version (0 if no data)
        /// </summary>
        public int LocalVersion => _syncState.LocalVersion;

        /// <summary>
        /// True if we need to download from server
        /// </summary>
        public bool NeedsDownload => _syncState.NeedsDownload;

        /// <summary>
        /// True if we have any cached data (can work offline)
        /// </summary>
        public bool HasCachedData => _cachedData != null || File.Exists(_cacheFilePath);

        /// <summary>
        /// Last successful sync time
        /// </summary>
        public DateTime? LastSyncTime => _syncState.LastSyncTime;

        // ===== PUBLIC METHODS =====

        /// <summary>
        /// Get category data - loads from cache, syncs if online and needed
        /// </summary>
        public async Task<FullCategoryData?> GetDataAsync(bool forceSync = false)
        {
            try
            {
                // Load from cache first (fast, works offline)
                if (_cachedData == null)
                {
                    _cachedData = await LoadFromCacheAsync();
                }

                // Sync if needed or forced
                if (forceSync || _syncState.NeedsDownload || _syncState.IsFirstRun)
                {
                    if (_connectionState.IsOnline && _hubConnection != null)
                    {
                        await SyncWithServerAsync();
                    }
                    else
                    {
                        _logger.LogDebug("Offline - using cached data (v{Version})", _syncState.LocalVersion);
                    }
                }

                return _cachedData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category data");
                return _cachedData; // Return whatever we have
            }
        }

        /// <summary>
        /// Set the SignalR hub connection
        /// </summary>
        public void SetHubConnection(HubConnection hubConnection)
        {
            _hubConnection = hubConnection;
            RegisterHubHandlers();
            _logger.LogDebug("Hub connection set for CategoryDataService");
        }

        /// <summary>
        /// Manually trigger sync with server
        /// </summary>
        public async Task<bool> SyncWithServerAsync()
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot sync - not connected to server");
                return false;
            }

            try
            {
                _logger.LogInformation("Starting version exchange with server...");

                var request = new VersionExchangeMessage
                {
                    MyVersion = _syncState.LocalVersion,
                    DeviceId = GetDeviceId(),
                    DeviceType = "MAUI"
                };

                // Send version exchange via SignalR
                var response = await _hubConnection.InvokeAsync<VersionExchangeResponse>(
                    "SendMessage",
                    "MAUI_ExpenseTracker",
                    "VersionExchange",
                    request);

                if (response == null)
                {
                    _logger.LogWarning("No response from server");
                    _syncState.MarkDownloadFailed();
                    return false;
                }

                _logger.LogInformation(
                    "Server version: v{ServerVersion}, YouNeedToDownload: {NeedDownload}",
                    response.ServerVersion, response.YouNeedToDownload);

                _syncState.SetRemoteVersion(response.ServerVersion);

                if (response.YouNeedToDownload && response.Data != null)
                {
                    // Save the data
                    await SaveToCacheAsync(response.Data);
                    _cachedData = response.Data;
                    _syncState.MarkDownloadComplete(response.Data.DataVersion);

                    _logger.LogInformation(
                        "Sync complete - Downloaded v{Version}: {CatCount} categories, {LocCount} locations, {PerCount} persons",
                        response.Data.DataVersion,
                        response.Data.Categories.Count,
                        response.Data.Locations.Count,
                        response.Data.Persons.Count);

                    // Notify listeners
                    DataUpdated?.Invoke(this, response.Data);
                    return true;
                }
                else if (response.YouNeedToDownload && response.Data == null)
                {
                    // Server said we need download but didn't send data - request it explicitly
                    _logger.LogWarning("Server indicated download needed but sent no data - requesting full data");
                    return await RequestFullDataAsync();
                }
                else
                {
                    _logger.LogDebug("Already up to date (v{Version})", _syncState.LocalVersion);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during sync");
                _syncState.MarkDownloadFailed();
                SyncError?.Invoke(this, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Request full data from server (force download)
        /// </summary>
        public async Task<bool> RequestFullDataAsync()
        {
            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot request data - not connected");
                return false;
            }

            try
            {
                _logger.LogInformation("Requesting full data from server...");

                var data = await _hubConnection.InvokeAsync<FullCategoryData>(
                    "SendMessage",
                    "MAUI_ExpenseTracker",
                    "RequestFullData",
                    new { });

                if (data != null)
                {
                    await SaveToCacheAsync(data);
                    _cachedData = data;
                    _syncState.MarkDownloadComplete(data.DataVersion);

                    _logger.LogInformation("Full data received - v{Version}", data.DataVersion);

                    DataUpdated?.Invoke(this, data);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Server returned no data");
                    _syncState.MarkDownloadFailed();
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting full data");
                _syncState.MarkDownloadFailed();
                return false;
            }
        }

        // ===== PRIVATE METHODS =====

        private void RegisterHubHandlers()
        {
            if (_hubConnection == null) return;

            // Listen for server push notifications (when Blazor edits data)
            _hubConnection.On<int>("NewVersionAvailable", async (newVersion) =>
            {
                _logger.LogInformation("Server notified new version available: v{Version}", newVersion);
                _syncState.SetRemoteVersion(newVersion);

                // Auto-sync if we're online
                if (_connectionState.IsOnline)
                {
                    await SyncWithServerAsync();
                }
            });

            // Listen for initial data on connect
            _hubConnection.On<FullCategoryData>("InitialCategoryData", async (data) =>
            {
                _logger.LogInformation("Received initial data from server: v{Version}", data.DataVersion);

                if (data.DataVersion > _syncState.LocalVersion)
                {
                    await SaveToCacheAsync(data);
                    _cachedData = data;
                    _syncState.MarkDownloadComplete(data.DataVersion);

                    DataUpdated?.Invoke(this, data);
                }
            });

            _logger.LogDebug("Hub handlers registered");
        }

        private void OnConnectionStateChanged(object? sender, bool isOnline)
        {
            if (isOnline && _syncState.NeedsDownload)
            {
                _logger.LogInformation("Back online and need download - triggering sync");
                // Fire and forget - don't await in event handler
                _ = SyncWithServerAsync();
            }
        }

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
                throw; // Re-throw so caller knows save failed
            }
        }

        private string GetDeviceId()
        {
            try
            {
                // Use MAUI's device info
                return DeviceInfo.Current.Idiom.ToString() + "_" + DeviceInfo.Current.Name;
            }
            catch
            {
                return "Unknown_Device";
            }
        }

        /// <summary>
        /// Called by CategoryHubConnector when new data is received
        /// </summary>
        public void NotifyDataChanged(FullCategoryData data)
        {
            _cachedData = data;
            _logger.LogInformation("Data updated externally - v{Version}", data.DataVersion);
            DataUpdated?.Invoke(this, data);
        }
    }
}