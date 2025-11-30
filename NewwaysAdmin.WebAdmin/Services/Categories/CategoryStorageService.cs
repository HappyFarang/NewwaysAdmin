// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryStorageService.cs
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Shared.IO.Structure;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Simple storage service for category data
    /// One file, one data structure, one version
    /// </summary>
    public class CategoryStorageService
    {
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly ILogger<CategoryStorageService> _logger;

        private const string FOLDER_NAME = "Categories";
        private const string DATA_FILE = "category_data.json";

        public CategoryStorageService(
            EnhancedStorageFactory storageFactory,
            ILogger<CategoryStorageService> logger)
        {
            _storageFactory = storageFactory;
            _logger = logger;
        }

        // ===== LOAD =====

        public async Task<FullCategoryData> LoadAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<FullCategoryData>(FOLDER_NAME);
                var data = await storage.LoadAsync(DATA_FILE);

                if (data == null)
                {
                    _logger.LogInformation("No category data found, creating default");
                    data = CreateDefault();
                    await SaveAsync(data);
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading category data");
                throw;
            }
        }

        // ===== SAVE =====

        public async Task SaveAsync(FullCategoryData data)
        {
            try
            {
                data.LastUpdated = DateTime.UtcNow;

                var storage = _storageFactory.GetStorage<FullCategoryData>(FOLDER_NAME);
                await storage.SaveAsync(DATA_FILE, data);

                _logger.LogDebug("Category data saved - Version: {Version}", data.DataVersion);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving category data");
                throw;
            }
        }

        // ===== DEFAULT =====

        public FullCategoryData CreateDefault()
        {
            return new FullCategoryData
            {
                DataVersion = 1,
                LastUpdated = DateTime.UtcNow,
                LastModifiedBy = "System",
                Categories = new List<Category>(),
                Locations = new List<BusinessLocation>(),
                Persons = new List<ResponsiblePerson>()
            };
        }
    }
}