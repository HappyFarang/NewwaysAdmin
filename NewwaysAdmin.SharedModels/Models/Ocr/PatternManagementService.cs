// NewwaysAdmin.SharedModels/Services/Ocr/PatternManagementService.cs
// UPDATED for 3-level structure

using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.Shared.IO;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    public class PatternManagementService
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

                _logger.LogDebug("Retrieved {Count} collection names", names.Count);
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
                return new List<string>();

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection))
                {
                    var names = collection.SubCollections.Keys.ToList();
                    _logger.LogDebug("Retrieved {Count} sub-collection names from '{CollectionName}'",
                        names.Count, collectionName);
                    return names;
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sub-collection names from '{CollectionName}'", collectionName);
                return new List<string>();
            }
        }

        /// <summary>
        /// Get list of search pattern names in a specific sub-collection
        /// </summary>
        public async Task<List<string>> GetSearchPatternNamesAsync(string collectionName, string subCollectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName))
                return new List<string>();

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SubCollections.TryGetValue(subCollectionName, out var subCollection))
                {
                    var names = subCollection.SearchPatterns.Keys.ToList();
                    _logger.LogDebug("Retrieved {Count} pattern names from '{CollectionName}.{SubCollectionName}'",
                        names.Count, collectionName, subCollectionName);
                    return names;
                }

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
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) || string.IsNullOrWhiteSpace(patternName))
                return null;

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
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                // Get or create the main collection
                if (!library.Collections.TryGetValue(collectionName, out var collection))
                {
                    collection = new PatternCollection { Name = collectionName };
                    library.Collections[collectionName] = collection;
                }

                // Get or create the sub-collection
                if (!collection.SubCollections.TryGetValue(subCollectionName, out var subCollection))
                {
                    subCollection = new PatternSubCollection { Name = subCollectionName };
                    collection.SubCollections[subCollectionName] = subCollection;
                }

                // Ensure pattern has proper name
                pattern.SearchName = patternName;

                // Add or update the pattern
                subCollection.SearchPatterns[patternName] = pattern;

                var success = await SaveLibraryAsync(library);

                if (success)
                {
                    _logger.LogInformation("Saved pattern '{PatternName}' in '{CollectionName}.{SubCollectionName}'",
                        patternName, collectionName, subCollectionName);
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
                return false;

            try
            {
                await _lock.WaitAsync();

                library.Collections ??= new Dictionary<string, PatternCollection>();

                await _storage.SaveAsync(LIBRARY_KEY, library);

                _logger.LogInformation("Saved pattern library with {CollectionCount} collections",
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
        /// Delete a specific search pattern
        /// </summary>
        /// <summary>
        /// Delete a specific search pattern and clean up empty containers
        /// </summary>
        public async Task<bool> DeleteSearchPatternAsync(string collectionName, string subCollectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) || string.IsNullOrWhiteSpace(patternName))
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SubCollections.TryGetValue(subCollectionName, out var subCollection) &&
                    subCollection.SearchPatterns.Remove(patternName))
                {
                    // AUTO-CLEANUP: Remove empty sub-collection if it has no patterns
                    if (subCollection.SearchPatterns.Count == 0)
                    {
                        collection.SubCollections.Remove(subCollectionName);
                        _logger.LogInformation("Removed empty sub-collection '{SubCollectionName}' from '{CollectionName}'",
                            subCollectionName, collectionName);
                    }

                    // AUTO-CLEANUP: Remove empty collection if it has no sub-collections
                    if (collection.SubCollections.Count == 0)
                    {
                        library.Collections.Remove(collectionName);
                        _logger.LogInformation("Removed empty collection '{CollectionName}'", collectionName);
                    }

                    var success = await SaveLibraryAsync(library);

                    if (success)
                    {
                        _logger.LogInformation("Deleted pattern '{PatternName}' from '{CollectionName}.{SubCollectionName}' with auto-cleanup",
                            patternName, collectionName, subCollectionName);
                    }

                    return success;
                }

                _logger.LogDebug("Pattern '{PatternName}' not found in '{CollectionName}.{SubCollectionName}' for deletion",
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

        // ADD this new method to clean up existing mess
        /// <summary>
        /// Clean up empty sub-collections and collections in the entire library
        /// Call this once to fix existing data, then auto-cleanup will prevent future issues
        /// </summary>
        public async Task<bool> CleanupEmptyContainersAsync()
        {
            try
            {
                var library = await LoadLibraryAsync();
                var cleanupCount = 0;

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
        /// Check if a pattern exists
        /// </summary>
        public async Task<bool> SearchPatternExistsAsync(string collectionName, string subCollectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(subCollectionName) || string.IsNullOrWhiteSpace(patternName))
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

        #endregion

        #region Private Helpers

        private static PatternLibrary CreateEmptyLibrary()
        {
            return new PatternLibrary();
        }

        #endregion

        public void Dispose()
        {
            _lock?.Dispose();
        }
    }
}