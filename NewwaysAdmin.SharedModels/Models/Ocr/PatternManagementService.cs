// NewwaysAdmin.SharedModels/Services/Ocr/PatternManagementService.cs
using NewwaysAdmin.SharedModels.Models.Ocr.Patterns;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Simple service for loading and saving OCR pattern collections
    /// Uses the storage system to persist PatternLibrary as JSON
    /// </summary>
    public class PatternManagementService
    {
        private readonly IDataStorage<PatternLibrary> _storage;

        public PatternManagementService(IDataStorage<PatternLibrary> storage)
        {
            _storage = storage;
        }

        /// <summary>
        /// Get a specific pattern collection by name (e.g., "KBIZ", "KBank")
        /// Returns null if the collection doesn't exist
        /// </summary>
        /// <param name="collectionName">Name of the collection to load</param>
        /// <returns>PatternCollection or null if not found</returns>
        public async Task<PatternCollection?> GetCollectionAsync(string collectionName)
        {
            var library = await LoadLibraryAsync();
            return library.Collections.GetValueOrDefault(collectionName);
        }

        /// <summary>
        /// Save a pattern collection back to the library
        /// Creates or updates the collection in the library
        /// </summary>
        /// <param name="collection">Collection to save</param>
        public async Task SaveCollectionAsync(PatternCollection collection)
        {
            var library = await LoadLibraryAsync();
            library.Collections[collection.Name] = collection;
            await _storage.SaveAsync("pattern-library", library);
        }

        /// <summary>
        /// Get list of all available collection names
        /// </summary>
        /// <returns>List of collection names</returns>
        public async Task<List<string>> GetCollectionNamesAsync()
        {
            var library = await LoadLibraryAsync();
            return library.Collections.Keys.ToList();
        }

        /// <summary>
        /// Delete a collection from the library
        /// </summary>
        /// <param name="collectionName">Name of collection to delete</param>
        /// <returns>True if deleted, false if not found</returns>
        public async Task<bool> DeleteCollectionAsync(string collectionName)
        {
            var library = await LoadLibraryAsync();

            if (library.Collections.Remove(collectionName))
            {
                await _storage.SaveAsync("pattern-library", library);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a collection exists
        /// </summary>
        /// <param name="collectionName">Name to check</param>
        /// <returns>True if collection exists</returns>
        public async Task<bool> CollectionExistsAsync(string collectionName)
        {
            var library = await LoadLibraryAsync();
            return library.Collections.ContainsKey(collectionName);
        }

        /// <summary>
        /// Load the complete pattern library from storage
        /// Returns empty library if file doesn't exist or can't be loaded
        /// </summary>
        private async Task<PatternLibrary> LoadLibraryAsync()
        {
            try
            {
                var library = await _storage.LoadAsync("pattern-library");

                // Initialize empty library if null
                if (library == null)
                {
                    library = new PatternLibrary
                    {
                        Collections = new Dictionary<string, Models.Ocr.Patterns.PatternCollection>
                }

                // Ensure Collections dictionary is not null
                library.Collections ??= new Dictionary<string, PatternCollection>();

                return library;
            }
            catch
            {
                // Return empty library if anything goes wrong
                return new PatternLibrary
                {
                    Collections = new Dictionary<string, PatternCollection>()
                };
            }
        }
    }
}