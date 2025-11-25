// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryStorageService.cs
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Shared.IO.Structure;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles all storage operations for the category system
    /// Single file contains categories, locations, and persons
    /// </summary>
    public class CategoryStorageService
    {
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly ILogger<CategoryStorageService> _logger;

        private const string CATEGORY_SYSTEM_FILE = "category_system.json";

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

        // ===== DEFAULT SYSTEM CREATION =====

        public CategorySystem CreateDefaultCategorySystem()
        {
            return new CategorySystem
            {
                Categories = new List<Category>(),
                Locations = new List<BusinessLocation>(),
                Persons = new List<Person>(),
                Version = 1,
                LastModified = DateTime.UtcNow,
                ModifiedBy = "System"
            };
        }
    }
}