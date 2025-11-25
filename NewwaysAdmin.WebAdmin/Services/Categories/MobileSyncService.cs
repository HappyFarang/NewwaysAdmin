// File: NewwaysAdmin.WebAdmin/Services/Categories/MobileSyncService.cs
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Shared.IO.Structure;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles mobile sync data generation and caching
    /// Optimizes category data for MAUI consumption
    /// </summary>
    public class MobileSyncService
    {
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly CategoryStorageService _storageService;
        private readonly CategoryUsageService _usageService;
        private readonly ILogger<MobileSyncService> _logger;

        private const string MOBILE_SYNC_FILE = "mobile_categories.json";

        public MobileSyncService(
            EnhancedStorageFactory storageFactory,
            CategoryStorageService storageService,
            CategoryUsageService usageService,
            ILogger<MobileSyncService> logger)
        {
            _storageFactory = storageFactory;
            _storageService = storageService;
            _usageService = usageService;
            _logger = logger;
        }

        // ===== MOBILE SYNC DATA =====

        public async Task<MobileCategorySync> GetMobileSyncDataAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<MobileCategorySync>("CategorySync");
                var syncData = await storage.LoadAsync(MOBILE_SYNC_FILE);

                if (syncData == null)
                {
                    _logger.LogInformation("No mobile sync data found, regenerating from category system");
                    return await RegenerateMobileSyncAsync();
                }

                return syncData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading mobile sync data");
                throw;
            }
        }

        public async Task<MobileCategorySync> RegenerateMobileSyncAsync()
        {
            try
            {
                _logger.LogDebug("Regenerating mobile sync data");

                var categorySystem = await _storageService.LoadCategorySystemAsync();
                var locationSystem = await _storageService.LoadLocationSystemAsync();

                if (categorySystem == null)
                {
                    categorySystem = _storageService.CreateDefaultCategorySystem();
                    await _storageService.SaveCategorySystemAsync(categorySystem);
                }

                if (locationSystem == null)
                {
                    locationSystem = _storageService.CreateDefaultLocationSystem();
                    await _storageService.SaveLocationSystemAsync(locationSystem);
                }

                var syncData = new MobileCategorySync
                {
                    LastUpdated = DateTime.UtcNow,
                    CategoryVersion = categorySystem.Version,
                    LocationVersion = locationSystem.Version,
                    Categories = await BuildMobileCategoriesAsync(categorySystem),
                    Locations = BuildMobileLocationsAsync(locationSystem)
                };

                // Cache the generated sync data
                var storage = _storageFactory.GetStorage<MobileCategorySync>("CategorySync");
                await storage.SaveAsync(MOBILE_SYNC_FILE, syncData);

                _logger.LogInformation("Mobile sync data regenerated with {CategoryCount} categories and {LocationCount} locations",
                    syncData.Categories.Count, syncData.Locations.Count);

                return syncData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating mobile sync data");
                throw;
            }
        }

        // ===== MOBILE DATA BUILDERS =====

        private async Task<List<MobileCategoryItem>> BuildMobileCategoriesAsync(CategorySystem categorySystem)
        {
            var mobileCategories = new List<MobileCategoryItem>();

            foreach (var category in categorySystem.Categories.Where(c => c.IsActive).OrderBy(c => c.SortOrder))
            {
                var mobileCategory = new MobileCategoryItem
                {
                    Id = category.Id,
                    Name = category.Name,
                    SortOrder = category.SortOrder,
                    SubCategories = new List<MobileSubCategoryItem>()
                };

                foreach (var subCategory in category.SubCategories.Where(s => s.IsActive).OrderBy(s => s.SortOrder))
                {
                    var usageStats = await _usageService.GetSubCategoryUsageStatsAsync(subCategory.Id);

                    var mobileSubCategory = new MobileSubCategoryItem
                    {
                        Id = subCategory.Id,
                        Name = subCategory.Name,
                        FullPath = $"{category.Name}/{subCategory.Name}",
                        SortOrder = subCategory.SortOrder,
                        TotalUsageCount = usageStats.TotalUsageCount,
                        LocationUsage = usageStats.LocationUsage
                    };

                    mobileCategory.SubCategories.Add(mobileSubCategory);
                }

                mobileCategories.Add(mobileCategory);
            }

            return mobileCategories;
        }

        private List<MobileLocationItem> BuildMobileLocationsAsync(LocationSystem locationSystem)
        {
            return locationSystem.Locations
                .Where(l => l.IsActive)
                .OrderBy(l => l.SortOrder)
                .Select(l => new MobileLocationItem
                {
                    Id = l.Id,
                    Name = l.Name,
                    SortOrder = l.SortOrder
                })
                .ToList();
        }

        // ===== CACHE INVALIDATION =====

        public async Task InvalidateCacheAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<MobileCategorySync>("CategorySync");

                // Delete the cached sync file to force regeneration
                // Note: You might need to add a DeleteAsync method to your storage interface
                // For now, just regenerate
                await RegenerateMobileSyncAsync();

                _logger.LogDebug("Mobile sync cache invalidated and regenerated");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating mobile sync cache");
                throw;
            }
        }

        // ===== OPTIMIZATION HELPERS =====

        public async Task<bool> IsSyncDataStaleAsync(TimeSpan maxAge)
        {
            try
            {
                var syncData = await GetMobileSyncDataAsync();
                return DateTime.UtcNow - syncData.LastUpdated > maxAge;
            }
            catch
            {
                // If we can't load sync data, consider it stale
                return true;
            }
        }

        public async Task<MobileCategorySync> GetOrRegenerateSyncDataAsync(TimeSpan maxAge)
        {
            if (await IsSyncDataStaleAsync(maxAge))
            {
                _logger.LogDebug("Sync data is stale, regenerating");
                return await RegenerateMobileSyncAsync();
            }

            return await GetMobileSyncDataAsync();
        }
    }
}