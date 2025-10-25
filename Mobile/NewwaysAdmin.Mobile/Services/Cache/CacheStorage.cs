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
        private const string CACHE_FOLDER = "OfflineCache";

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

                _logger.LogDebug("Added cache item {ItemId} of type {DataType}", item.Id, item.DataType);
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
                var existingIndex = cacheIndex.Items.FindIndex(i => i.Id == item.Id);

                if (existingIndex >= 0)
                {
                    cacheIndex.Items[existingIndex] = item;
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
                    _logger.LogDebug("Removed cache item {ItemId}", itemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache item {ItemId}", itemId);
                throw;
            }
        }

        // ===== DATA FILE OPERATIONS =====

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
                    // Load from IO Manager file
                    var storage = _storageFactory.GetStorage<T>(CACHE_FOLDER);
                    return await storage.LoadAsync(item.DataFilePath);
                }
                else
                {
                    _logger.LogWarning("Cache item {ItemId} has no data reference", item.Id);
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
                var storage = _storageFactory.GetStorage<T>(CACHE_FOLDER) ;
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
}