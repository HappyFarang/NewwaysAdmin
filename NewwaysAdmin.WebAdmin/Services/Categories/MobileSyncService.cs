// File: NewwaysAdmin.WebAdmin/Services/Categories/MobileSyncService.cs
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Categories
{
    /// <summary>
    /// Handles mobile sync data
    /// Simplified - mobile uses same CategorySystem as server
    /// </summary>
    public class MobileSyncService
    {
        private readonly CategoryStorageService _storageService;
        private readonly ILogger<MobileSyncService> _logger;

        public MobileSyncService(
            CategoryStorageService storageService,
            ILogger<MobileSyncService> logger)
        {
            _storageService = storageService;
            _logger = logger;
        }

        // ===== MOBILE SYNC DATA =====

        /// <summary>
        /// Get category system for mobile sync
        /// Same data structure as server uses
        /// </summary>
        public async Task<CategorySystem> GetMobileSyncDataAsync()
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
                _logger.LogError(ex, "Error loading mobile sync data");
                throw;
            }
        }

        /// <summary>
        /// Force refresh of category data
        /// </summary>
        public async Task<CategorySystem> RegenerateMobileSyncAsync()
        {
            return await GetMobileSyncDataAsync();
        }

        /// <summary>
        /// Invalidate cache - kept for compatibility
        /// </summary>
        public async Task InvalidateCacheAsync()
        {
            _logger.LogDebug("Mobile sync cache invalidated");
            await Task.CompletedTask;
        }
    }
}