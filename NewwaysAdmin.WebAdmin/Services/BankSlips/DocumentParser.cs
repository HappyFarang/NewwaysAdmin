// NewwaysAdmin.WebAdmin/Services/BankSlips/DocumentParser.cs
// 🚀 FIXED: Now uses SpatialOcrService like the Settings → OCR Analyzer

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Services.Ocr;
using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;
using System.Text.RegularExpressions;
using NewwaysAdmin.SharedModels.Models.Documents;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    /// <summary>
    /// Modern document parser - transforms spatial OCR data into flexible dictionary data
    /// 🔧 FIXED: Now uses the same SpatialOcrService as Settings → OCR Analyzer
    /// </summary>
    public class DocumentParser
    {
        private readonly ISpatialOcrService _spatialOcrService; // 🔧 CHANGED: From PatternLoaderService to ISpatialOcrService
        private readonly PatternManagementService _patternManagement;
        private readonly ILogger<DocumentParser> _logger;

        public DocumentParser(
            ISpatialOcrService spatialOcrService, // 🔧 CHANGED: Use the same service as Settings
            PatternManagementService patternManagement,
            ILogger<DocumentParser> logger)
        {
            _spatialOcrService = spatialOcrService ?? throw new ArgumentNullException(nameof(spatialOcrService));
            _patternManagement = patternManagement ?? throw new ArgumentNullException(nameof(patternManagement));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Parse image using spatial OCR processing (same as Settings → OCR Analyzer)
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

            try
            {
                // Always add filename for tracking/debugging
                result["FileName"] = Path.GetFileName(imagePath);

                _logger.LogDebug("Starting spatial document parsing for {FileName} using {DocumentType}/{FormatName}",
                    result["FileName"], documentType, formatName);

                // 🔧 STEP 1: Extract spatial data using the SAME service as Settings → OCR Analyzer
                _logger.LogDebug("🔍 Extracting spatial OCR data...");
                var spatialResult = await _spatialOcrService.ExtractSpatialTextAsync(imagePath);

                if (!spatialResult.Success || spatialResult.Document == null)
                {
                    _logger.LogWarning("Spatial OCR extraction failed for {FileName}: {Error}",
                        result["FileName"], spatialResult.ErrorMessage);

                    // Add all pattern fields as "Missing" so they show up in UI
                    await AddMissingFieldsAsync(result, documentType, formatName);
                    return result;
                }

                _logger.LogDebug("✅ Spatial OCR extracted {WordCount} words from {FileName}",
                    spatialResult.Document.Words.Count, result["FileName"]);

                // 🔧 STEP 2: Load patterns for this document type/format
                var patterns = await LoadPatternsForFormatAsync(documentType, formatName);
                if (patterns.Count == 0)
                {
                    _logger.LogWarning("No patterns found for {DocumentType}/{FormatName}", documentType, formatName);
                    return result;
                }

                _logger.LogDebug("Found {PatternCount} patterns for {DocumentType}/{FormatName}",
                    patterns.Count, documentType, formatName);

                // 🔧 STEP 3: Process each pattern using spatial matching (same as Settings)
                var successCount = 0;
                var extractedFields = new List<string>();
                var missingFields = new List<string>();

                foreach (var (patternKey, pattern) in patterns)
                {
                    try
                    {
                        var extractedValue = await ExtractValueUsingSpatialPatternAsync(
                            spatialResult.Document, pattern, result["FileName"]);

                        if (!string.IsNullOrWhiteSpace(extractedValue))
                        {
                            result[patternKey] = extractedValue;
                            extractedFields.Add(patternKey);
                            successCount++;
                            _logger.LogDebug("✅ Pattern '{PatternKey}' extracted: '{Value}'",
                                patternKey, extractedValue.Length > 50 ? $"{extractedValue[..50]}..." : extractedValue);
                        }
                        else
                        {
                            result[patternKey] = "Missing"; // 🔧 Mark as Missing for debugging
                            missingFields.Add(patternKey);
                            _logger.LogDebug("❌ Pattern '{PatternKey}' marked as Missing", patternKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        result[patternKey] = "Missing";
                        missingFields.Add(patternKey);
                        _logger.LogWarning(ex, "❌ Pattern '{PatternKey}' error - marked as Missing: {Error}",
                            patternKey, ex.Message);
                    }
                }

                // 🔧 STEP 4: Log results (same format as PatternLoaderService for consistency)
                if (extractedFields.Any())
                {
                    _logger.LogInformation("✅ {FileName}: Extracted {SuccessCount}/{TotalCount} patterns: {ExtractedFields}",
                        result["FileName"], successCount, patterns.Count, string.Join(", ", extractedFields));
                }

                if (missingFields.Any())
                {
                    _logger.LogWarning("❌ {FileName}: Missing {MissingCount}/{TotalCount} patterns: {MissingFields}",
                        result["FileName"], missingFields.Count, patterns.Count, string.Join(", ", missingFields));
                }

                _logger.LogInformation("🎉 Spatial pattern extraction completed for {DocumentType}/{Format}: " +
                    "{SuccessCount}/{TotalCount} successful, {MissingCount} missing",
                    documentType, formatName, successCount, patterns.Count, missingFields.Count);

                _logger.LogDebug("Spatial document parsing completed for {FileName}: {FieldCount} fields extracted",
                    result["FileName"], result.Count - 1); // -1 for FileName

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing document {FileName}", result["FileName"]);

                // Add all pattern fields as "Missing" on error
                await AddMissingFieldsAsync(result, documentType, formatName);
                return result;
            }
        }

        /// <summary>
        /// Extract value using spatial pattern matching (same logic as Settings → OCR Analyzer)
        /// </summary>
        private async Task<string> ExtractValueUsingSpatialPatternAsync(
            SpatialDocument document,
            SearchPattern pattern,
            string fileName)
        {
            try
            {
                _logger.LogDebug("🔍 Starting spatial pattern extraction for '{PatternName}' with keyword '{KeyWord}' on {FileName}",
                    pattern.SearchName, pattern.KeyWord, fileName);

                // Find the keyword word in the spatial document
                var keywordWord = document.Words.FirstOrDefault(w =>
                    w.Text.Contains(pattern.KeyWord, StringComparison.OrdinalIgnoreCase));

                if (keywordWord == null)
                {
                    _logger.LogWarning("❌ Keyword '{KeyWord}' not found in spatial document for pattern '{PatternName}' on {FileName}",
                        pattern.KeyWord, pattern.SearchName, fileName);
                    return string.Empty;
                }

                _logger.LogDebug("✅ Keyword '{KeyWord}' found at position ({X},{Y}) for pattern '{PatternName}' on {FileName}",
                    pattern.KeyWord, keywordWord.NormX1, keywordWord.NormY1, pattern.SearchName, fileName);

                // Extract text based on pattern type using spatial coordinates
                var candidateWords = pattern.PatternType.ToLower() switch
                {
                    "verticalcolumn" => GetWordsInVerticalColumn(document.Words, keywordWord, pattern),
                    "horizontal" => GetWordsInHorizontalLine(document.Words, keywordWord, pattern),
                    _ => new List<WordBoundingBox>()
                };

                if (!candidateWords.Any())
                {
                    _logger.LogWarning("❌ No candidate words found using {PatternType} method for pattern '{PatternName}' on {FileName}",
                        pattern.PatternType, pattern.SearchName, fileName);
                    return string.Empty;
                }

                // Combine candidate words into text
                var extractedText = string.Join(" ", candidateWords.Select(w => w.Text)).Trim();

                _logger.LogDebug("📝 Extracted text for pattern '{PatternName}' on {FileName}: '{ExtractedText}'",
                    pattern.SearchName, fileName, extractedText);

                // Apply regex patterns if specified
                var finalValue = ApplyRegexPatterns(extractedText, pattern.RegexPatterns, pattern.SearchName, fileName);

                // Clean up stop words
                if (!string.IsNullOrWhiteSpace(pattern.StopWords))
                {
                    var beforeStopWords = finalValue;
                    finalValue = RemoveStopWords(finalValue, pattern.StopWords);
                    if (beforeStopWords != finalValue)
                    {
                        _logger.LogDebug("🧹 Stop words removed for pattern '{PatternName}' on {FileName}: '{Before}' → '{After}'",
                            pattern.SearchName, fileName, beforeStopWords, finalValue);
                    }
                }

                if (!string.IsNullOrWhiteSpace(finalValue))
                {
                    _logger.LogInformation("🎉 Pattern '{PatternName}' successfully extracted from {FileName}: '{FinalValue}'",
                        pattern.SearchName, fileName, finalValue);
                }

                return finalValue.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error extracting spatial value for pattern '{PatternName}' on {FileName}: {Error}",
                    pattern.SearchName, fileName, ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Get words in vertical column below the keyword (with tolerance)
        /// </summary>
        private List<WordBoundingBox> GetWordsInVerticalColumn(List<WordBoundingBox> allWords, WordBoundingBox keywordWord, SearchPattern pattern)
        {
            var tolerance = pattern.ToleranceY / 1000.0f; // Convert to normalized coordinates
            var horizontalTolerance = pattern.ToleranceX / 1000.0f;

            return allWords
                .Where(w => w != keywordWord) // Exclude the keyword itself
                .Where(w => w.NormY1 > keywordWord.NormY2) // Below the keyword
                .Where(w => Math.Abs(w.NormX1 - keywordWord.NormX1) <= horizontalTolerance) // Same column
                .Where(w => w.NormY1 <= keywordWord.NormY2 + tolerance) // Within tolerance
                .OrderBy(w => w.NormY1) // Top to bottom
                .Take(3) // Limit results
                .ToList();
        }

        /// <summary>
        /// Get words in horizontal line after the keyword (with tolerance)
        /// </summary>
        private List<WordBoundingBox> GetWordsInHorizontalLine(List<WordBoundingBox> allWords, WordBoundingBox keywordWord, SearchPattern pattern)
        {
            var tolerance = pattern.ToleranceY / 1000.0f;
            var horizontalTolerance = pattern.ToleranceX / 1000.0f;

            return allWords
                .Where(w => w != keywordWord) // Exclude the keyword itself
                .Where(w => w.NormX1 > keywordWord.NormX2) // To the right of keyword
                .Where(w => Math.Abs(w.NormY1 - keywordWord.NormY1) <= tolerance) // Same line
                .Where(w => w.NormX1 <= keywordWord.NormX2 + horizontalTolerance) // Within tolerance
                .OrderBy(w => w.NormX1) // Left to right
                .Take(3) // Limit results
                .ToList();
        }

        /// <summary>
        /// Apply regex patterns to extracted text
        /// </summary>
        private string ApplyRegexPatterns(string text, List<string> regexPatterns, string patternName, string fileName)
        {
            if (regexPatterns == null || regexPatterns.Count == 0)
            {
                _logger.LogDebug("No regex patterns defined for '{PatternName}' on {FileName}, returning original text", patternName, fileName);
                return text;
            }

            _logger.LogDebug("🔄 Applying {Count} regex patterns to text '{Text}' for pattern '{PatternName}' on {FileName}",
                regexPatterns.Count, text, patternName, fileName);

            foreach (var regexPattern in regexPatterns)
            {
                try
                {
                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    var match = regex.Match(text);

                    if (match.Success)
                    {
                        var result = match.Value;
                        _logger.LogInformation("✅ Regex pattern matched for '{PatternName}' on {FileName}: '{Pattern}' → '{Result}'",
                            patternName, fileName, regexPattern, result);
                        return result;
                    }
                    else
                    {
                        _logger.LogDebug("❌ Regex pattern '{Pattern}' did not match text '{Text}' for pattern '{PatternName}' on {FileName}",
                            regexPattern, text, patternName, fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "💥 Invalid regex pattern for '{PatternName}' on {FileName}: '{Pattern}' - {Error}",
                        patternName, fileName, regexPattern, ex.Message);
                }
            }

            _logger.LogWarning("❌ No regex patterns matched for '{PatternName}' on {FileName}, returning original text", patternName, fileName);
            return text;
        }

        /// <summary>
        /// Remove stop words from extracted text
        /// </summary>
        private string RemoveStopWords(string text, string stopWords)
        {
            if (string.IsNullOrWhiteSpace(stopWords))
                return text;

            var stopWordList = stopWords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .Where(w => !string.IsNullOrEmpty(w));

            var result = text;
            foreach (var stopWord in stopWordList)
            {
                result = result.Replace(stopWord, "", StringComparison.OrdinalIgnoreCase);
            }

            return result.Trim();
        }

        /// <summary>
        /// Load patterns for the specified document format
        /// </summary>
        private async Task<Dictionary<string, SearchPattern>> LoadPatternsForFormatAsync(string documentType, string format)
        {
            try
            {
                var library = await _patternManagement.LoadLibraryAsync();

                if (library.Collections.TryGetValue(documentType, out var collection) &&
                    collection.SubCollections.TryGetValue(format, out var subCollection))
                {
                    return subCollection.SearchPatterns;
                }

                _logger.LogWarning("No patterns found for {DocumentType}/{Format}", documentType, format);
                return new Dictionary<string, SearchPattern>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading patterns for {DocumentType}/{Format}", documentType, format);
                return new Dictionary<string, SearchPattern>();
            }
        }

        /// <summary>
        /// Add all pattern fields as "Missing" when extraction fails
        /// </summary>
        private async Task AddMissingFieldsAsync(Dictionary<string, string> result, string documentType, string formatName)
        {
            try
            {
                var patterns = await LoadPatternsForFormatAsync(documentType, formatName);
                foreach (var patternKey in patterns.Keys)
                {
                    if (!result.ContainsKey(patternKey))
                    {
                        result[patternKey] = "Missing";
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not add missing fields for {DocumentType}/{FormatName}", documentType, formatName);
            }
        }
    }
}