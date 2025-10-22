// File: NewwaysAdmin.WebAdmin/Services/Categories/CategoryService.cs
using Microsoft.AspNetCore.SignalR;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WebAdmin.Hubs;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    public class CategoryService
    {
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly ILogger<CategoryService> _logger;
        private readonly IHubContext<MobileCommHub> _hubContext;

        private const string CATEGORY_SYSTEM_FILE = "category_system.json";
        private const string LOCATION_SYSTEM_FILE = "location_system.json";
        private const string MOBILE_SYNC_FILE = "mobile_categories.json";

        public CategoryService(
            EnhancedStorageFactory storageFactory,
            ILogger<CategoryService> logger,
            IHubContext<MobileCommHub> hubContext)
        {
            _storageFactory = storageFactory;
            _logger = logger;
            _hubContext = hubContext;
        }

        // ===== CATEGORY CRUD OPERATIONS =====

        public async Task<CategorySystem> GetCategorySystemAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<CategorySystem>("Categories");
                var system = await storage.LoadAsync(CATEGORY_SYSTEM_FILE);

                if (system == null)
                {
                    _logger.LogInformation("No category system found, creating default");
                    return await CreateDefaultCategorySystemAsync();
                }

                return system;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading category system");
                throw;
            }
        }

        public async Task<Category> CreateCategoryAsync(string name, string description, string createdBy)
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

            await SaveCategorySystemAsync(system);
            await RegenerateMobileSyncAsync();
            await NotifyClientsAsync("CategoryAdded", category.Id, category.Name, createdBy);

            _logger.LogInformation("Created category: {CategoryName} by {User}", name, createdBy);
            return category;
        }

        public async Task<SubCategory> CreateSubCategoryAsync(string categoryId, string name, string description, string createdBy)
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

            await SaveCategorySystemAsync(system);
            await RegenerateMobileSyncAsync();
            await NotifyClientsAsync("SubCategoryAdded", subCategory.Id, subCategory.FullPath, createdBy);

            _logger.LogInformation("Created subcategory: {SubCategoryPath} by {User}", subCategory.FullPath, createdBy);
            return subCategory;
        }

        public async Task UpdateCategoryAsync(string categoryId, string name, string description, string updatedBy)
        {
            var system = await GetCategorySystemAsync();
            var category = system.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            category.Name = name;
            category.Description = description;
            category.LastModified = DateTime.UtcNow;

            // Update parent category name in all subcategories
            foreach (var sub in category.SubCategories)
            {
                sub.ParentCategoryName = name;
            }

            system.LastModified = DateTime.UtcNow;
            system.Version++;
            system.ModifiedBy = updatedBy;

            await SaveCategorySystemAsync(system);
            await RegenerateMobileSyncAsync();
            await NotifyClientsAsync("CategoryUpdated", categoryId, name, updatedBy);

            _logger.LogInformation("Updated category: {CategoryName} by {User}", name, updatedBy);
        }

        public async Task DeleteCategoryAsync(string categoryId, string deletedBy)
        {
            var system = await GetCategorySystemAsync();
            var category = system.Categories.FirstOrDefault(c => c.Id == categoryId);

            if (category == null)
                throw new ArgumentException($"Category {categoryId} not found");

            system.Categories.Remove(category);
            system.LastModified = DateTime.UtcNow;
            system.Version++;
            system.ModifiedBy = deletedBy;

            await SaveCategorySystemAsync(system);
            await RegenerateMobileSyncAsync();
            await NotifyClientsAsync("CategoryDeleted", categoryId, category.Name, deletedBy);

            _logger.LogInformation("Deleted category: {CategoryName} by {User}", category.Name, deletedBy);
        }

        // ===== BUSINESS LOCATION MANAGEMENT =====

        public async Task<List<BusinessLocation>> GetBusinessLocationsAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<LocationSystem>("BusinessLocations");
                var system = await storage.LoadAsync(LOCATION_SYSTEM_FILE);

                if (system == null)
                {
                    return await CreateDefaultLocationSystemAsync();
                }

                return system.Locations.Where(l => l.IsActive).OrderBy(l => l.SortOrder).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading business locations");
                throw;
            }
        }

        public async Task<BusinessLocation> AddBusinessLocationAsync(string locationName, string createdBy)
        {
            var storage = _storageFactory.GetStorage<LocationSystem>("BusinessLocations");
            var system = await storage.LoadAsync(LOCATION_SYSTEM_FILE) ?? new LocationSystem();

            var location = new BusinessLocation
            {
                Name = locationName,
                CreatedBy = createdBy,
                SortOrder = system.Locations.Count
            };

            system.Locations.Add(location);
            system.LastModified = DateTime.UtcNow;
            system.Version++;
            system.ModifiedBy = createdBy;

            await storage.SaveAsync(LOCATION_SYSTEM_FILE, system);
            await RegenerateMobileSyncAsync();
            await NotifyLocationUpdateAsync(system.Locations, createdBy);

            _logger.LogInformation("Added location: {LocationName} by {User}", locationName, createdBy);
            return location;
        }

        public async Task DeleteBusinessLocationAsync(string locationId, string deletedBy)
        {
            var storage = _storageFactory.GetStorage<LocationSystem>("BusinessLocations");
            var system = await storage.LoadAsync(LOCATION_SYSTEM_FILE);

            if (system == null)
                throw new ArgumentException("Location system not found");

            var location = system.Locations.FirstOrDefault(l => l.Id == locationId);
            if (location == null)
                throw new ArgumentException($"Location {locationId} not found");

            location.IsActive = false; // Soft delete to preserve usage history
            system.LastModified = DateTime.UtcNow;
            system.Version++;
            system.ModifiedBy = deletedBy;

            await storage.SaveAsync(LOCATION_SYSTEM_FILE, system);
            await RegenerateMobileSyncAsync();
            await NotifyLocationUpdateAsync(system.Locations.Where(l => l.IsActive).ToList(), deletedBy);

            _logger.LogInformation("Deleted location: {LocationName} by {User}", location.Name, deletedBy);
        }

        // ===== USAGE TRACKING =====

        public async Task RecordUsageAsync(string subCategoryId, string? locationId, string usedBy, string? transactionNote = null, decimal? amount = null)
        {
            try
            {
                var storage = _storageFactory.GetStorage<List<CategoryUsage>>("CategoryUsage");
                var filename = $"usage_{DateTime.UtcNow:yyyy_MM}.json"; // Monthly files

                var usageList = await storage.LoadAsync(filename);
                if (usageList == null)
                {
                    usageList = new List<CategoryUsage>();
                }

                // Get subcategory details for better tracking
                var categorySystem = await GetCategorySystemAsync();
                var subCategory = categorySystem.Categories
                    .SelectMany(c => c.SubCategories)
                    .FirstOrDefault(s => s.Id == subCategoryId);

                string? locationName = null;
                if (!string.IsNullOrEmpty(locationId))
                {
                    var locations = await GetBusinessLocationsAsync();
                    locationName = locations.FirstOrDefault(l => l.Id == locationId)?.Name;
                }

                var usage = new CategoryUsage
                {
                    SubCategoryId = subCategoryId,
                    SubCategoryPath = subCategory?.FullPath ?? "Unknown",
                    LocationId = locationId,
                    LocationName = locationName,
                    UsedBy = usedBy,
                    TransactionNote = transactionNote,
                    Amount = amount
                };

                usageList.Add(usage);
                await storage.SaveAsync(filename, usageList);

                await RegenerateMobileSyncAsync();

                _logger.LogDebug("Recorded usage for subcategory: {SubCategoryPath} at location: {LocationName}",
                    usage.SubCategoryPath, locationName ?? "No location");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording usage for subcategory {SubCategoryId}", subCategoryId);
                throw;
            }
        }

        // ===== MOBILE SYNC =====

        public async Task<MobileCategorySync> GetMobileSyncDataAsync()
        {
            try
            {
                var syncStorage = _storageFactory.GetStorage<MobileCategorySync>("CategorySync");
                var syncData = await syncStorage.LoadAsync(MOBILE_SYNC_FILE);

                if (syncData == null)
                {
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

        // ===== PRIVATE METHODS =====

        private async Task<CategorySystem> CreateDefaultCategorySystemAsync()
        {
            var system = new CategorySystem
            {
                ModifiedBy = "System"
            };

            // Add some default categories
            var transportation = new Category
            {
                Name = "Transportation",
                Description = "Travel and transportation expenses",
                CreatedBy = "System",
                SortOrder = 0
            };

            transportation.SubCategories.Add(new SubCategory
            {
                Name = "Green Buses",
                Description = "Local green bus transportation",
                ParentCategoryName = "Transportation",
                CreatedBy = "System",
                SortOrder = 0
            });

            var tax = new Category
            {
                Name = "Tax & VAT",
                Description = "Tax payments and VAT expenses",
                CreatedBy = "System",
                SortOrder = 1
            };

            tax.SubCategories.Add(new SubCategory
            {
                Name = "VAT Payment",
                Description = "Value Added Tax payments",
                ParentCategoryName = "Tax & VAT",
                CreatedBy = "System",
                SortOrder = 0
            });

            system.Categories.Add(transportation);
            system.Categories.Add(tax);

            await SaveCategorySystemAsync(system);
            return system;
        }

        private async Task<List<BusinessLocation>> CreateDefaultLocationSystemAsync()
        {
            var system = new LocationSystem
            {
                ModifiedBy = "System"
            };

            system.Locations.Add(new BusinessLocation
            {
                Name = "Phrae",
                Description = "Phrae office/operations",
                CreatedBy = "System",
                SortOrder = 0
            });

            system.Locations.Add(new BusinessLocation
            {
                Name = "Chiang Mai",
                Description = "Chiang Mai office/operations",
                CreatedBy = "System",
                SortOrder = 1
            });

            var storage = _storageFactory.GetStorage<LocationSystem>("BusinessLocations");
            await storage.SaveAsync(LOCATION_SYSTEM_FILE, system);

            return system.Locations;
        }

        private async Task SaveCategorySystemAsync(CategorySystem system)
        {
            var storage = _storageFactory.GetStorage<CategorySystem>("Categories");
            await storage.SaveAsync(CATEGORY_SYSTEM_FILE, system);
        }

        private async Task<MobileCategorySync> RegenerateMobileSyncAsync()
        {
            var categorySystem = await GetCategorySystemAsync();
            var locations = await GetBusinessLocationsAsync();

            var syncData = new MobileCategorySync
            {
                LastUpdated = DateTime.UtcNow,
                CategoryVersion = categorySystem.Version,
                LocationVersion = 1, // TODO: Get from LocationSystem
                Categories = categorySystem.Categories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new MobileCategoryItem
                    {
                        Id = c.Id,
                        Name = c.Name,
                        SortOrder = c.SortOrder,
                        SubCategories = c.SubCategories
                            .Where(s => s.IsActive)
                            .OrderBy(s => s.SortOrder)
                            .Select(s => new MobileSubCategoryItem
                            {
                                Id = s.Id,
                                Name = s.Name,
                                FullPath = s.FullPath,
                                SortOrder = s.SortOrder,
                                TotalUsageCount = 0, // TODO: Calculate from usage data
                                LocationUsage = new List<LocationUsageCount>()
                            }).ToList()
                    }).ToList(),
                Locations = locations.Select(l => new MobileLocationItem
                {
                    Id = l.Id,
                    Name = l.Name,
                    SortOrder = l.SortOrder
                }).ToList()
            };

            var syncStorage = _storageFactory.GetStorage<MobileCategorySync>("CategorySync");
            await syncStorage.SaveAsync(MOBILE_SYNC_FILE, syncData);

            return syncData;
        }

        private async Task NotifyClientsAsync(string messageType, string categoryId, string categoryName, string updatedBy)
        {
            var message = new CategoryUpdateMessage
            {
                MessageType = messageType,
                CategoryId = categoryId,
                CategoryName = categoryName,
                UpdatedBy = updatedBy
            };

            await _hubContext.Clients.All.SendAsync("CategoryUpdate", message);
        }

        private async Task NotifyLocationUpdateAsync(List<BusinessLocation> locations, string updatedBy)
        {
            var message = new LocationUpdateMessage
            {
                Locations = locations,
                UpdatedBy = updatedBy
            };

            await _hubContext.Clients.All.SendAsync("LocationUpdate", message);
        }
    }
}