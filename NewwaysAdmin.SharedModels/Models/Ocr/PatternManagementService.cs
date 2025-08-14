// NewwaysAdmin.SharedModels/Services/Ocr/PatternManagementService.cs
// CLEANED and OPTIMIZED for 3-level structure

using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.Shared.IO;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    public class PatternManagementService : IDisposable
    {
        private readonly IDataStorage<PatternLibrary> _storage;
        private readonly ILogger<PatternManagementService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private const string LIBRARY_KEY = "pattern-library";

        public PatternManagementService(
            IDataStorage<PatternLibrary> storage,
            ILogger<PatternManagementService> logger)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Load Operations

        /// <summary>
        /// Load the complete pattern library from storage
        /// </summary>
        public async Task<PatternLibrary> LoadLibraryAsync()
        {
            try
            {
                await _lock.WaitAsync();

                if (await _storage.ExistsAsync(LIBRARY_KEY))
                {
                    var library = await _storage.LoadAsync(LIBRARY_KEY);
                    library.Collections ??= new Dictionary<string, PatternCollection>();

                    _logger.LogDebug("Loaded pattern library with {CollectionCount} collections",
                        library.Collections.Count);

                    return library;
                }

                var emptyLibrary = CreateEmptyLibrary();
                _logger.LogInformation("No existing pattern library found, returning empty library");
                return emptyLibrary;
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

        /// <summary>
        /// Get list of all document type names (BankSlips, Invoices, etc.)
        /// </summary>
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

        /// <summary>
        /// Get list of sub-collection names for a specific document type (KBIZ, KBank, etc.)
        /// </summary>
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

        /// <summary>
        /// Get available formats (sub-collection names) for a document type - alias for GetSubCollectionNamesAsync
        /// </summary>
        public async Task<List<string>> GetAvailableFormatsAsync(string documentType)
        {
            return await GetSubCollectionNamesAsync(documentType);
        }

        /// <summary>
        /// Get list of search pattern names in a specific sub-collection
        /// </summary>
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

        /// <summary>
        /// Load a specific search pattern
        /// </summary>
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

        /// <summary>
        /// Save or update a specific search pattern (creates hierarchy as needed)
        /// </summary>
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

        /// <summary>
        /// Save the complete pattern library to storage
        /// </summary>
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

                await _storage.SaveAsync(LIBRARY_KEY, library);

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

        /// <summary>
        /// Delete a specific search pattern and auto-cleanup empty containers
        /// </summary>
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

                    // AUTO-CLEANUP: Remove empty sub-collection if it has no patterns
                    if (subCollection.SearchPatterns.Count == 0)
                    {
                        collection.SubCollections.Remove(subCollectionName);
                        _logger.LogInformation("Auto-removed empty sub-collection '{SubCollectionName}' from '{CollectionName}'",
                            subCollectionName, collectionName);
                    }

                    // AUTO-CLEANUP: Remove empty collection if it has no sub-collections
                    if (collection.SubCollections.Count == 0)
                    {
                        library.Collections.Remove(collectionName);
                        _logger.LogInformation("Auto-removed empty collection '{CollectionName}'", collectionName);
                    }

                    var success = await SaveLibraryAsync(library);
                    return success;
                }

                _logger.LogWarning("Pattern '{PatternName}' not found in '{CollectionName}.{SubCollectionName}' for deletion",
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

        /// <summary>
        /// Clean up empty sub-collections and collections in the entire library
        /// </summary>
        public async Task<bool> CleanupEmptyContainersAsync()
        {
            try
            {
                var library = await LoadLibraryAsync();
                var cleanupCount = 0;

                _logger.LogInformation("Starting library cleanup...");

                // Clean up empty sub-collections
                var collectionsToRemove = new List<string>();

                foreach (var (collectionName, collection) in library.Collections.ToList())
                {
                    var subCollectionsToRemove = new List<string>();

                    foreach (var (subCollectionName, subCollection) in collection.SubCollections.ToList())
                    {
                        if (subCollection.SearchPatterns.Count == 0)
                        {
                            subCollectionsToRemove.Add(subCollectionName);
                            cleanupCount++;
                        }
                    }

                    // Remove empty sub-collections
                    foreach (var subCollectionName in subCollectionsToRemove)
                    {
                        collection.SubCollections.Remove(subCollectionName);
                        _logger.LogInformation("Cleaned up empty sub-collection '{SubCollectionName}' from '{CollectionName}'",
                            subCollectionName, collectionName);
                    }

                    // Mark empty collections for removal
                    if (collection.SubCollections.Count == 0)
                    {
                        collectionsToRemove.Add(collectionName);
                        cleanupCount++;
                    }
                }

                // Remove empty collections
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

        /// <summary>
        /// Check if patterns exist for a specific document type and format
        /// </summary>
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

        /// <summary>
        /// Check if a specific pattern exists
        /// </summary>
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

        /// <summary>
        /// Create a new sub-collection for a document type
        /// </summary>
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

                // Get or create the main collection
                if (!library.Collections.TryGetValue(collectionName, out var collection))
                {
                    collection = new PatternCollection { Name = collectionName };
                    library.Collections[collectionName] = collection;
                    _logger.LogInformation("Created new collection '{CollectionName}'", collectionName);
                }

                // Create the sub-collection if it doesn't exist
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

        /// <summary>
        /// Debug method to log the complete library structure
        /// </summary>
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
                            subCollection.SearchPatterns.Any() ? string.Join(", ", subCollection.SearchPatterns.Keys) : "none");
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