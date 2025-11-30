// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/MobileCategoryService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Cache;
using NewwaysAdmin.Mobile.Services.Sync;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Mobile category service - handles category browsing, selection, and clipboard operations
    /// Single responsibility: Category management for mobile app with offline-first approach
    /// Uses existing SyncCoordinator and CacheManager foundation
    /// </summary>
    public class MobileCategoryService
    {
        private readonly ILogger<MobileCategoryService> _logger;
        private readonly SyncCoordinator _syncCoordinator;
        private readonly CacheManager _cacheManager;
        private readonly EnhancedStorageFactory _storageFactory;

        // Storage keys
        private const string CATEGORIES_CACHE_KEY = "cached_categories.json";
        private const string LAST_SYNC_KEY = "last_category_sync.json";
        private const string SELECTED_LOCATION_KEY = "selected_location.json";

        // Cache retention - keep categories after sync for offline use
        private const CacheRetentionPolicy CATEGORY_RETENTION = CacheRetentionPolicy.KeepAfterSync;

        public MobileCategoryService(
            ILogger<MobileCategoryService> logger,
            SyncCoordinator syncCoordinator,
            CacheManager cacheManager,
            EnhancedStorageFactory storageFactory)
        {
            _logger = logger;
            _syncCoordinator = syncCoordinator;
            _cacheManager = cacheManager;
            _storageFactory = storageFactory;
        }

        // ===== CATEGORY OPERATIONS =====

        /// <summary>
        /// Get categories - tries online first, falls back to cached offline data
        /// </summary>
        public async Task<CategorySystem?> GetCategoriesAsync()
        {
            try
            {
                _logger.LogInformation("Getting categories - attempting online sync first");

                // Try to sync fresh data from server
                var syncSuccess = await TrySyncCategoriesAsync();

                if (syncSuccess)
                {
                    _logger.LogInformation("Successfully synced categories from server");
                    return await LoadCachedCategoriesAsync();
                }
                else
                {
                    _logger.LogWarning("Server sync failed - using cached categories");
                    var cached = await LoadCachedCategoriesAsync();

                    if (cached == null)
                    {
                        _logger.LogError("No cached categories available and server sync failed");
                        return CreateEmptyCategorySystem();
                    }

                    return cached;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting categories");

                // Fallback to cached data
                var cached = await LoadCachedCategoriesAsync();
                return cached ?? CreateEmptyCategorySystem();
            }
        }

        /// <summary>
        /// Request fresh categories from server via SignalR
        /// </summary>
        public async Task<bool> RequestCategoriesFromServerAsync()
        {
            try
            {
                _logger.LogInformation("Requesting fresh categories from server");

                // Create a proper class instead of anonymous type
                var requestData = new CategoryRequest
                {
                    RequestType = "GetCategories",
                    Timestamp = DateTime.UtcNow
                };

                await _syncCoordinator.CacheAndSyncAsync(
                    requestData,
                    "CategoryRequest",
                    "RequestCategories",
                    CacheRetentionPolicy.DeleteAfterSync
                );

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting categories from server");
                return false;
            }
        }

        /// <summary>
        /// Save categories received from server
        /// </summary>
        public async Task SaveCategoriesFromServerAsync(CategorySystem categorySystem)
        {
            try
            {
                _logger.LogInformation("Saving categories received from server - Version: {Version}", categorySystem.Version);

                // Cache categories using CacheManager with correct method signature
                await _cacheManager.CacheInlineDataAsync(
                    categorySystem,
                    "CategoryData",
                    "CategorySync",  // messageType parameter
                    CATEGORY_RETENTION
                );

                // Update last sync timestamp
                var syncInfo = new CategorySyncInfo
                {
                    LastSyncDate = DateTime.UtcNow,
                    ServerVersion = categorySystem.Version,
                    CategoryCount = categorySystem.Categories?.Count ?? 0,
                    Success = true
                };

                await SaveLastSyncInfoAsync(syncInfo);

                _logger.LogInformation("Successfully cached {Count} categories from server", syncInfo.CategoryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving categories from server");
                throw;
            }
        }

        // ===== CLIPBOARD OPERATIONS =====

        /// <summary>
        /// Format category path for clipboard copy
        /// Returns: "Transportation/Green Buses"
        /// </summary>
        public string FormatCategoryPath(Category category, SubCategory subCategory)
        {
            if (category == null || subCategory == null)
            {
                _logger.LogWarning("Cannot format category path - category or subcategory is null");
                return string.Empty;
            }

            var path = $"{category.Name}/{subCategory.Name}";
            _logger.LogInformation("Formatted category path: {Path}", path);
            return path;
        }

        /// <summary>
        /// Copy category path to clipboard
        /// </summary>
        public async Task<bool> CopyCategoryToClipboardAsync(Category category, SubCategory subCategory)
        {
            try
            {
                var categoryPath = FormatCategoryPath(category, subCategory);

                if (string.IsNullOrEmpty(categoryPath))
                {
                    return false;
                }

                // Use MAUI clipboard API
                await Clipboard.Default.SetTextAsync(categoryPath);

                _logger.LogInformation("Copied to clipboard: {Path}", categoryPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying category to clipboard");
                return false;
            }
        }

        // ===== LOCATION OPERATIONS =====

        /// <summary>
        /// Get selected location (or null if "No Location" selected)
        /// </summary>
        public async Task<BusinessLocation?> GetSelectedLocationAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<BusinessLocation>("MobileUserSettings");
                return await storage.LoadAsync(SELECTED_LOCATION_KEY);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting selected location");
                return null;
            }
        }

        /// <summary>
        /// Set selected location (pass null for "No Location")
        /// </summary>
        public async Task SetSelectedLocationAsync(BusinessLocation? location)
        {
            try
            {
                var storage = _storageFactory.GetStorage<BusinessLocation>("MobileUserSettings");

                if (location == null)
                {
                    // Delete the file to represent "No Location"
                    await storage.DeleteAsync(SELECTED_LOCATION_KEY);
                    _logger.LogInformation("Cleared selected location (No Location)");
                }
                else
                {
                    await storage.SaveAsync(SELECTED_LOCATION_KEY, location);
                    _logger.LogInformation("Set selected location: {LocationName}", location.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting selected location");
                throw;
            }
        }

        // ===== SYNC STATUS =====

        /// <summary>
        /// Get last sync information
        /// </summary>
        public async Task<CategorySyncInfo?> GetLastSyncInfoAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<CategorySyncInfo>("MobileUserSettings");
                return await storage.LoadAsync(LAST_SYNC_KEY);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last sync info");
                return null;
            }
        }

        /// <summary>
        /// Check if categories are available (either cached or fresh)
        /// </summary>
        public async Task<bool> HasCategoriesAsync()
        {
            try
            {
                var categories = await LoadCachedCategoriesAsync();
                return categories?.Categories?.Any() == true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if categories are available");
                return false;
            }
        }

        // ===== PRIVATE HELPER METHODS =====

        /// <summary>
        /// Try to sync categories from server
        /// </summary>
        private async Task<bool> TrySyncCategoriesAsync()
        {
            try
            {
                // Check if we're online and can sync
                var syncStatus = await _syncCoordinator.GetSyncStatusAsync();

                if (!syncStatus.IsOnline)
                {
                    _logger.LogInformation("Offline - cannot sync categories");
                    return false;
                }

                // Request fresh categories from server
                return await RequestCategoriesFromServerAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during category sync attempt");
                return false;
            }
        }

        /// <summary>
        /// Load cached categories from local storage
        /// </summary>
        private async Task<CategorySystem?> LoadCachedCategoriesAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<CategorySystem>("MobileCategories");
                return await storage.LoadAsync(CATEGORIES_CACHE_KEY);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cached categories");
                return null;
            }
        }

        /// <summary>
        /// Save last sync information
        /// </summary>
        private async Task SaveLastSyncInfoAsync(CategorySyncInfo syncInfo)
        {
            try
            {
                var storage = _storageFactory.GetStorage<CategorySyncInfo>("MobileUserSettings");
                await storage.SaveAsync(LAST_SYNC_KEY, syncInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving last sync info");
                throw;
            }
        }

        /// <summary>
        /// Create empty category system as fallback
        /// </summary>
        private CategorySystem CreateEmptyCategorySystem()
        {
            return new CategorySystem
            {
                Categories = new List<Category>(),
                LastModified = DateTime.UtcNow,
                Version = 0,
                ModifiedBy = "Mobile App"
            };
        }
    }

    // ===== SUPPORTING DATA MODELS =====

    /// <summary>
    /// Request data for category sync
    /// </summary>
    public class CategoryRequest
    {
        public string RequestType { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Tracks category sync status and metadata
    /// </summary>
    public class CategorySyncInfo
    {
        public DateTime LastSyncDate { get; set; }
        public int ServerVersion { get; set; }
        public int CategoryCount { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}