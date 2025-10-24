// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryStorageService.cs
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Shared.IO.Structure;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles all storage operations for categories and locations
    /// Focused on data persistence without business logic
    /// </summary>
    public class CategoryStorageService
    {
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly ILogger<CategoryStorageService> _logger;

        private const string CATEGORY_SYSTEM_FILE = "category_system.json";
        private const string LOCATION_SYSTEM_FILE = "location_system.json";

        public CategoryStorageService(
            EnhancedStorageFactory storageFactory,
            ILogger<CategoryStorageService> logger)
        {
            _storageFactory = storageFactory;
            _logger = logger;
        }

        // ===== CATEGORY SYSTEM STORAGE =====

        public async Task<CategorySystem?> LoadCategorySystemAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<CategorySystem>("Categories");
                return await storage.LoadAsync(CATEGORY_SYSTEM_FILE);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading category system from storage");
                throw;
            }
        }

        public async Task SaveCategorySystemAsync(CategorySystem system)
        {
            try
            {
                var storage = _storageFactory.GetStorage<CategorySystem>("Categories");
                await storage.SaveAsync(CATEGORY_SYSTEM_FILE, system);

                _logger.LogDebug("Category system saved with version {Version}", system.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving category system to storage");
                throw;
            }
        }

        // ===== LOCATION SYSTEM STORAGE =====

        public async Task<LocationSystem?> LoadLocationSystemAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<LocationSystem>("BusinessLocations");
                return await storage.LoadAsync(LOCATION_SYSTEM_FILE);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading location system from storage");
                throw;
            }
        }

        public async Task SaveLocationSystemAsync(LocationSystem system)
        {
            try
            {
                var storage = _storageFactory.GetStorage<LocationSystem>("BusinessLocations");
                await storage.SaveAsync(LOCATION_SYSTEM_FILE, system);

                _logger.LogDebug("Location system saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving location system to storage");
                throw;
            }
        }

        // ===== USAGE TRACKING STORAGE =====

        public async Task SaveCategoryUsageAsync(CategoryUsage usage)
        {
            try
            {
                var usageStorage = _storageFactory.GetStorage<CategoryUsage>("CategoryUsage");
                var fileName = $"usage_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..8]}.json";

                await usageStorage.SaveAsync(fileName, usage);

                _logger.LogDebug("Category usage saved: {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving category usage to storage");
                throw;
            }
        }

        public async Task<List<CategoryUsage>> LoadRecentUsageAsync(int daysBack = 30)
        {
            try
            {
                var usageStorage = _storageFactory.GetStorage<CategoryUsage>("CategoryUsage");
                var allIdentifiers = await usageStorage.ListIdentifiersAsync();
                var usageList = new List<CategoryUsage>();

                foreach (var identifier in allIdentifiers)
                {
                    try
                    {
                        var usage = await usageStorage.LoadAsync(identifier);
                        if (usage != null)
                        {
                            usageList.Add(usage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not load usage file: {Identifier}", identifier);
                    }
                }

                return usageList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading recent category usage");
                throw;
            }
        }

        // ===== DEFAULT SYSTEM CREATION =====

        public CategorySystem CreateDefaultCategorySystem()
        {
            return new CategorySystem
            {
                Categories = new List<Category>(), // Empty - create your own categories
                Version = 1,
                LastModified = DateTime.UtcNow,
                ModifiedBy = "System"
            };
        }

        public LocationSystem CreateDefaultLocationSystem()
        {
            return new LocationSystem
            {
                Locations = new List<BusinessLocation>
                {
                    new BusinessLocation
                    {
                        Name = "No Location",
                        Description = "For transactions that don't require a specific location",
                        IsActive = true,
                        SortOrder = 0,
                        CreatedBy = "System"
                    }
                },
                Version = 1,
                LastModified = DateTime.UtcNow,
                ModifiedBy = "System"
            };
        }
    }
}