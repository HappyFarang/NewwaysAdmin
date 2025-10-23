// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryUsageService.cs
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles category usage tracking and analytics
    /// Provides insights into category selection patterns
    /// </summary>
    public class CategoryUsageService
    {
        private readonly CategoryStorageService _storageService;
        private readonly ILogger<CategoryUsageService> _logger;

        public CategoryUsageService(
            CategoryStorageService storageService,
            ILogger<CategoryUsageService> logger)
        {
            _storageService = storageService;
            _logger = logger;
        }

        // ===== USAGE RECORDING =====

        public async Task RecordUsageAsync(string subCategoryId, string? locationId, string deviceId)
        {
            try
            {
                var categorySystem = await _storageService.LoadCategorySystemAsync();
                var locationSystem = await _storageService.LoadLocationSystemAsync();

                if (categorySystem == null)
                {
                    _logger.LogWarning("Cannot record usage - category system not found");
                    return;
                }

                // Find the subcategory
                var (subCategory, parentCategory) = FindSubCategory(categorySystem, subCategoryId);

                if (subCategory == null || parentCategory == null)
                {
                    _logger.LogWarning("SubCategory {SubCategoryId} not found for usage recording", subCategoryId);
                    return;
                }

                // Create usage record
                var usage = new CategoryUsage
                {
                    SubCategoryId = subCategoryId,
                    SubCategoryPath = $"{parentCategory.Name}/{subCategory.Name}",
                    LocationId = locationId,
                    LocationName = locationId != null ?
                        locationSystem?.Locations.FirstOrDefault(l => l.Id == locationId)?.Name : null,
                    UsedDate = DateTime.UtcNow,
                    UsedBy = deviceId
                };

                // Save usage record
                await _storageService.SaveCategoryUsageAsync(usage);

                _logger.LogDebug("Recorded usage for {SubCategoryPath} at location {LocationName} by {DeviceId}",
                    usage.SubCategoryPath, usage.LocationName ?? "No location", deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording category usage for {SubCategoryId}", subCategoryId);
                throw;
            }
        }

        // ===== USAGE ANALYTICS =====

        public async Task<SubCategoryUsageStats> GetSubCategoryUsageStatsAsync(string subCategoryId)
        {
            try
            {
                // For now, return empty stats
                // TODO: Implement when you add usage querying to your storage system
                return new SubCategoryUsageStats
                {
                    SubCategoryId = subCategoryId,
                    TotalUsageCount = 0,
                    LocationUsage = new List<LocationUsageCount>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting usage stats for subcategory {SubCategoryId}", subCategoryId);
                throw;
            }
        }

        public async Task<List<CategoryUsageStats>> GetTopUsedCategoriesAsync(int count = 10, int daysBack = 30)
        {
            try
            {
                // TODO: Implement when you have usage querying
                return new List<CategoryUsageStats>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top used categories");
                throw;
            }
        }

        public async Task<List<CategoryUsage>> GetRecentUsageAsync(int daysBack = 7)
        {
            try
            {
                return await _storageService.LoadRecentUsageAsync(daysBack);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent usage");
                throw;
            }
        }

        // ===== HELPER METHODS =====

        private (SubCategory?, Category?) FindSubCategory(CategorySystem categorySystem, string subCategoryId)
        {
            foreach (var category in categorySystem.Categories)
            {
                var subCategory = category.SubCategories.FirstOrDefault(sc => sc.Id == subCategoryId);
                if (subCategory != null)
                {
                    return (subCategory, category);
                }
            }
            return (null, null);
        }
    }

    // ===== STATS DATA MODELS =====

    public class SubCategoryUsageStats
    {
        public string SubCategoryId { get; set; } = string.Empty;
        public int TotalUsageCount { get; set; }
        public List<LocationUsageCount> LocationUsage { get; set; } = new();
    }

    public class CategoryUsageStats
    {
        public string CategoryId { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int TotalUsageCount { get; set; }
        public DateTime LastUsed { get; set; }
        public List<SubCategoryUsageStats> SubCategoryStats { get; set; } = new();
    }
}