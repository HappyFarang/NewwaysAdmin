// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/CategoryMobileService.cs
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Mobile.Services.SignalR;
using NewwaysAdmin.Mobile.Services.Cache;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Mobile service for managing categories with offline-first support
    /// </summary>
    public class CategoryMobileService
    {
        private readonly SignalREventListener _eventListener;
        private readonly SignalRMessageSender _messageSender;
        private readonly SignalRConnection _connection;
        private readonly CacheManager _cacheManager;
        private readonly ILogger<CategoryMobileService> _logger;

        private MobileCategorySync? _cachedCategories;
        private DateTime? _lastSyncTime;
        private bool _isInitialized = false;

        private const string CACHE_KEY = "mobile_categories";
        private const string LAST_SYNC_KEY = "categories_last_sync";

        public CategoryMobileService(
            SignalREventListener eventListener,
            SignalRMessageSender messageSender,
            SignalRConnection connection,
            CacheManager cacheManager,
            ILogger<CategoryMobileService> logger)
        {
            _eventListener = eventListener;
            _messageSender = messageSender;
            _connection = connection;
            _cacheManager = cacheManager;
            _logger = logger;

            // Subscribe to initial data from SignalR
            _eventListener.OnInitialDataReceived += HandleInitialDataReceivedAsync;
        }

        /// <summary>
        /// Initialize service - load cached data
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                _logger.LogInformation("Initializing CategoryMobileService...");

                // Load cached categories if available
                await LoadCachedCategoriesAsync();

                _isInitialized = true;
                _logger.LogInformation("CategoryMobileService initialized. Has cached data: {HasData}", _cachedCategories != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing CategoryMobileService");
            }
        }

        /// <summary>
        /// Get all categories (from cache or server)
        /// </summary>
        public async Task<MobileCategorySync?> GetCategoriesAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            // Return cached data if available
            if (_cachedCategories != null)
            {
                _logger.LogDebug("Returning cached categories: {Count} categories", _cachedCategories.Categories.Count);
                return _cachedCategories;
            }

            // If online and no cache, request from server
            if (_connection.IsConnected)
            {
                _logger.LogInformation("No cached data, requesting from server...");
                await RequestCategorySyncAsync();

                // Wait a bit for the response (timeout after 5 seconds)
                var timeout = DateTime.UtcNow.AddSeconds(5);
                while (_cachedCategories == null && DateTime.UtcNow < timeout)
                {
                    await Task.Delay(100);
                }

                return _cachedCategories;
            }

            // Offline and no cache
            _logger.LogWarning("No cached categories and offline");
            return null;
        }

        /// <summary>
        /// Get all business locations
        /// </summary>
        public async Task<List<MobileLocationItem>> GetLocationsAsync()
        {
            var categories = await GetCategoriesAsync();
            return categories?.Locations ?? new List<MobileLocationItem>();
        }

        /// <summary>
        /// Request category sync from server
        /// </summary>
        public async Task<bool> RequestCategorySyncAsync()
        {
            if (!_connection.IsConnected)
            {
                _logger.LogWarning("Cannot request category sync - not connected to server");
                return false;
            }

            try
            {
                _logger.LogInformation("Requesting category sync from server...");

                var success = await _messageSender.SendMessageAsync(
                    "RequestCategorySync",
                    "Server",
                    new { requestTime = DateTime.UtcNow });

                if (success)
                {
                    _logger.LogInformation("Category sync request sent successfully");
                }
                else
                {
                    _logger.LogWarning("Failed to send category sync request");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting category sync");
                return false;
            }
        }

        /// <summary>
        /// Get last sync time
        /// </summary>
        public DateTime? GetLastSyncTime()
        {
            return _lastSyncTime;
        }

        /// <summary>
        /// Check if we have cached data
        /// </summary>
        public bool HasCachedData => _cachedCategories != null;

        // ===== PRIVATE METHODS =====

        private async Task HandleInitialDataReceivedAsync(object data)
        {
            try
            {
                _logger.LogInformation("Received initial data from SignalR");

                // Parse the data - it comes wrapped in a message object
                var jsonElement = (JsonElement)data;

                // Check if it's the initial category data message
                if (jsonElement.TryGetProperty("messageType", out var messageType) &&
                    messageType.GetString() == "InitialCategoryData")
                {
                    if (jsonElement.TryGetProperty("data", out var categoryData))
                    {
                        var syncData = JsonSerializer.Deserialize<MobileCategorySync>(categoryData.GetRawText());

                        if (syncData != null)
                        {
                            await SaveCategoriesAsync(syncData);
                            _logger.LogInformation("Saved initial category data: {Count} categories, {LocationCount} locations",
                                syncData.Categories.Count, syncData.Locations.Count);
                        }
                    }
                }
                else
                {
                    // Try parsing as direct MobileCategorySync
                    var syncData = JsonSerializer.Deserialize<MobileCategorySync>(jsonElement.GetRawText());
                    if (syncData != null)
                    {
                        await SaveCategoriesAsync(syncData);
                        _logger.LogInformation("Saved category data: {Count} categories", syncData.Categories.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling initial data");
            }
        }

        private async Task LoadCachedCategoriesAsync()
        {
            try
            {
                // Try to load categories from cache
                var cachedItems = await _cacheManager.GetPendingSyncItemsAsync();
                var categoryCache = cachedItems.FirstOrDefault(c => c.DataType == CACHE_KEY);

                if (categoryCache != null)
                {
                    _cachedCategories = await _cacheManager.GetCachedDataAsync<MobileCategorySync>(categoryCache.Id);

                    if (_cachedCategories != null)
                    {
                        _lastSyncTime = categoryCache.CreatedAt;
                        _logger.LogInformation("Loaded cached categories: {Count} categories, Last sync: {LastSync}",
                            _cachedCategories.Categories.Count, _lastSyncTime);
                    }
                }
                else
                {
                    _logger.LogInformation("No cached categories found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cached categories");
            }
        }

        private async Task SaveCategoriesAsync(MobileCategorySync syncData)
        {
            try
            {
                _cachedCategories = syncData;
                _lastSyncTime = DateTime.UtcNow;

                // Cache the data using CacheManager
                await _cacheManager.CacheInlineDataAsync(
                    syncData,
                    CACHE_KEY,
                    "CategorySync",
                    NewwaysAdmin.Mobile.Services.Cache.CacheRetentionPolicy.KeepAfterSync);

                // Also save last sync time
                await _cacheManager.CacheInlineDataAsync(
                    new { lastSync = _lastSyncTime },
                    LAST_SYNC_KEY,
                    "CategoryMetadata",
                    NewwaysAdmin.Mobile.Services.Cache.CacheRetentionPolicy.KeepAfterSync);

                _logger.LogInformation("Cached category data: {Count} categories, {LocationCount} locations",
                    syncData.Categories.Count, syncData.Locations.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving categories to cache");
            }
        }
    }
}
