// NewwaysAdmin.SharedModels/Services/Ocr/PatternManagementService.cs
// CLEANED and OPTIMIZED for 3-level structure

using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.Shared.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    public class PatternManagementService : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PatternManagementService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private const string LIBRARY_KEY = "pattern-library";
        private IDataStorage<PatternLibrary>? _storage;

        public PatternManagementService(
            IServiceProvider serviceProvider,
            ILogger<PatternManagementService> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ✅ FIXED: Properly resolve StorageManager using Type.GetType
        private IDataStorage<PatternLibrary> GetStorage()
        {
            if (_storage == null)
            {
                using var scope = _serviceProvider.CreateScope();

                // Get the StorageManager type by fully qualified name
                var storageManagerType = Type.GetType("NewwaysAdmin.WebAdmin.Infrastructure.Storage.StorageManager, NewwaysAdmin.WebAdmin");

                if (storageManagerType == null)
                {
                    throw new InvalidOperationException("Could not find StorageManager type. Ensure WebAdmin assembly is loaded.");
                }

                // Get the StorageManager instance from DI
                var storageManager = scope.ServiceProvider.GetRequiredService(storageManagerType);

                // Use reflection to call GetStorageSync since we can't reference WebAdmin
                var method = storageManagerType.GetMethod("GetStorageSync");
                if (method == null)
                {
                    throw new InvalidOperationException("Could not find GetStorageSync method on StorageManager");
                }

                var genericMethod = method.MakeGenericMethod(typeof(PatternLibrary));
                _storage = (IDataStorage<PatternLibrary>)genericMethod.Invoke(storageManager, new object[] { "OcrPatterns" })!;

                _logger.LogDebug("Successfully initialized storage for OcrPatterns");
            }
            return _storage;
        }

        #region Load Operations

        public async Task<PatternLibrary> LoadLibraryAsync()
        {
            try
            {
                await _lock.WaitAsync();
                var storage = GetStorage();

                if (await storage.ExistsAsync(LIBRARY_KEY))
                {
                    var library = await storage.LoadAsync(LIBRARY_KEY);
                    library.Collections ??= new Dictionary<string, PatternCollection>();
                    _logger.LogDebug("Loaded pattern library with {CollectionCount} collections",
                        library.Collections.Count);
                    return library;
                }

                _logger.LogInformation("No existing pattern library found, creating new empty library");
                return CreateEmptyLibrary();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pattern library, returning empty library");
                return CreateEmptyLibrary();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<string>> GetPatternNamesAsync(string collectionName, string subCollectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName))
            {
                _logger.LogWarning("GetPatternNamesAsync called with empty parameters");
                return new List<string>();
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SubCollections.TryGetValue(subCollectionName, out var subCollection))
                {
                    var names = subCollection.SearchPatterns.Keys.ToList();
                    _logger.LogDebug("Found {Count} pattern names for '{CollectionName}.{SubCollectionName}': {Names}",
                        names.Count, collectionName, subCollectionName, string.Join(", ", names));
                    return names;
                }

                _logger.LogDebug("No patterns found for '{CollectionName}.{SubCollectionName}'",
                    collectionName, subCollectionName);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pattern names from '{CollectionName}.{SubCollectionName}'",
                    collectionName, subCollectionName);
                return new List<string>();
            }
        }

        public async Task<SearchPattern?> LoadSearchPatternAsync(string collectionName, string subCollectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) ||
                string.IsNullOrWhiteSpace(patternName))
            {
                _logger.LogWarning("LoadSearchPatternAsync called with empty parameters");
                return null;
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SubCollections.TryGetValue(subCollectionName, out var subCollection) &&
                    subCollection.SearchPatterns.TryGetValue(patternName, out var pattern))
                {
                    _logger.LogDebug("Loaded pattern '{PatternName}' from '{CollectionName}.{SubCollectionName}'",
                        patternName, collectionName, subCollectionName);
                    return pattern;
                }

                _logger.LogDebug("Pattern '{PatternName}' not found in '{CollectionName}.{SubCollectionName}'",
                    patternName, collectionName, subCollectionName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pattern '{PatternName}' from '{CollectionName}.{SubCollectionName}'",
                    patternName, collectionName, subCollectionName);
                return null;
            }
        }

        #endregion

        #region Save Operations

        public async Task<bool> SaveSearchPatternAsync(string collectionName, string subCollectionName, string patternName, SearchPattern pattern)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) ||
                string.IsNullOrWhiteSpace(patternName) || pattern == null)
            {
                _logger.LogWarning("SaveSearchPatternAsync called with invalid parameters");
                return false;
            }

            try
            {
                var library = await LoadLibraryAsync();

                // Get or create the main collection
                if (!library.Collections.TryGetValue(collectionName, out var collection))
                {
                    collection = new PatternCollection { Name = collectionName };
                    library.Collections[collectionName] = collection;
                    _logger.LogInformation("Created new collection '{CollectionName}'", collectionName);
                }

                // Get or create the sub-collection
                if (!collection.SubCollections.TryGetValue(subCollectionName, out var subCollection))
                {
                    subCollection = new PatternSubCollection { Name = subCollectionName };
                    collection.SubCollections[subCollectionName] = subCollection;
                    _logger.LogInformation("Created new sub-collection '{SubCollectionName}' in '{CollectionName}'",
                        subCollectionName, collectionName);
                }

                // Ensure pattern has proper name
                pattern.SearchName = patternName;

                // Add or update the pattern
                var isUpdate = subCollection.SearchPatterns.ContainsKey(patternName);
                subCollection.SearchPatterns[patternName] = pattern;

                var success = await SaveLibraryAsync(library);

                if (success)
                {
                    _logger.LogInformation("{Action} pattern '{PatternName}' in '{CollectionName}.{SubCollectionName}'",
                        isUpdate ? "Updated" : "Created", patternName, collectionName, subCollectionName);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving pattern '{PatternName}' in '{CollectionName}.{SubCollectionName}'",
                    patternName, collectionName, subCollectionName);
                return false;
            }
        }

        public async Task<bool> SaveLibraryAsync(PatternLibrary library)
        {
            if (library == null)
            {
                _logger.LogWarning("SaveLibraryAsync called with null library");
                return false;
            }

            try
            {
                await _lock.WaitAsync();

                library.Collections ??= new Dictionary<string, PatternCollection>();

                await _storage!.SaveAsync(LIBRARY_KEY, library);

                _logger.LogDebug("Saved pattern library with {CollectionCount} collections",
                    library.Collections.Count);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving pattern library");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        #endregion

        #region Delete Operations

        public async Task<bool> DeleteSearchPatternAsync(string collectionName, string subCollectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) ||
                string.IsNullOrWhiteSpace(patternName))
            {
                _logger.LogWarning("DeleteSearchPatternAsync called with empty parameters");
                return false;
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SubCollections.TryGetValue(subCollectionName, out var subCollection) &&
                    subCollection.SearchPatterns.Remove(patternName))
                {
                    _logger.LogInformation("Deleted pattern '{PatternName}' from '{CollectionName}.{SubCollectionName}'",
                        patternName, collectionName, subCollectionName);

                    await CleanupEmptyContainersAsync();
                    return await SaveLibraryAsync(library);
                }

                _logger.LogWarning("Pattern '{PatternName}' not found in '{CollectionName}.{SubCollectionName}'",
                    patternName, collectionName, subCollectionName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting pattern '{PatternName}' from '{CollectionName}.{SubCollectionName}'",
                    patternName, collectionName, subCollectionName);
                return false;
            }
        }

        private async Task<bool> CleanupEmptyContainersAsync()
        {
            try
            {
                var library = await LoadLibraryAsync();
                var collectionsToRemove = new List<string>();
                var cleanupCount = 0;

                foreach (var (collectionName, collection) in library.Collections)
                {
                    var subCollectionsToRemove = new List<string>();

                    foreach (var (subCollectionName, subCollection) in collection.SubCollections)
                    {
                        if (subCollection.SearchPatterns.Count == 0)
                        {
                            subCollectionsToRemove.Add(subCollectionName);
                            cleanupCount++;
                        }
                    }

                    foreach (var subCollectionName in subCollectionsToRemove)
                    {
                        collection.SubCollections.Remove(subCollectionName);
                        _logger.LogInformation("Cleaned up empty sub-collection '{SubCollectionName}' from '{CollectionName}'",
                            subCollectionName, collectionName);
                    }

                    if (collection.SubCollections.Count == 0)
                    {
                        collectionsToRemove.Add(collectionName);
                        cleanupCount++;
                    }
                }

                foreach (var collectionName in collectionsToRemove)
                {
                    library.Collections.Remove(collectionName);
                    _logger.LogInformation("Cleaned up empty collection '{CollectionName}'", collectionName);
                }

                if (cleanupCount > 0)
                {
                    var success = await SaveLibraryAsync(library);
                    if (success)
                    {
                        _logger.LogInformation("Successfully cleaned up {Count} empty containers", cleanupCount);
                    }
                    return success;
                }
                else
                {
                    _logger.LogInformation("No empty containers found - library is already clean");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup of empty containers");
                return false;
            }
        }

        #endregion

        #region Utility Operations

        public async Task<List<string>> GetCollectionNamesAsync()
        {
            try
            {
                var library = await LoadLibraryAsync();
                var names = library.Collections.Keys.ToList();

                _logger.LogDebug("Retrieved {Count} collection names: {Names}",
                    names.Count, string.Join(", ", names));
                return names;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting collection names");
                return new List<string>();
            }
        }

        public async Task<List<string>> GetSubCollectionNamesAsync(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                _logger.LogWarning("GetSubCollectionNamesAsync called with empty collectionName");
                return new List<string>();
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection))
                {
                    var names = collection.SubCollections.Keys.ToList();
                    _logger.LogDebug("Retrieved {Count} sub-collection names from '{CollectionName}': {Names}",
                        names.Count, collectionName, string.Join(", ", names));
                    return names;
                }

                _logger.LogDebug("No collection found with name '{CollectionName}'", collectionName);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sub-collection names from '{CollectionName}'", collectionName);
                return new List<string>();
            }
        }

        public async Task<List<string>> GetAvailableFormatsAsync(string documentType)
        {
            return await GetSubCollectionNamesAsync(documentType);
        }

        public async Task<List<string>> GetSearchPatternNamesAsync(string collectionName, string subCollectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName))
            {
                _logger.LogWarning("GetSearchPatternNamesAsync called with empty parameters");
                return new List<string>();
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SubCollections.TryGetValue(subCollectionName, out var subCollection))
                {
                    var names = subCollection.SearchPatterns.Keys.ToList();
                    _logger.LogDebug("Retrieved {Count} pattern names from '{CollectionName}.{SubCollectionName}': {Names}",
                        names.Count, collectionName, subCollectionName, string.Join(", ", names));
                    return names;
                }

                _logger.LogDebug("No patterns found for '{CollectionName}.{SubCollectionName}'",
                    collectionName, subCollectionName);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pattern names from '{CollectionName}.{SubCollectionName}'",
                    collectionName, subCollectionName);
                return new List<string>();
            }
        }

        public async Task<bool> HasPatternsAsync(string collectionName, string subCollectionName)
        {
            if (string.IsNullOrEmpty(collectionName) || string.IsNullOrEmpty(subCollectionName))
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                var hasPatterns = library.Collections.TryGetValue(collectionName, out var collection) &&
                                 collection.SubCollections.TryGetValue(subCollectionName, out var subCollection) &&
                                 subCollection.SearchPatterns.Any();

                _logger.LogDebug("HasPatterns check for {CollectionName}.{SubCollectionName}: {HasPatterns}",
                    collectionName, subCollectionName, hasPatterns);

                return hasPatterns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking patterns for {CollectionName}.{SubCollectionName}",
                    collectionName, subCollectionName);
                return false;
            }
        }

        public async Task<bool> SearchPatternExistsAsync(string collectionName, string subCollectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) ||
                string.IsNullOrWhiteSpace(patternName))
                return false;

            try
            {
                var pattern = await LoadSearchPatternAsync(collectionName, subCollectionName, patternName);
                return pattern != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if pattern '{PatternName}' exists in '{CollectionName}.{SubCollectionName}'",
                    patternName, collectionName, subCollectionName);
                return false;
            }
        }

        public async Task<bool> CreateCollectionAsync(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
            {
                _logger.LogWarning("CreateCollectionAsync called with empty collection name");
                return false;
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (!library.Collections.ContainsKey(collectionName))
                {
                    library.Collections[collectionName] = new PatternCollection { Name = collectionName };

                    var success = await SaveLibraryAsync(library);

                    if (success)
                    {
                        _logger.LogInformation("Created new collection '{CollectionName}'", collectionName);
                    }

                    return success;
                }

                _logger.LogDebug("Collection '{CollectionName}' already exists", collectionName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating collection '{CollectionName}'", collectionName);
                return false;
            }
        }

        public async Task<bool> CreateSubCollectionAsync(string collectionName, string subCollectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName))
            {
                _logger.LogWarning("CreateSubCollectionAsync called with empty parameters");
                return false;
            }

            try
            {
                var library = await LoadLibraryAsync();

                if (!library.Collections.TryGetValue(collectionName, out var collection))
                {
                    collection = new PatternCollection { Name = collectionName };
                    library.Collections[collectionName] = collection;
                    _logger.LogInformation("Created new collection '{CollectionName}'", collectionName);
                }

                if (!collection.SubCollections.ContainsKey(subCollectionName))
                {
                    collection.SubCollections[subCollectionName] = new PatternSubCollection { Name = subCollectionName };

                    var success = await SaveLibraryAsync(library);

                    if (success)
                    {
                        _logger.LogInformation("Created sub-collection '{SubCollectionName}' in '{CollectionName}'",
                            subCollectionName, collectionName);
                    }

                    return success;
                }

                _logger.LogDebug("Sub-collection '{SubCollectionName}' already exists in '{CollectionName}'",
                    subCollectionName, collectionName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sub-collection '{SubCollectionName}' in '{CollectionName}'",
                    subCollectionName, collectionName);
                return false;
            }
        }

        public async Task LogLibraryStructureAsync()
        {
            try
            {
                var library = await LoadLibraryAsync();

                _logger.LogInformation("=== PATTERN LIBRARY STRUCTURE ===");
                _logger.LogInformation("Total Collections: {Count}", library.Collections.Count);

                if (!library.Collections.Any())
                {
                    _logger.LogInformation("📝 Library is empty - no collections found");
                }

                foreach (var (collectionName, collection) in library.Collections)
                {
                    _logger.LogInformation("📁 Collection: {CollectionName} ({SubCount} sub-collections)",
                        collectionName, collection.SubCollections.Count);

                    if (!collection.SubCollections.Any())
                    {
                        _logger.LogInformation("    📝 No sub-collections in {CollectionName}", collectionName);
                    }

                    foreach (var (subName, subCollection) in collection.SubCollections)
                    {
                        _logger.LogInformation("    🏷️ {SubName} ({PatternCount} patterns): {Patterns}",
                            subName,
                            subCollection.SearchPatterns.Count,
                            subCollection.SearchPatterns.Any() ?
                                string.Join(", ", subCollection.SearchPatterns.Keys) : "none");
                    }
                }
                _logger.LogInformation("=== END LIBRARY STRUCTURE ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error logging library structure");
            }
        }

        #endregion

        #region Private Helpers

        private static PatternLibrary CreateEmptyLibrary()
        {
            return new PatternLibrary
            {
                Collections = new Dictionary<string, PatternCollection>()
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _lock?.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}