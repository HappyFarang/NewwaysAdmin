// File: Mobile/NewwaysAdmin.Mobile/Services/Cache/CacheManager.cs
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Services.Cache
{
    /// <summary>
    /// Cache management and sync coordination
    /// Single responsibility: Coordinate caching operations and sync logic
    /// </summary>
    public class CacheManager
    {
        private readonly ILogger<CacheManager> _logger;
        private readonly CacheStorage _storage;

        public CacheManager(
            ILogger<CacheManager> logger,
            CacheStorage storage)
        {
            _logger = logger;
            _storage = storage;
        }

        // ===== CACHE DATA FOR SYNC =====

        /// <summary>
        /// Cache small data (inline JSON) - for simple objects like category updates
        /// </summary>
        public async Task<string> CacheInlineDataAsync<T>(
            T data,
            string dataType,
            string messageType,
            CacheRetentionPolicy retentionPolicy = CacheRetentionPolicy.DeleteAfterSync)
        {
            try
            {
                var item = new CacheItem
                {
                    DataType = dataType,
                    MessageType = messageType,
                    RetentionPolicy = retentionPolicy,
                    InlineJsonData = System.Text.Json.JsonSerializer.Serialize(data)
                };

                await _storage.AddCacheItemAsync(item);
                _logger.LogInformation("Cached inline data {DataType} with ID {ItemId}", dataType, item.Id);
                return item.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching inline data {DataType}", dataType);
                throw;
            }
        }

        /// <summary>
        /// Cache large data (file-based) - for binary data like images
        /// </summary>
        public async Task<string> CacheFileDataAsync<T>(
            T data,
            string dataType,
            string messageType,
            CacheRetentionPolicy retentionPolicy = CacheRetentionPolicy.DeleteAfterSync) where T : class, new()
        {
            try
            {
                // Save data to file
                var fileName = await _storage.SaveDataAsync(data, dataType);

                var item = new CacheItem
                {
                    DataType = dataType,
                    MessageType = messageType,
                    RetentionPolicy = retentionPolicy,
                    DataFilePath = fileName
                };

                await _storage.AddCacheItemAsync(item);
                _logger.LogInformation("Cached file data {DataType} with ID {ItemId}", dataType, item.Id);
                return item.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching file data {DataType}", dataType);
                throw;
            }
        }

        // ===== SYNC OPERATIONS =====

        public async Task<List<CacheItem>> GetPendingSyncItemsAsync()
        {
            return await _storage.GetPendingCacheItemsAsync();
        }

        public async Task MarkAsSyncedAsync(string itemId)
        {
            try
            {
                var items = await _storage.GetAllCacheItemsAsync();
                var item = items.FirstOrDefault(i => i.Id == itemId);

                if (item != null)
                {
                    item.SyncedAt = DateTime.UtcNow;
                    item.ErrorMessage = null;
                    await _storage.UpdateCacheItemAsync(item);

                    _logger.LogInformation("Marked cache item {ItemId} as synced", itemId);

                    // Handle retention policy
                    if (item.RetentionPolicy == CacheRetentionPolicy.DeleteAfterSync)
                    {
                        await DeleteCacheItemAsync(itemId);
                    }
                }
                else
                {
                    _logger.LogWarning("Cache item {ItemId} not found for sync marking", itemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking cache item {ItemId} as synced", itemId);
                throw;
            }
        }

        public async Task MarkAsFailedAsync(string itemId, string errorMessage)
        {
            try
            {
                var items = await _storage.GetAllCacheItemsAsync();
                var item = items.FirstOrDefault(i => i.Id == itemId);

                if (item != null)
                {
                    item.RetryCount++;
                    item.ErrorMessage = errorMessage;
                    await _storage.UpdateCacheItemAsync(item);

                    _logger.LogWarning("Cache item {ItemId} failed (retry {RetryCount}/{MaxRetries}): {Error}",
                        itemId, item.RetryCount, item.MaxRetries, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking cache item {ItemId} as failed", itemId);
                throw;
            }
        }

        // ===== DATA RETRIEVAL =====

        public async Task<T?> GetCachedDataAsync<T>(string itemId) where T : class, new()
        {
            try
            {
                var items = await _storage.GetAllCacheItemsAsync();
                var item = items.FirstOrDefault(i => i.Id == itemId);

                if (item != null)
                {
                    return await _storage.LoadDataAsync<T>(item);
                }
                else
                {
                    _logger.LogWarning("Cache item {ItemId} not found", itemId);
                    return default(T);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cached data for item {ItemId}", itemId);
                return default(T);
            }
        }

        // ===== CLEANUP =====

        public async Task DeleteCacheItemAsync(string itemId)
        {
            try
            {
                var items = await _storage.GetAllCacheItemsAsync();
                var item = items.FirstOrDefault(i => i.Id == itemId);

                if (item != null)
                {
                    // Delete data file if it exists
                    if (!string.IsNullOrEmpty(item.DataFilePath))
                    {
                        await _storage.DeleteDataFileAsync(item.DataFilePath);
                    }

                    // Remove from index
                    await _storage.RemoveCacheItemAsync(itemId);
                    _logger.LogInformation("Deleted cache item {ItemId}", itemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting cache item {ItemId}", itemId);
                throw;
            }
        }

        // ===== STATISTICS =====

        public async Task<CacheStats> GetCacheStatsAsync()
        {
            return await _storage.GetCacheStatsAsync();
        }
    }
}