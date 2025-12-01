// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryService.cs
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using NewwaysAdmin.SignalR.Universal.Hubs;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Main category service - all category, location, and person operations
    /// Single DataVersion increments on ANY change
    /// Notifies connected MAUI clients via SignalR on changes
    /// </summary>
    public class CategoryService
    {
        private readonly CategoryStorageService _storage;
        private readonly ILogger<CategoryService> _logger;
        private readonly IHubContext<UniversalCommHub> _hubContext;

        public CategoryService(
            CategoryStorageService storage,
            ILogger<CategoryService> logger,
            IHubContext<UniversalCommHub> hubContext)
        {
            _storage = storage;
            _logger = logger;
            _hubContext = hubContext;
        }

        // ===== FULL DATA ACCESS =====

        public async Task<FullCategoryData> GetFullDataAsync()
        {
            return await _storage.LoadAsync();
        }

        public async Task<int> GetCurrentVersionAsync()
        {
            var data = await _storage.LoadAsync();
            return data.DataVersion;
        }

        // ===== CATEGORY OPERATIONS =====

        public async Task<Category> CreateCategoryAsync(string name, string? description, string createdBy)
        {
            var data = await _storage.LoadAsync();

            var category = new Category
            {
                Name = name,
                Description = description,
                CreatedBy = createdBy,
                SortOrder = data.Categories.Count
            };

            data.Categories.Add(category);
            IncrementVersion(data, createdBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Created category: {CategoryName} (v{Version})", name, data.DataVersion);
            return category;
        }

        public async Task UpdateCategoryAsync(string categoryId, string name, string? description, string modifiedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var category = data.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            category.Name = name;
            category.Description = description;
            category.LastModified = DateTime.UtcNow;

            // Update parent name in all subcategories
            foreach (var sub in category.SubCategories)
            {
                sub.ParentCategoryName = name;
            }

            IncrementVersion(data, modifiedBy);
            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Updated category: {CategoryName} (v{Version})", name, data.DataVersion);
        }

        public async Task DeleteCategoryAsync(string categoryId, string deletedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var category = data.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            data.Categories.Remove(category);
            IncrementVersion(data, deletedBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Deleted category: {CategoryName} (v{Version})", category.Name, data.DataVersion);
        }

        // ===== SUBCATEGORY OPERATIONS =====

        public async Task<SubCategory> CreateSubCategoryAsync(
            string categoryId,
            string name,
            string? description,
            bool hasVAT,
            string createdBy)
        {
            var data = await _storage.LoadAsync();
            var category = data.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            var subCategory = new SubCategory
            {
                Name = name,
                Description = description,
                ParentCategoryName = category.Name,
                HasVAT = hasVAT,
                CreatedBy = createdBy,
                SortOrder = category.SubCategories.Count
            };

            category.SubCategories.Add(subCategory);
            category.LastModified = DateTime.UtcNow;
            IncrementVersion(data, createdBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Created subcategory: {SubCategoryName} in {CategoryName} (v{Version})",
                name, category.Name, data.DataVersion);
            return subCategory;
        }

        public async Task UpdateSubCategoryAsync(
            string categoryId,
            string subCategoryId,
            string name,
            string? description,
            bool hasVAT,
            string modifiedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var category = data.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            var subCategory = category.SubCategories.FirstOrDefault(s => s.Id == subCategoryId);

            if (subCategory == null)
                throw new ArgumentException($"SubCategory {subCategoryId} not found");

            subCategory.Name = name;
            subCategory.Description = description;
            subCategory.HasVAT = hasVAT;
            subCategory.LastModified = DateTime.UtcNow;

            category.LastModified = DateTime.UtcNow;
            IncrementVersion(data, modifiedBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Updated subcategory: {SubCategoryName} (v{Version})", name, data.DataVersion);
        }

        public async Task DeleteSubCategoryAsync(string categoryId, string subCategoryId, string deletedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var category = data.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            var subCategory = category.SubCategories.FirstOrDefault(s => s.Id == subCategoryId);

            if (subCategory == null)
                throw new ArgumentException($"SubCategory {subCategoryId} not found");

            category.SubCategories.Remove(subCategory);
            category.LastModified = DateTime.UtcNow;
            IncrementVersion(data, deletedBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Deleted subcategory: {SubCategoryName} (v{Version})", subCategory.Name, data.DataVersion);
        }

        // ===== LOCATION OPERATIONS =====

        public async Task<BusinessLocation> AddLocationAsync(string name, string createdBy)
        {
            var data = await _storage.LoadAsync();

            var location = new BusinessLocation
            {
                Name = name,
                CreatedBy = createdBy,
                SortOrder = data.Locations.Count
            };

            data.Locations.Add(location);
            IncrementVersion(data, createdBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Added location: {LocationName} (v{Version})", name, data.DataVersion);
            return location;
        }

        public async Task UpdateLocationAsync(string locationId, string name, string? description, string modifiedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var location = data.Locations.FirstOrDefault(l => l.Id == locationId);

            if (location == null)
                throw new ArgumentException($"Location {locationId} not found");

            location.Name = name;
            location.Description = description;

            IncrementVersion(data, modifiedBy);
            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Updated location: {LocationName} (v{Version})", name, data.DataVersion);
        }

        public async Task DeleteLocationAsync(string locationId, string deletedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var location = data.Locations.FirstOrDefault(l => l.Id == locationId);

            if (location == null)
                throw new ArgumentException($"Location {locationId} not found");

            data.Locations.Remove(location);
            IncrementVersion(data, deletedBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Deleted location: {LocationName} (v{Version})", location.Name, data.DataVersion);
        }

        // ===== PERSON OPERATIONS =====

        public async Task<ResponsiblePerson> AddPersonAsync(string name, string createdBy)
        {
            var data = await _storage.LoadAsync();

            var person = new ResponsiblePerson
            {
                Name = name,
                CreatedBy = createdBy,
                SortOrder = data.Persons.Count
            };

            data.Persons.Add(person);
            IncrementVersion(data, createdBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Added person: {PersonName} (v{Version})", name, data.DataVersion);
            return person;
        }

        public async Task UpdatePersonAsync(string personId, string name, string? description, string modifiedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var person = data.Persons.FirstOrDefault(p => p.Id == personId);

            if (person == null)
                throw new ArgumentException($"Person {personId} not found");

            person.Name = name;
            person.Description = description;

            IncrementVersion(data, modifiedBy);
            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Updated person: {PersonName} (v{Version})", name, data.DataVersion);
        }

        public async Task DeletePersonAsync(string personId, string deletedBy = "Admin")
        {
            var data = await _storage.LoadAsync();
            var person = data.Persons.FirstOrDefault(p => p.Id == personId);

            if (person == null)
                throw new ArgumentException($"Person {personId} not found");

            data.Persons.Remove(person);
            IncrementVersion(data, deletedBy);

            await SaveAndNotifyAsync(data);

            _logger.LogInformation("Deleted person: {PersonName} (v{Version})", person.Name, data.DataVersion);
        }

        // ===== PRIVATE HELPERS =====

        private void IncrementVersion(FullCategoryData data, string modifiedBy)
        {
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;
            data.LastModifiedBy = modifiedBy;
        }

        private async Task SaveAndNotifyAsync(FullCategoryData data)
        {
            await _storage.SaveAsync(data);
            await NotifyClientsAsync(data.DataVersion);
        }

        private async Task NotifyClientsAsync(int newVersion)
        {
            try
            {
                // Notify all connected MAUI apps
                await _hubContext.Clients.Group("App_MAUI_ExpenseTracker")
                    .SendAsync("NewVersionAvailable", newVersion);

                _logger.LogInformation("Pushed version update v{Version} to MAUI clients", newVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to notify MAUI clients of version update");
            }
        }
    }
}