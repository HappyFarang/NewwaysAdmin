// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryService.cs
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Main category service - orchestrates category operations
    /// </summary>
    public class CategoryService
    {
        private readonly CategoryStorageService _storageService;
        private readonly MobileSyncService _syncService;
        private readonly BusinessLocationService _locationService;
        private readonly ILogger<CategoryService> _logger;

        public CategoryService(
            CategoryStorageService storageService,
            MobileSyncService syncService,
            BusinessLocationService locationService,
            ILogger<CategoryService> logger)
        {
            _storageService = storageService;
            _syncService = syncService;
            _locationService = locationService;
            _logger = logger;
        }

        // ===== CATEGORY SYSTEM OPERATIONS =====

        public async Task<CategorySystem> GetCategorySystemAsync()
        {
            try
            {
                var system = await _storageService.LoadCategorySystemAsync();

                if (system == null)
                {
                    _logger.LogInformation("No category system found, creating default");
                    system = _storageService.CreateDefaultCategorySystem();
                    await _storageService.SaveCategorySystemAsync(system);
                }

                return system;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category system");
                throw;
            }
        }

        // ===== CATEGORY CRUD OPERATIONS =====

        public async Task<Category> CreateCategoryAsync(string name, string description, string createdBy)
        {
            try
            {
                var system = await GetCategorySystemAsync();

                var category = new Category
                {
                    Name = name,
                    Description = description,
                    CreatedBy = createdBy,
                    SortOrder = system.Categories.Count
                };

                system.Categories.Add(category);
                system.LastModified = DateTime.UtcNow;
                system.Version++;
                system.ModifiedBy = createdBy;

                await _storageService.SaveCategorySystemAsync(system);
                await _syncService.InvalidateCacheAsync();

                _logger.LogInformation("Created category: {CategoryName} by {User}", name, createdBy);
                return category;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating category: {CategoryName}", name);
                throw;
            }
        }

        public async Task<SubCategory> CreateSubCategoryAsync(string categoryId, string name, string description, string createdBy)
        {
            try
            {
                var system = await GetCategorySystemAsync();
                var category = system.Categories.FirstOrDefault(c => c.Id == categoryId);

                if (category == null)
                    throw new ArgumentException($"Category {categoryId} not found");

                var subCategory = new SubCategory
                {
                    Name = name,
                    Description = description,
                    ParentCategoryName = category.Name,
                    CreatedBy = createdBy,
                    SortOrder = category.SubCategories.Count
                };

                category.SubCategories.Add(subCategory);
                category.LastModified = DateTime.UtcNow;
                system.LastModified = DateTime.UtcNow;
                system.Version++;
                system.ModifiedBy = createdBy;

                await _storageService.SaveCategorySystemAsync(system);
                await _syncService.InvalidateCacheAsync();

                _logger.LogInformation("Created subcategory: {SubCategoryName} in {CategoryName} by {User}",
                    name, category.Name, createdBy);
                return subCategory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating subcategory: {SubCategoryName}", name);
                throw;
            }
        }

        public async Task UpdateCategoryAsync(string categoryId, string name, string description, string updatedBy)
        {
            try
            {
                var system = await GetCategorySystemAsync();
                var category = system.Categories.FirstOrDefault(c => c.Id == categoryId);

                if (category == null)
                    throw new ArgumentException($"Category {categoryId} not found");

                category.Name = name;
                category.Description = description;
                category.LastModified = DateTime.UtcNow;
                system.LastModified = DateTime.UtcNow;
                system.Version++;
                system.ModifiedBy = updatedBy;

                await _storageService.SaveCategorySystemAsync(system);
                await _syncService.InvalidateCacheAsync();

                _logger.LogInformation("Updated category: {CategoryName} by {User}", name, updatedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating category: {CategoryId}", categoryId);
                throw;
            }
        }

        public async Task DeleteCategoryAsync(string categoryId, string deletedBy)
        {
            try
            {
                var system = await GetCategorySystemAsync();
                var category = system.Categories.FirstOrDefault(c => c.Id == categoryId);

                if (category == null)
                    throw new ArgumentException($"Category {categoryId} not found");

                system.Categories.Remove(category);
                system.LastModified = DateTime.UtcNow;
                system.Version++;
                system.ModifiedBy = deletedBy;

                await _storageService.SaveCategorySystemAsync(system);
                await _syncService.InvalidateCacheAsync();

                _logger.LogInformation("Deleted category: {CategoryName} by {User}", category.Name, deletedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting category: {CategoryId}", categoryId);
                throw;
            }
        }

        // ===== MOBILE SYNC OPERATIONS =====

        public async Task<CategorySystem> GetMobileSyncDataAsync()
        {
            return await _syncService.GetMobileSyncDataAsync();
        }

        public async Task<CategorySystem> RegenerateMobileSyncAsync()
        {
            return await _syncService.RegenerateMobileSyncAsync();
        }

        // ===== LOCATION OPERATIONS (DELEGATED) =====

        public async Task<List<BusinessLocation>> GetBusinessLocationsAsync()
        {
            return await _locationService.GetBusinessLocationsAsync();
        }

        public async Task<BusinessLocation> AddBusinessLocationAsync(string locationName, string description = "", string createdBy = "System")
        {
            var location = await _locationService.AddBusinessLocationAsync(locationName, description);
            await _syncService.InvalidateCacheAsync();
            return location;
        }

        public async Task<BusinessLocation> UpdateBusinessLocationAsync(string locationId, string name, string description)
        {
            var location = await _locationService.UpdateBusinessLocationAsync(locationId, name, description);
            await _syncService.InvalidateCacheAsync();
            return location;
        }

        public async Task DeleteBusinessLocationAsync(string locationId, string deletedBy = "System")
        {
            await _locationService.DeleteBusinessLocationAsync(locationId);
            await _syncService.InvalidateCacheAsync();
        }

        // ===== VALIDATION =====

        public async Task<bool> CategoryExistsAsync(string categoryName)
        {
            try
            {
                var system = await GetCategorySystemAsync();
                return system.Categories.Any(c => c.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if category exists: {CategoryName}", categoryName);
                throw;
            }
        }

        public async Task<bool> SubCategoryExistsAsync(string categoryId, string subCategoryName)
        {
            try
            {
                var system = await GetCategorySystemAsync();
                var category = system.Categories.FirstOrDefault(c => c.Id == categoryId);
                return category?.SubCategories.Any(s => s.Name.Equals(subCategoryName, StringComparison.OrdinalIgnoreCase)) ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if subcategory exists: {SubCategoryName}", subCategoryName);
                throw;
            }
        }
    }
}