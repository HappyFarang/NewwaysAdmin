// NewwaysAdmin.WebAdmin/Services/BankSlips/DocumentParser.cs
// 🚀 PHASE 2: Final version using SpatialPatternMatcher + SpatialResultParser

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Services.Ocr;
using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    /// <summary>
    /// Modern document parser - transforms spatial OCR data into flexible dictionary data
    /// 🔧 PHASE 2: Now uses SpatialPatternMatcher + SpatialResultParser (same algorithms as Settings)
    /// </summary>
    public class DocumentParser
    {
        private readonly ISpatialOcrService _spatialOcrService;
        private readonly PatternManagementService _patternManagement;
        private readonly SpatialPatternMatcher _spatialPatternMatcher;
        private readonly SpatialResultParser _spatialResultParser;
        private readonly ILogger<DocumentParser> _logger;

        public DocumentParser(
            ISpatialOcrService spatialOcrService,
            PatternManagementService patternManagement,
            SpatialPatternMatcher spatialPatternMatcher,
            SpatialResultParser spatialResultParser,
            ILogger<DocumentParser> logger)
        {
            _spatialOcrService = spatialOcrService ?? throw new ArgumentNullException(nameof(spatialOcrService));
            _patternManagement = patternManagement ?? throw new ArgumentNullException(nameof(patternManagement));
            _spatialPatternMatcher = spatialPatternMatcher ?? throw new ArgumentNullException(nameof(spatialPatternMatcher));
            _spatialResultParser = spatialResultParser ?? throw new ArgumentNullException(nameof(spatialResultParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Parse image using spatial OCR processing (same algorithms as Settings → OCR Analyzer)
        /// </summary>
        /// <param name="ocrText">Raw OCR text (not used anymore - we extract spatial data instead)</param>
        /// <param name="imagePath">Full path to the source image file</param>
        /// <param name="documentType">Document type (e.g., "BankSlips")</param>
        /// <param name="formatName">Format name (e.g., "KBIZ", "KBank")</param>
        /// <returns>Dictionary with extracted fields + FileName</returns>
        public async Task<Dictionary<string, string>> ParseAsync(
            string ocrText, // Keep for compatibility but we'll extract our own spatial data
            string imagePath,
            string documentType,
            string formatName)
        {
            var result = new Dictionary<string, string>();
            var fileName = Path.GetFileName(imagePath);

            try
            {
                // Always add filename for tracking/debugging
                result["FileName"] = fileName;

                _logger.LogDebug("🚀 Starting spatial document parsing for {FileName} using {DocumentType}/{FormatName}",
                    fileName, documentType, formatName);

                // STEP 1: Extract spatial data using the SAME service as Settings → OCR Analyzer
                _logger.LogDebug("🔍 Extracting spatial OCR data...");
                var spatialResult = await _spatialOcrService.ExtractSpatialTextAsync(imagePath);

                if (!spatialResult.Success || spatialResult.Document == null)
                {
                    _logger.LogWarning("Spatial OCR extraction failed for {FileName}: {Error}",
                        fileName, spatialResult.ErrorMessage);

                    // Add all pattern fields as "Missing Pattern" so they show up in UI
                    await AddMissingFieldsAsync(result, documentType, formatName);
                    return result;
                }

                _logger.LogDebug("✅ Spatial OCR extracted {WordCount} words from {FileName}",
                    spatialResult.Document.Words.Count, fileName);

                // STEP 2: Load patterns for this document type/format
                var patterns = await LoadPatternsForFormatAsync(documentType, formatName);
                if (patterns.Count == 0)
                {
                    _logger.LogWarning("No patterns found for {DocumentType}/{FormatName}", documentType, formatName);
                    result["Error"] = "No patterns configured";
                    return result;
                }

                _logger.LogDebug("Found {PatternCount} patterns for {DocumentType}/{FormatName}",
                    patterns.Count, documentType, formatName);

                // STEP 3: Process each pattern using the shared SpatialPatternMatcher
                var spatialResults = new Dictionary<string, SpatialPatternResult>();
                var successCount = 0;

                foreach (var (patternKey, pattern) in patterns)
                {
                    try
                    {
                        _logger.LogDebug("🔄 Processing pattern '{PatternKey}' for {FileName}", patternKey, fileName);

                        // Use the shared SpatialPatternMatcher (same algorithms as Settings)
                        var spatialPatternResult = _spatialPatternMatcher.ExtractPattern(
                            document: spatialResult.Document,
                            searchTerm: pattern.KeyWord,
                            patternType: pattern.PatternType,
                            yTolerance: pattern.ToleranceY,
                            xTolerance: pattern.ToleranceX,
                            stopWords: pattern.StopWords ?? "",
                            patternName: pattern.SearchName
                        );

                        spatialResults[patternKey] = spatialPatternResult;

                        if (spatialPatternResult.Success)
                        {
                            successCount++;
                            _logger.LogDebug("✅ Spatial extraction succeeded for pattern '{PatternKey}' on {FileName}",
                                patternKey, fileName);
                        }
                        else
                        {
                            _logger.LogDebug("❌ Spatial extraction failed for pattern '{PatternKey}' on {FileName}",
                                patternKey, fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "💥 Error processing spatial pattern '{PatternKey}' for {FileName}",
                            patternKey, fileName);

                        // Create empty result for failed pattern
                        spatialResults[patternKey] = new SpatialPatternResult { Success = false };
                    }
                }

                _logger.LogDebug("🔄 Spatial extraction completed: {SuccessCount}/{TotalCount} patterns successful",
                    successCount, patterns.Count);

                // STEP 4: Process spatial results through regex patterns using SpatialResultParser
                _logger.LogDebug("🧠 Processing spatial results through regex patterns...");
                var finalResults = _spatialResultParser.ProcessMultipleResults(spatialResults, patterns, fileName);

                // STEP 5: Combine final results with metadata
                foreach (var kvp in finalResults)
                {
                    result[kvp.Key] = kvp.Value;
                }

                // Add processing metadata
                result["ProcessedPatterns"] = patterns.Count.ToString();
                result["SuccessfulPatterns"] = finalResults.Values.Count(v => !v.StartsWith("Missing") && v != "Error").ToString();

                var finalSuccessCount = finalResults.Values.Count(v => !v.StartsWith("Missing") && v != "Error");
                _logger.LogInformation("🎉 Document parsing completed for {FileName}: {FinalSuccessCount}/{TotalCount} patterns extracted successfully",
                    fileName, finalSuccessCount, patterns.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error parsing document {FileName}", fileName);

                // Add all pattern fields as "Error" on exception
                await AddMissingFieldsAsync(result, documentType, formatName, "Error");
                result["Error"] = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Load patterns for a specific document type and format
        /// </summary>
        private async Task<Dictionary<string, SearchPattern>> LoadPatternsForFormatAsync(string documentType, string formatName)
        {
            try
            {
                var patterns = new Dictionary<string, SearchPattern>();
                var patternNames = await _patternManagement.GetSearchPatternNamesAsync(documentType, formatName);

                foreach (var patternName in patternNames)
                {
                    var pattern = await _patternManagement.LoadSearchPatternAsync(documentType, formatName, patternName);
                    if (pattern != null)
                    {
                        patterns[patternName] = pattern;
                    }
                }

                return patterns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading patterns for {DocumentType}/{FormatName}", documentType, formatName);
                return new Dictionary<string, SearchPattern>();
            }
        }

        /// <summary>
        /// Add all pattern fields as "Missing Pattern" when no patterns can be processed
        /// </summary>
        private async Task AddMissingFieldsAsync(Dictionary<string, string> result, string documentType, string formatName, string defaultValue = "Missing Pattern")
        {
            try
            {
                var patterns = await LoadPatternsForFormatAsync(documentType, formatName);
                foreach (var patternKey in patterns.Keys)
                {
                    if (!result.ContainsKey(patternKey))
                    {
                        result[patternKey] = defaultValue;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding missing fields for {DocumentType}/{FormatName}", documentType, formatName);
            }
        }
    }
}