// NewwaysAdmin.SharedModels/Services/Ocr/PatternManagementService.cs
using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.Shared.IO;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Comprehensive service for managing OCR pattern collections
    /// Uses single-file storage with PatternLibrary containing all collections and patterns
    /// </summary>
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
        /// Returns empty library if file doesn't exist
        /// </summary>
        public async Task<PatternLibrary> LoadLibraryAsync()
        {
            try
            {
                await _lock.WaitAsync();

                if (await _storage.ExistsAsync(LIBRARY_KEY))
                {
                    var library = await _storage.LoadAsync(LIBRARY_KEY);

                    // Ensure Collections dictionary is never null
                    library.Collections ??= new Dictionary<string, PatternCollection>();

                    _logger.LogDebug("Loaded pattern library with {CollectionCount} collections",
                        library.Collections.Count);

                    return library;
                }

                // Return empty library if none exists
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
        /// Load a specific pattern collection by name
        /// Returns null if collection doesn't exist
        /// </summary>
        public async Task<PatternCollection?> LoadCollectionAsync(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return null;

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection))
                {
                    _logger.LogDebug("Loaded collection '{CollectionName}' with {PatternCount} patterns",
                        collectionName, collection.SearchPatterns?.Count ?? 0);
                    return collection;
                }

                _logger.LogDebug("Collection '{CollectionName}' not found", collectionName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading collection '{CollectionName}'", collectionName);
                return null;
            }
        }

        /// <summary>
        /// Load a specific search pattern from a collection
        /// Returns null if collection or pattern doesn't exist
        /// </summary>
        public async Task<SearchPattern?> LoadSearchPatternAsync(string collectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(patternName))
                return null;

            try
            {
                var collection = await LoadCollectionAsync(collectionName);

                if (collection?.SearchPatterns?.TryGetValue(patternName, out var pattern) == true)
                {
                    _logger.LogDebug("Loaded pattern '{PatternName}' from collection '{CollectionName}'",
                        patternName, collectionName);
                    return pattern;
                }

                _logger.LogDebug("Pattern '{PatternName}' not found in collection '{CollectionName}'",
                    patternName, collectionName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pattern '{PatternName}' from collection '{CollectionName}'",
                    patternName, collectionName);
                return null;
            }
        }

        /// <summary>
        /// Get list of all collection names in the library
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
        /// Get list of all search pattern names in a specific collection
        /// Returns empty list if collection doesn't exist
        /// </summary>
        public async Task<List<string>> GetSearchPatternNamesAsync(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return new List<string>();

            try
            {
                var collection = await LoadCollectionAsync(collectionName);

                if (collection?.SearchPatterns != null)
                {
                    var names = collection.SearchPatterns.Keys.ToList();
                    _logger.LogDebug("Retrieved {Count} pattern names from collection '{CollectionName}'",
                        names.Count, collectionName);
                    return names;
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting pattern names from collection '{CollectionName}'", collectionName);
                return new List<string>();
            }
        }

        #endregion

        #region Save Operations

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

                // Ensure Collections is never null
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

        /// <summary>
        /// Save or update a specific pattern collection
        /// Creates or updates the collection in the library
        /// </summary>
        public async Task<bool> SaveCollectionAsync(string collectionName, PatternCollection collection)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || collection == null)
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                // Ensure collection has proper name and SearchPatterns
                collection.Name = collectionName;
                collection.SearchPatterns ??= new Dictionary<string, SearchPattern>();

                // Add or update the collection
                library.Collections[collectionName] = collection;

                var success = await SaveLibraryAsync(library);

                if (success)
                {
                    _logger.LogInformation("Saved collection '{CollectionName}' with {PatternCount} patterns",
                        collectionName, collection.SearchPatterns.Count);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving collection '{CollectionName}'", collectionName);
                return false;
            }
        }

        /// <summary>
        /// Save or update a specific search pattern within a collection
        /// Creates the collection if it doesn't exist
        /// </summary>
        public async Task<bool> SaveSearchPatternAsync(string collectionName, string patternName, SearchPattern pattern)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(patternName) || pattern == null)
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                // Get or create the collection
                if (!library.Collections.TryGetValue(collectionName, out var collection))
                {
                    collection = new PatternCollection
                    {
                        Name = collectionName,
                        SearchPatterns = new Dictionary<string, SearchPattern>()
                    };
                    library.Collections[collectionName] = collection;
                }

                // Ensure SearchPatterns is not null
                collection.SearchPatterns ??= new Dictionary<string, SearchPattern>();

                // Ensure pattern has proper name
                pattern.SearchName = patternName;

                // Add or update the pattern
                collection.SearchPatterns[patternName] = pattern;

                var success = await SaveLibraryAsync(library);

                if (success)
                {
                    _logger.LogInformation("Saved pattern '{PatternName}' in collection '{CollectionName}'",
                        patternName, collectionName);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving pattern '{PatternName}' in collection '{CollectionName}'",
                    patternName, collectionName);
                return false;
            }
        }

        #endregion

        #region Delete Operations

        /// <summary>
        /// Delete a complete collection from the library
        /// Returns true if deleted, false if not found
        /// </summary>
        public async Task<bool> DeleteCollectionAsync(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.Remove(collectionName))
                {
                    var success = await SaveLibraryAsync(library);

                    if (success)
                    {
                        _logger.LogInformation("Deleted collection '{CollectionName}'", collectionName);
                    }

                    return success;
                }

                _logger.LogDebug("Collection '{CollectionName}' not found for deletion", collectionName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting collection '{CollectionName}'", collectionName);
                return false;
            }
        }

        /// <summary>
        /// Delete a specific search pattern from a collection
        /// Returns true if deleted, false if not found
        /// </summary>
        public async Task<bool> DeleteSearchPatternAsync(string collectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(patternName))
                return false;

            try
            {
                var library = await LoadLibraryAsync();

                if (library.Collections.TryGetValue(collectionName, out var collection) &&
                    collection.SearchPatterns?.Remove(patternName) == true)
                {
                    var success = await SaveLibraryAsync(library);

                    if (success)
                    {
                        _logger.LogInformation("Deleted pattern '{PatternName}' from collection '{CollectionName}'",
                            patternName, collectionName);
                    }

                    return success;
                }

                _logger.LogDebug("Pattern '{PatternName}' not found in collection '{CollectionName}' for deletion",
                    patternName, collectionName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting pattern '{PatternName}' from collection '{CollectionName}'",
                    patternName, collectionName);
                return false;
            }
        }

        #endregion

        #region Utility Operations

        /// <summary>
        /// Check if a collection exists in the library
        /// </summary>
        public async Task<bool> CollectionExistsAsync(string collectionName)
        {
            if (string.IsNullOrWhiteSpace(collectionName))
                return false;

            try
            {
                var library = await LoadLibraryAsync();
                return library.Collections.ContainsKey(collectionName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if collection '{CollectionName}' exists", collectionName);
                return false;
            }
        }

        /// <summary>
        /// Check if a search pattern exists in a specific collection
        /// </summary>
        public async Task<bool> SearchPatternExistsAsync(string collectionName, string patternName)
        {
            if (string.IsNullOrWhiteSpace(collectionName) || string.IsNullOrWhiteSpace(patternName))
                return false;

            try
            {
                var collection = await LoadCollectionAsync(collectionName);
                return collection?.SearchPatterns?.ContainsKey(patternName) == true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if pattern '{PatternName}' exists in collection '{CollectionName}'",
                    patternName, collectionName);
                return false;
            }
        }

        /// <summary>
        /// Create and save an empty pattern library
        /// Useful for initialization
        /// </summary>
        public async Task<bool> CreateEmptyLibraryAsync()
        {
            try
            {
                var emptyLibrary = CreateEmptyLibrary();
                var success = await SaveLibraryAsync(emptyLibrary);

                if (success)
                {
                    _logger.LogInformation("Created empty pattern library");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating empty library");
                return false;
            }
        }

        /// <summary>
        /// Export a collection for backup or sharing
        /// Returns the collection object that can be serialized separately
        /// </summary>
        public async Task<PatternCollection?> ExportCollectionAsync(string collectionName)
        {
            try
            {
                var collection = await LoadCollectionAsync(collectionName);

                if (collection != null)
                {
                    _logger.LogInformation("Exported collection '{CollectionName}' with {PatternCount} patterns",
                        collectionName, collection.SearchPatterns?.Count ?? 0);
                }

                return collection;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting collection '{CollectionName}'", collectionName);
                return null;
            }
        }

        /// <summary>
        /// Import a collection from backup or sharing
        /// Overwrites existing collection if it exists
        /// </summary>
        public async Task<bool> ImportCollectionAsync(PatternCollection collection)
        {
            if (collection == null || string.IsNullOrWhiteSpace(collection.Name))
                return false;

            try
            {
                var success = await SaveCollectionAsync(collection.Name, collection);

                if (success)
                {
                    _logger.LogInformation("Imported collection '{CollectionName}' with {PatternCount} patterns",
                        collection.Name, collection.SearchPatterns?.Count ?? 0);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing collection '{CollectionName}'", collection.Name);
                return false;
            }
        }

        #endregion

        #region Private Helpers

        /// <summary>
        /// Create an empty pattern library
        /// </summary>
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
        }

        #endregion
    }
}