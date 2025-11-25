// File: Mobile/NewwaysAdmin.Mobile/Services/Cache/CacheStorage.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Mobile.Services.Cache
{
    /// <summary>
    /// Cache storage using IO Manager
    /// Single responsibility: Store and retrieve cache items and their data
    /// </summary>
    public class CacheStorage
    {
        private readonly ILogger<CacheStorage> _logger;
        private readonly EnhancedStorageFactory _storageFactory;

        private const string CACHE_INDEX_FILE = "cache_index.json";
        private const string CACHE_FOLDER = "SyncQueue";  // FIXED: Use folder that actually exists

        public CacheStorage(
            ILogger<CacheStorage> logger,
            EnhancedStorageFactory storageFactory)
        {
            _logger = logger;
            _storageFactory = storageFactory;
        }

        // ===== CACHE INDEX OPERATIONS =====

        public async Task<List<CacheItem>> GetAllCacheItemsAsync()
        {
            try
            {
                var cacheIndex = await LoadCacheIndexAsync();
                return cacheIndex.Items;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading cache items");
                return new List<CacheItem>();
            }
        }

        public async Task<List<CacheItem>> GetPendingCacheItemsAsync()
        {
            try
            {
                var allItems = await GetAllCacheItemsAsync();
                return allItems.Where(item => item.ShouldRetry).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pending cache items");
                return new List<CacheItem>();
            }
        }

        public async Task AddCacheItemAsync(CacheItem item)
        {
            try
            {
                var cacheIndex = await LoadCacheIndexAsync();
                cacheIndex.Items.Add(item);
                await SaveCacheIndexAsync(cacheIndex);

                _logger.LogDebug("Added cache item {ItemId} to index", item.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding cache item {ItemId}", item.Id);
                throw;
            }
        }

        public async Task UpdateCacheItemAsync(CacheItem item)
        {
            try
            {
                var cacheIndex = await LoadCacheIndexAsync();
                var existingItem = cacheIndex.Items.FirstOrDefault(i => i.Id == item.Id);

                if (existingItem != null)
                {
                    var index = cacheIndex.Items.IndexOf(existingItem);
                    cacheIndex.Items[index] = item;
                    await SaveCacheIndexAsync(cacheIndex);

                    _logger.LogDebug("Updated cache item {ItemId}", item.Id);
                }
                else
                {
                    _logger.LogWarning("Cache item {ItemId} not found for update", item.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating cache item {ItemId}", item.Id);
                throw;
            }
        }

        public async Task RemoveCacheItemAsync(string itemId)
        {
            try
            {
                var cacheIndex = await LoadCacheIndexAsync();
                var item = cacheIndex.Items.FirstOrDefault(i => i.Id == itemId);

                if (item != null)
                {
                    cacheIndex.Items.Remove(item);
                    await SaveCacheIndexAsync(cacheIndex);

                    // Delete associated data file if it exists
                    if (!string.IsNullOrEmpty(item.DataFilePath))
                    {
                        await DeleteDataFileAsync(item.DataFilePath);
                    }

                    _logger.LogDebug("Removed cache item {ItemId}", itemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache item {ItemId}", itemId);
                throw;
            }
        }

        // ===== DATA OPERATIONS =====

        public async Task<T?> LoadDataAsync<T>(CacheItem item) where T : class, new()
        {
            try
            {
                if (!string.IsNullOrEmpty(item.InlineJsonData))
                {
                    // Load from inline JSON
                    return System.Text.Json.JsonSerializer.Deserialize<T>(item.InlineJsonData);
                }
                else if (!string.IsNullOrEmpty(item.DataFilePath))
                {
                    // Load from file
                    var storage = _storageFactory.GetStorage<T>(CACHE_FOLDER);
                    return await storage.LoadAsync(item.DataFilePath);
                }
                else
                {
                    _logger.LogWarning("Cache item {ItemId} has no data source", item.Id);
                    return default(T);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data for cache item {ItemId}", item.Id);
                return default(T);
            }
        }

        public async Task<string> SaveDataAsync<T>(T data, string dataType) where T : class, new()
        {
            try
            {
                var fileName = $"{Guid.NewGuid()}_{dataType}.bin";
                var storage = _storageFactory.GetStorage<T>(CACHE_FOLDER);
                await storage.SaveAsync(fileName, data);

                _logger.LogDebug("Saved cache data to file {FileName}", fileName);
                return fileName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving cache data for type {DataType}", dataType);
                throw;
            }
        }

        public async Task DeleteDataFileAsync(string fileName)
        {
            try
            {
                var storage = _storageFactory.GetStorage<object>(CACHE_FOLDER);
                await storage.DeleteAsync(fileName);
                _logger.LogDebug("Deleted cache data file {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting cache data file {FileName}", fileName);
                // Don't throw - file might already be deleted
            }
        }

        // ===== STATISTICS =====

        public async Task<CacheStats> GetCacheStatsAsync()
        {
            try
            {
                var items = await GetAllCacheItemsAsync();

                return new CacheStats
                {
                    TotalItems = items.Count,
                    PendingItems = items.Count(i => i.ShouldRetry),
                    SyncedItems = items.Count(i => i.IsSynced),
                    FailedItems = items.Count(i => i.HasFailed),
                    DataTypeBreakdown = items.GroupBy(i => i.DataType).ToDictionary(g => g.Key, g => g.Count())
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache statistics");
                return new CacheStats();
            }
        }

        // ===== PRIVATE METHODS =====

        private async Task<CacheIndex> LoadCacheIndexAsync()
        {
            try
            {
                var storage = _storageFactory.GetStorage<CacheIndex>(CACHE_FOLDER);
                var index = await storage.LoadAsync(CACHE_INDEX_FILE);
                return index ?? new CacheIndex();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading cache index, creating new one");
                return new CacheIndex();
            }
        }

        private async Task SaveCacheIndexAsync(CacheIndex cacheIndex)
        {
            cacheIndex.LastModified = DateTime.UtcNow;
            var storage = _storageFactory.GetStorage<CacheIndex>(CACHE_FOLDER);
            await storage.SaveAsync(CACHE_INDEX_FILE, cacheIndex);
        }
    }

    /// <summary>
    /// Index of all cached items
    /// </summary>
    public class CacheIndex
    {
        public List<CacheItem> Items { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Cache statistics for monitoring
    /// </summary>
    public class CacheStats
    {
        public int TotalItems { get; set; }
        public int PendingItems { get; set; }
        public int SyncedItems { get; set; }
        public int FailedItems { get; set; }
        public Dictionary<string, int> DataTypeBreakdown { get; set; } = new();
    }
}