// NewwaysAdmin.SharedModels/Models/Ocr/Core/PatternLoaderService.cs
// Pure C# service for loading patterns - no UI dependencies

using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.SharedModels.Services.Ocr;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Service for loading OCR patterns with hierarchical navigation
    /// Can be used by UI components, automated systems, background services, etc.
    /// </summary>
    public class PatternLoaderService
    {
        private readonly PatternManagementService _patternService;
        private readonly ILogger<PatternLoaderService> _logger;

        public PatternLoaderService(
            PatternManagementService patternService,
            ILogger<PatternLoaderService> logger)
        {
            _patternService = patternService ?? throw new ArgumentNullException(nameof(patternService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Hierarchy Navigation

        /// <summary>
        /// Get all available document types (Level 1: BankSlips, Invoices, etc.)
        /// </summary>
        public async Task<List<string>> GetDocumentTypesAsync()
        {
            try
            {
                var documentTypes = await _patternService.GetCollectionNamesAsync();
                _logger.LogDebug("Retrieved {Count} document types", documentTypes.Count);
                return documentTypes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting document types");
                return new List<string>();
            }
        }

        /// <summary>
        /// Get sub-collections for a specific document type (Level 2: KBIZ, KBank, etc.)
        /// </summary>
        public async Task<List<string>> GetSubCollectionsAsync(string documentType)
        {
            if (string.IsNullOrWhiteSpace(documentType))
            {
                _logger.LogWarning("GetSubCollectionsAsync called with empty document type");
                return new List<string>();
            }

            try
            {
                var subCollections = await _patternService.GetSubCollectionNamesAsync(documentType);
                _logger.LogDebug("Retrieved {Count} sub-collections for document type '{DocumentType}'",
                    subCollections.Count, documentType);
                return subCollections;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sub-collections for document type '{DocumentType}'", documentType);
                return new List<string>();
            }
        }

        /// <summary>
        /// Get pattern names for a specific document type and sub-collection (Level 3: Date, Total, etc.)
        /// </summary>
        public async Task<List<string>> GetPatternNamesAsync(string documentType, string subCollection)
        {
            if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(subCollection))
            {
                _logger.LogWarning("GetPatternNamesAsync called with empty parameters: documentType='{DocumentType}', subCollection='{SubCollection}'",
                    documentType, subCollection);
                return new List<string>();
            }

            try
            {
                var patterns = await _patternService.GetSearchPatternNamesAsync(documentType, subCollection);
                _logger.LogDebug("Retrieved {Count} patterns for '{DocumentType}.{SubCollection}'",
                    patterns.Count, documentType, subCollection);
                return patterns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting patterns for '{DocumentType}.{SubCollection}'",
                    documentType, subCollection);
                return new List<string>();
            }
        }

        #endregion

        #region Pattern Loading

        /// <summary>
        /// Load a specific pattern by its full path
        /// </summary>
        public async Task<SearchPattern?> LoadPatternAsync(string documentType, string subCollection, string patternName)
        {
            if (string.IsNullOrWhiteSpace(documentType) ||
                string.IsNullOrWhiteSpace(subCollection) ||
                string.IsNullOrWhiteSpace(patternName))
            {
                _logger.LogWarning("LoadPatternAsync called with empty parameters: documentType='{DocumentType}', subCollection='{SubCollection}', patternName='{PatternName}'",
                    documentType, subCollection, patternName);
                return null;
            }

            try
            {
                var pattern = await _patternService.LoadSearchPatternAsync(documentType, subCollection, patternName);

                if (pattern != null)
                {
                    _logger.LogInformation("Successfully loaded pattern '{PatternName}' from '{DocumentType}.{SubCollection}'",
                        patternName, documentType, subCollection);
                }
                else
                {
                    _logger.LogWarning("Pattern '{PatternName}' not found in '{DocumentType}.{SubCollection}'",
                        patternName, documentType, subCollection);
                }

                return pattern;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading pattern '{PatternName}' from '{DocumentType}.{SubCollection}'",
                    patternName, documentType, subCollection);
                return null;
            }
        }

        /// <summary>
        /// Check if a pattern exists without loading it
        /// </summary>
        public async Task<bool> PatternExistsAsync(string documentType, string subCollection, string patternName)
        {
            if (string.IsNullOrWhiteSpace(documentType) ||
                string.IsNullOrWhiteSpace(subCollection) ||
                string.IsNullOrWhiteSpace(patternName))
            {
                return false;
            }

            try
            {
                return await _patternService.SearchPatternExistsAsync(documentType, subCollection, patternName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if pattern exists: '{DocumentType}.{SubCollection}.{PatternName}'",
                    documentType, subCollection, patternName);
                return false;
            }
        }

        #endregion

        #region Batch Operations (for automated systems)

        /// <summary>
        /// Load all patterns for a specific document type and sub-collection
        /// Useful for automated processing systems
        /// </summary>
        public async Task<Dictionary<string, SearchPattern>> LoadAllPatternsAsync(string documentType, string subCollection)
        {
            var result = new Dictionary<string, SearchPattern>();

            if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(subCollection))
            {
                _logger.LogWarning("LoadAllPatternsAsync called with empty parameters");
                return result;
            }

            try
            {
                var patternNames = await GetPatternNamesAsync(documentType, subCollection);

                foreach (var patternName in patternNames)
                {
                    var pattern = await LoadPatternAsync(documentType, subCollection, patternName);
                    if (pattern != null)
                    {
                        result[patternName] = pattern;
                    }
                }

                _logger.LogInformation("Loaded {Count} patterns from '{DocumentType}.{SubCollection}'",
                    result.Count, documentType, subCollection);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading all patterns from '{DocumentType}.{SubCollection}'",
                    documentType, subCollection);
                return result;
            }
        }

        /// <summary>
        /// Load all patterns for a specific document type (all sub-collections)
        /// Useful for comprehensive automated processing
        /// </summary>
        public async Task<Dictionary<string, Dictionary<string, SearchPattern>>> LoadAllPatternsForDocumentTypeAsync(string documentType)
        {
            var result = new Dictionary<string, Dictionary<string, SearchPattern>>();

            if (string.IsNullOrWhiteSpace(documentType))
            {
                _logger.LogWarning("LoadAllPatternsForDocumentTypeAsync called with empty document type");
                return result;
            }

            try
            {
                var subCollections = await GetSubCollectionsAsync(documentType);

                foreach (var subCollection in subCollections)
                {
                    var patterns = await LoadAllPatternsAsync(documentType, subCollection);
                    if (patterns.Any())
                    {
                        result[subCollection] = patterns;
                    }
                }

                var totalPatterns = result.Values.Sum(sc => sc.Count);
                _logger.LogInformation("Loaded {TotalPatterns} patterns from {SubCollectionCount} sub-collections for document type '{DocumentType}'",
                    totalPatterns, result.Count, documentType);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading all patterns for document type '{DocumentType}'", documentType);
                return result;
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get the complete hierarchy structure for inspection/debugging
        /// </summary>
        public async Task<PatternHierarchyInfo> GetCompleteHierarchyAsync()
        {
            try
            {
                var hierarchy = new PatternHierarchyInfo();
                var documentTypes = await GetDocumentTypesAsync();

                foreach (var docType in documentTypes)
                {
                    var docTypeInfo = new DocumentTypeInfo { Name = docType };
                    var subCollections = await GetSubCollectionsAsync(docType);

                    foreach (var subCollection in subCollections)
                    {
                        var subCollectionInfo = new SubCollectionInfo { Name = subCollection };
                        var patterns = await GetPatternNamesAsync(docType, subCollection);
                        subCollectionInfo.PatternNames = patterns;
                        docTypeInfo.SubCollections.Add(subCollectionInfo);
                    }

                    hierarchy.DocumentTypes.Add(docTypeInfo);
                }

                _logger.LogInformation("Retrieved complete hierarchy: {DocumentTypeCount} document types", hierarchy.DocumentTypes.Count);
                return hierarchy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete hierarchy");
                return new PatternHierarchyInfo();
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Represents the complete pattern hierarchy for inspection
    /// </summary>
    public class PatternHierarchyInfo
    {
        public List<DocumentTypeInfo> DocumentTypes { get; set; } = new();
    }

    public class DocumentTypeInfo
    {
        public string Name { get; set; } = "";
        public List<SubCollectionInfo> SubCollections { get; set; } = new();
    }

    public class SubCollectionInfo
    {
        public string Name { get; set; } = "";
        public List<string> PatternNames { get; set; } = new();
    }

    #endregion
}