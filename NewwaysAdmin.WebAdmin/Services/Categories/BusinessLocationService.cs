// File: NewwaysAdmin.WebAdmin/Services/Categories/BusinessLocationService.cs
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles business location management
    /// Separate from categories but used in conjunction with them
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
                var locationSystem = await _storageService.LoadLocationSystemAsync();

                if (locationSystem == null)
                {
                    locationSystem = _storageService.CreateDefaultLocationSystem();
                    await _storageService.SaveLocationSystemAsync(locationSystem);
                }

                return locationSystem.Locations
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

        public async Task<BusinessLocation> AddBusinessLocationAsync(string locationName, string description = "", string createdBy = "System")
        {
            try
            {
                var locationSystem = await GetOrCreateLocationSystemAsync();

                var newLocation = new BusinessLocation
                {
                    Name = locationName,
                    Description = description,
                    IsActive = true,
                    SortOrder = locationSystem.Locations.Count,
                    CreatedBy = createdBy
                };

                locationSystem.Locations.Add(newLocation);
                locationSystem.Version++;
                locationSystem.LastModified = DateTime.UtcNow;

                await _storageService.SaveLocationSystemAsync(locationSystem);

                _logger.LogInformation("Added business location: {LocationName} by {CreatedBy}", locationName, createdBy);
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
                var locationSystem = await GetOrCreateLocationSystemAsync();
                var location = locationSystem.Locations.FirstOrDefault(l => l.Id == locationId);

                if (location == null)
                {
                    throw new ArgumentException($"Location {locationId} not found");
                }

                location.Name = name;
                location.Description = description;
                locationSystem.Version++;
                locationSystem.LastModified = DateTime.UtcNow;

                await _storageService.SaveLocationSystemAsync(locationSystem);

                _logger.LogInformation("Updated business location: {LocationName}", name);
                return location;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating business location: {LocationId}", locationId);
                throw;
            }
        }

        public async Task DeleteBusinessLocationAsync(string locationId, string deletedBy = "System")
        {
            try
            {
                var locationSystem = await GetOrCreateLocationSystemAsync();
                var location = locationSystem.Locations.FirstOrDefault(l => l.Id == locationId);

                if (location == null)
                {
                    throw new ArgumentException($"Location {locationId} not found");
                }

                // Soft delete
                location.IsActive = false;
                locationSystem.Version++;
                locationSystem.LastModified = DateTime.UtcNow;

                await _storageService.SaveLocationSystemAsync(locationSystem);

                _logger.LogInformation("Deleted business location: {LocationName} by {DeletedBy}", location.Name, deletedBy);
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
                var locationSystem = await GetOrCreateLocationSystemAsync();

                for (int i = 0; i < locationIds.Count; i++)
                {
                    var location = locationSystem.Locations.FirstOrDefault(l => l.Id == locationIds[i]);
                    if (location != null)
                    {
                        location.SortOrder = i;
                    }
                }

                locationSystem.Version++;
                locationSystem.LastModified = DateTime.UtcNow;

                await _storageService.SaveLocationSystemAsync(locationSystem);

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

        private async Task<LocationSystem> GetOrCreateLocationSystemAsync()
        {
            var locationSystem = await _storageService.LoadLocationSystemAsync();

            if (locationSystem == null)
            {
                locationSystem = _storageService.CreateDefaultLocationSystem();
                await _storageService.SaveLocationSystemAsync(locationSystem);
            }

            return locationSystem;
        }
    }
}