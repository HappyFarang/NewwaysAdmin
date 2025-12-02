// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/CategoryDataService.cs
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Category data service - handles local cache and CRUD operations
    /// SignalR communication is handled by CategoryHubConnector
    /// </summary>
    public class CategoryDataService
    {
        private readonly ILogger<CategoryDataService> _logger;
        private readonly SyncState _syncState;
        private readonly ConnectionState _connectionState;

        private readonly string _cacheFilePath;

        // In-memory cache for fast access
        private FullCategoryData? _cachedData;

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
        }

        // ===== PUBLIC PROPERTIES =====

        public int LocalVersion => _syncState.LocalVersion;
        public bool NeedsDownload => _syncState.NeedsDownload;
        public bool HasCachedData => _cachedData != null || File.Exists(_cacheFilePath);
        public DateTime? LastSyncTime => _syncState.LastSyncTime;

        // ===== PUBLIC METHODS - READ =====

        /// <summary>
        /// Get category data from cache
        /// </summary>
        public async Task<FullCategoryData?> GetDataAsync()
        {
            try
            {
                if (_cachedData == null)
                {
                    _cachedData = await LoadFromCacheAsync();
                }
                return _cachedData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category data");
                return _cachedData;
            }
        }

        /// <summary>
        /// Save data to cache - called by CategoryHubConnector when data is received
        /// </summary>
        public async Task SaveDataAsync(FullCategoryData data)
        {
            try
            {
                await SaveToCacheAsync(data);
                _cachedData = data;
                _logger.LogInformation(
                    "Saved data v{Version}: {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    data.DataVersion,
                    data.Categories.Count,
                    data.Locations.Count,
                    data.Persons.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data");
                throw;
            }
        }

        /// <summary>
        /// Called by CategoryHubConnector when new data is received from server
        /// </summary>
        public void NotifyDataChanged(FullCategoryData data)
        {
            _cachedData = data;
            _logger.LogInformation("Data updated externally - v{Version}", data.DataVersion);
            DataUpdated?.Invoke(this, data);
        }

        /// <summary>
        /// Notify listeners of sync error
        /// </summary>
        public void NotifySyncError(string message)
        {
            SyncError?.Invoke(this, message);
        }

        // ===== PUBLIC METHODS - CREATE =====

        /// <summary>
        /// Create a new person locally, bump version
        /// </summary>
        public async Task CreatePersonAsync(string name)
        {
            var data = await GetDataAsync() ?? CreateEmptyData();

            var newPerson = new ResponsiblePerson  // NOT BusinessPerson
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "MAUI"
            };

            data.Persons.Add(newPerson);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;  // NOT LastModified

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Created person: {Name} (v{Version})", name, data.DataVersion);
        }

        /// <summary>
        /// Create a new location locally
        /// </summary>
        public async Task CreateLocationAsync(string name)
        {
            var data = await GetDataAsync() ?? CreateEmptyData();

            var newLocation = new BusinessLocation
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                CreatedDate = DateTime.UtcNow
            };

            data.Locations.Add(newLocation);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Created location: {Name} (v{Version})", name, data.DataVersion);
        }

        /// <summary>
        /// Create a new category locally
        /// </summary>
        public async Task CreateCategoryAsync(string name)
        {
            var data = await GetDataAsync() ?? CreateEmptyData();

            var newCategory = new Category
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                SubCategories = new List<SubCategory>(),
                SortOrder = data.Categories.Count,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "MAUI"
            };

            data.Categories.Add(newCategory);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Created category: {Name} (v{Version})", name, data.DataVersion);
        }

        /// <summary>
        /// Create a new subcategory under a parent category
        /// </summary>
        public async Task CreateSubCategoryAsync(string parentCategoryId, string name)
        {
            var data = await GetDataAsync();
            if (data == null) throw new InvalidOperationException("No data loaded");

            var parentCategory = data.Categories.FirstOrDefault(c => c.Id == parentCategoryId);
            if (parentCategory == null) throw new ArgumentException($"Category not found: {parentCategoryId}");

            parentCategory.SubCategories ??= new List<SubCategory>();

            var newSubCategory = new SubCategory
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                ParentCategoryName = parentCategory.Name,  // NOT ParentCategoryId
                SortOrder = parentCategory.SubCategories.Count,
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "MAUI"
            };

            parentCategory.SubCategories.Add(newSubCategory);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;  // NOT LastModified

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Created subcategory: {Name} under {Parent} (v{Version})", name, parentCategory.Name, data.DataVersion);
        }

        // ===== PUBLIC METHODS - DELETE =====

        /// <summary>
        /// Delete a person
        /// </summary>
        public async Task DeletePersonAsync(string personId)
        {
            var data = await GetDataAsync();
            if (data == null) throw new InvalidOperationException("No data loaded");

            var person = data.Persons.FirstOrDefault(p => p.Id == personId);
            if (person == null) return;

            data.Persons.Remove(person);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Deleted person: {Name} (v{Version})", person.Name, data.DataVersion);
        }

        /// <summary>
        /// Delete a location
        /// </summary>
        public async Task DeleteLocationAsync(string locationId)
        {
            var data = await GetDataAsync();
            if (data == null) throw new InvalidOperationException("No data loaded");

            var location = data.Locations.FirstOrDefault(l => l.Id == locationId);
            if (location == null) return;

            data.Locations.Remove(location);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Deleted location: {Name} (v{Version})", location.Name, data.DataVersion);
        }

        /// <summary>
        /// Delete a category and all its subcategories
        /// </summary>
        public async Task DeleteCategoryAsync(string categoryId)
        {
            var data = await GetDataAsync();
            if (data == null) throw new InvalidOperationException("No data loaded");

            var category = data.Categories.FirstOrDefault(c => c.Id == categoryId);
            if (category == null) return;

            data.Categories.Remove(category);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Deleted category: {Name} (v{Version})", category.Name, data.DataVersion);
        }

        /// <summary>
        /// Delete a subcategory
        /// </summary>
        public async Task DeleteSubCategoryAsync(string parentCategoryId, string subCategoryId)
        {
            var data = await GetDataAsync();
            if (data == null) throw new InvalidOperationException("No data loaded");

            var parentCategory = data.Categories.FirstOrDefault(c => c.Id == parentCategoryId);
            if (parentCategory?.SubCategories == null) return;

            var subCategory = parentCategory.SubCategories.FirstOrDefault(s => s.Id == subCategoryId);
            if (subCategory == null) return;

            parentCategory.SubCategories.Remove(subCategory);
            data.DataVersion++;
            data.LastUpdated = DateTime.UtcNow;

            await SaveAndNotifyAsync(data);
            _logger.LogInformation("Deleted subcategory: {Name} (v{Version})", subCategory.Name, data.DataVersion);
        }

        // ===== PRIVATE METHODS =====

        private FullCategoryData CreateEmptyData()
        {
            return new FullCategoryData
            {
                DataVersion = 1,
                LastUpdated = DateTime.UtcNow, 
                LastModifiedBy = "MAUI",
                Categories = new List<Category>(),
                Locations = new List<BusinessLocation>(),
                Persons = new List<ResponsiblePerson>() 
            };
        }

        private async Task SaveAndNotifyAsync(FullCategoryData data)
        {
            // Save to cache
            await SaveToCacheAsync(data);

            // Update cached data
            _cachedData = data;

            // Update sync state
            _syncState.MarkDownloadComplete(data.DataVersion);

            // Notify listeners
            DataUpdated?.Invoke(this, data);
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
                throw;
            }
        }
    }
}