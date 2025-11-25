// File: NewwaysAdmin.WebAdmin/Services/Categories/BusinessLocationService.cs
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles business location management
    /// Locations are now stored inside CategorySystem
    /// </summary>
    public class BusinessLocationService
    {
        private readonly CategoryStorageService _storageService;
        private readonly ILogger<BusinessLocationService> _logger;

        public BusinessLocationService(
            CategoryStorageService storageService,
            ILogger<BusinessLocationService> logger)
        {
            _storageService = storageService;
            _logger = logger;
        }

        // ===== LOCATION CRUD OPERATIONS =====

        public async Task<List<BusinessLocation>> GetBusinessLocationsAsync()
        {
            try
            {
                var system = await _storageService.LoadCategorySystemAsync();

                if (system == null)
                {
                    system = _storageService.CreateDefaultCategorySystem();
                    await _storageService.SaveCategorySystemAsync(system);
                }

                return system.Locations
                    .Where(l => l.IsActive)
                    .OrderBy(l => l.SortOrder)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading business locations");
                throw;
            }
        }

        public async Task<BusinessLocation> AddBusinessLocationAsync(string locationName, string description = "")
        {
            try
            {
                var system = await GetOrCreateSystemAsync();

                var newLocation = new BusinessLocation
                {
                    Name = locationName,
                    Description = description,
                    IsActive = true,
                    SortOrder = system.Locations.Count
                };

                system.Locations.Add(newLocation);
                system.Version++;
                system.LastModified = DateTime.UtcNow;

                await _storageService.SaveCategorySystemAsync(system);

                _logger.LogInformation("Added business location: {LocationName}", locationName);
                return newLocation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding business location: {LocationName}", locationName);
                throw;
            }
        }

        public async Task<BusinessLocation> UpdateBusinessLocationAsync(string locationId, string name, string description)
        {
            try
            {
                var system = await GetOrCreateSystemAsync();
                var location = system.Locations.FirstOrDefault(l => l.Id == locationId);

                if (location == null)
                {
                    throw new ArgumentException($"Location {locationId} not found");
                }

                location.Name = name;
                location.Description = description;
                system.Version++;
                system.LastModified = DateTime.UtcNow;

                await _storageService.SaveCategorySystemAsync(system);

                _logger.LogInformation("Updated business location: {LocationName}", name);
                return location;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating business location: {LocationId}", locationId);
                throw;
            }
        }

        public async Task DeleteBusinessLocationAsync(string locationId)
        {
            try
            {
                var system = await GetOrCreateSystemAsync();
                var location = system.Locations.FirstOrDefault(l => l.Id == locationId);

                if (location == null)
                {
                    throw new ArgumentException($"Location {locationId} not found");
                }

                // Soft delete
                location.IsActive = false;
                system.Version++;
                system.LastModified = DateTime.UtcNow;

                await _storageService.SaveCategorySystemAsync(system);

                _logger.LogInformation("Deleted business location: {LocationName}", location.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting business location: {LocationId}", locationId);
                throw;
            }
        }

        // ===== LOCATION ORDERING =====

        public async Task ReorderLocationsAsync(List<string> locationIds)
        {
            try
            {
                var system = await GetOrCreateSystemAsync();

                for (int i = 0; i < locationIds.Count; i++)
                {
                    var location = system.Locations.FirstOrDefault(l => l.Id == locationIds[i]);
                    if (location != null)
                    {
                        location.SortOrder = i;
                    }
                }

                system.Version++;
                system.LastModified = DateTime.UtcNow;

                await _storageService.SaveCategorySystemAsync(system);

                _logger.LogInformation("Reordered {Count} business locations", locationIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reordering business locations");
                throw;
            }
        }

        // ===== LOCATION QUERIES =====

        public async Task<BusinessLocation?> GetLocationByIdAsync(string locationId)
        {
            try
            {
                var locations = await GetBusinessLocationsAsync();
                return locations.FirstOrDefault(l => l.Id == locationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting location by ID: {LocationId}", locationId);
                throw;
            }
        }

        public async Task<bool> LocationExistsAsync(string locationName)
        {
            try
            {
                var locations = await GetBusinessLocationsAsync();
                return locations.Any(l => l.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if location exists: {LocationName}", locationName);
                throw;
            }
        }

        // ===== HELPER METHODS =====

        private async Task<CategorySystem> GetOrCreateSystemAsync()
        {
            var system = await _storageService.LoadCategorySystemAsync();

            if (system == null)
            {
                system = _storageService.CreateDefaultCategorySystem();
                await _storageService.SaveCategorySystemAsync(system);
            }

            return system;
        }
    }
}