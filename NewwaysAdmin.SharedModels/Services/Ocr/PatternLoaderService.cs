// NewwaysAdmin.SharedModels/Services/Ocr/PatternLoaderService.cs
using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.SharedModels.Models.Documents;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Linq;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Pure business logic service for pattern-based OCR text extraction
    /// Loads patterns from storage and applies them to OCR text to extract structured data
    /// </summary>
    public class PatternLoaderService
    {
        private readonly PatternManagementService _patternManagement;
        private readonly ILogger<PatternLoaderService> _logger;

        public PatternLoaderService(
            PatternManagementService patternManagement,
            ILogger<PatternLoaderService> logger)
        {
            _patternManagement = patternManagement ?? throw new ArgumentNullException(nameof(patternManagement));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Extract structured data from OCR text using stored patterns
        /// </summary>
        /// <param name="ocrText">Raw OCR text from document</param>
        /// <param name="filePath">Path to original scanned file</param>
        /// <param name="documentType">Document type (e.g., "BankSlips")</param>
        /// <param name="format">Format/sub-collection (e.g., "KBIZ")</param>
        /// <returns>Generic document with dynamic fields based on pattern keys</returns>
        public async Task<GenericDocumentData> ExtractPatternsAsync(
     string ocrText,
     string filePath,
     string documentType,
     string format)
        {
            var result = new GenericDocumentData
            {
                FilePath = filePath,
                DocumentType = documentType,
                DocumentFormat = format,
                ProcessedAt = DateTime.UtcNow,
                Status = DocumentProcessingStatus.Processing
            };

            try
            {
                _logger.LogDebug("Starting pattern extraction for {DocumentType}/{Format} from {FilePath}",
                    documentType, format, Path.GetFileName(filePath));

                // Validate inputs
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    result.Status = DocumentProcessingStatus.Failed;
                    result.ErrorReason = "OCR text is empty";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(documentType) || string.IsNullOrWhiteSpace(format))
                {
                    result.Status = DocumentProcessingStatus.Failed;
                    result.ErrorReason = "Document type and format are required";
                    return result;
                }

                // Load all patterns for the document type and format
                var patterns = await LoadPatternsAsync(documentType, format);
                if (patterns.Count == 0)
                {
                    result.Status = DocumentProcessingStatus.Failed;
                    result.ErrorReason = $"No patterns found for {documentType}/{format}";
                    return result;
                }

                _logger.LogDebug("Found {PatternCount} patterns for {DocumentType}/{Format}",
                    patterns.Count, documentType, format);

                // 🔧 ENHANCED: Process each pattern and mark missing ones
                var successCount = 0;
                var extractedFields = new List<string>();
                var missingFields = new List<string>();

                foreach (var (patternKey, pattern) in patterns)
                {
                    try
                    {
                        var extractedValue = ExtractValueForPattern(ocrText, pattern);

                        if (!string.IsNullOrWhiteSpace(extractedValue))
                        {
                            // Successfully extracted data
                            result.SetField(patternKey, extractedValue, ExtractedDataType.Text);
                            extractedFields.Add(patternKey);
                            successCount++;
                            _logger.LogDebug("✅ Pattern '{PatternKey}' extracted: '{Value}'",
                                patternKey, extractedValue.Length > 50 ? $"{extractedValue[..50]}..." : extractedValue);
                        }
                        else
                        {
                            // 🆕 NEW: Mark failed extractions as "Missing"
                            result.SetField(patternKey, "Missing", ExtractedDataType.Text);
                            missingFields.Add(patternKey);
                            _logger.LogDebug("❌ Pattern '{PatternKey}' found no matches - marked as Missing", patternKey);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Pattern processing error - also mark as Missing
                        result.SetField(patternKey, "Missing", ExtractedDataType.Text);
                        missingFields.Add(patternKey);
                        _logger.LogWarning(ex, "❌ Pattern '{PatternKey}' error - marked as Missing: {Error}",
                            patternKey, ex.Message);
                    }
                }

                // 🔧 ENHANCED: Detailed logging for debugging
                var fileName = Path.GetFileName(filePath);

                if (extractedFields.Any())
                {
                    _logger.LogInformation("✅ {FileName}: Extracted {SuccessCount}/{TotalCount} patterns: {ExtractedFields}",
                        fileName, successCount, patterns.Count, string.Join(", ", extractedFields));
                }

                if (missingFields.Any())
                {
                    _logger.LogWarning("❌ {FileName}: Missing {MissingCount}/{TotalCount} patterns: {MissingFields}",
                        fileName, missingFields.Count, patterns.Count, string.Join(", ", missingFields));
                }

                // Set success status
                if (successCount > 0)
                {
                    result.Status = DocumentProcessingStatus.Completed;
                    result.AddNote("ExtractedFields", successCount.ToString());
                    result.AddNote("TotalPatterns", patterns.Count.ToString());
                    result.AddNote("MissingFields", missingFields.Count.ToString());

                    _logger.LogInformation("🎉 Pattern extraction completed for {DocumentType}/{Format}: " +
                        "{SuccessCount}/{TotalCount} successful, {MissingCount} missing",
                        documentType, format, successCount, patterns.Count, missingFields.Count);
                }
                else
                {
                    result.Status = DocumentProcessingStatus.Failed;
                    result.ErrorReason = "No patterns could extract data from the document";
                    _logger.LogError("💥 Pattern extraction failed for {DocumentType}/{Format}: " +
                        "All {TotalCount} patterns missing", documentType, format, patterns.Count);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pattern extraction for {DocumentType}/{Format}",
                    documentType, format);
                result.Status = DocumentProcessingStatus.Failed;
                result.ErrorReason = $"Pattern extraction failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Load all patterns for a specific document type and format
        /// </summary>
        private async Task<Dictionary<string, SearchPattern>> LoadPatternsAsync(string documentType, string format)
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
        /// Extract value from OCR text using a specific search pattern
        /// </summary>
        private string ExtractValueForPattern(string ocrText, SearchPattern pattern)
        {
            try
            {
                // Step 1: Find the keyword in the text
                var keywordIndex = FindKeywordInText(ocrText, pattern.KeyWord);
                if (keywordIndex == -1)
                {
                    _logger.LogDebug("Keyword '{KeyWord}' not found in text for pattern '{PatternName}'",
                        pattern.KeyWord, pattern.SearchName);
                    return string.Empty;
                }

                // Step 2: Extract text based on pattern type
                string extractedText = pattern.PatternType.ToLower() switch
                {
                    "verticalcolumn" => ExtractVerticalColumn(ocrText, keywordIndex, pattern),
                    "horizontal" => ExtractHorizontal(ocrText, keywordIndex, pattern),
                    _ => throw new NotSupportedException($"Pattern type '{pattern.PatternType}' is not supported")
                };

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return string.Empty;
                }

                // Step 3: Apply regex patterns if specified
                var finalValue = ApplyRegexPatterns(extractedText, pattern.RegexPatterns);

                // Step 4: Clean up stop words
                if (!string.IsNullOrWhiteSpace(pattern.StopWords))
                {
                    finalValue = RemoveStopWords(finalValue, pattern.StopWords);
                }

                return finalValue.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting value for pattern '{PatternName}': {Error}",
                    pattern.SearchName, ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Find keyword in text (case-insensitive)
        /// </summary>
        private int FindKeywordInText(string text, string keyword)
        {
            return text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extract text in vertical column pattern (text below the keyword)
        /// </summary>
        private string ExtractVerticalColumn(string ocrText, int keywordIndex, SearchPattern pattern)
        {
            // For now, implement basic vertical extraction
            // This will need to be enhanced with actual spatial/coordinate logic later
            var lines = ocrText.Split('\n');
            var keywordLine = -1;

            // Find which line contains the keyword
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(pattern.KeyWord, StringComparison.OrdinalIgnoreCase))
                {
                    keywordLine = i;
                    break;
                }
            }

            if (keywordLine == -1 || keywordLine >= lines.Length - 1)
                return string.Empty;

            // Look for content in next few lines (within tolerance)
            var maxLinesToCheck = Math.Min(3, lines.Length - keywordLine - 1);
            for (int i = 1; i <= maxLinesToCheck; i++)
            {
                var candidateLine = lines[keywordLine + i].Trim();
                if (!string.IsNullOrWhiteSpace(candidateLine))
                {
                    return candidateLine;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Extract text in horizontal pattern (text after the keyword on same line)
        /// </summary>
        private string ExtractHorizontal(string ocrText, int keywordIndex, SearchPattern pattern)
        {
            // Find the line containing the keyword
            var lines = ocrText.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(pattern.KeyWord, StringComparison.OrdinalIgnoreCase))
                {
                    var keywordPos = line.IndexOf(pattern.KeyWord, StringComparison.OrdinalIgnoreCase);
                    var afterKeyword = line.Substring(keywordPos + pattern.KeyWord.Length).Trim();

                    if (!string.IsNullOrWhiteSpace(afterKeyword))
                    {
                        return afterKeyword;
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Apply regex patterns to extract specific portions of text
        /// </summary>
        private string ApplyRegexPatterns(string text, List<string> regexPatterns)
        {
            if (regexPatterns == null || regexPatterns.Count == 0)
                return text;

            foreach (var regexPattern in regexPatterns)
            {
                try
                {
                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    var match = regex.Match(text);

                    if (match.Success)
                    {
                        return match.Value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid regex pattern '{Pattern}': {Error}", regexPattern, ex.Message);
                }
            }

            return text; // Return original text if no regex matches
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
        /// Get all available document types
        /// </summary>
        public async Task<List<string>> GetAvailableDocumentTypesAsync()
        {
            return await _patternManagement.GetCollectionNamesAsync();
        }

        /// <summary>
        /// Get all available formats for a document type
        /// </summary>
        public async Task<List<string>> GetAvailableFormatsAsync(string documentType)
        {
            return await _patternManagement.GetSubCollectionNamesAsync(documentType);
        }

        /// <summary>
        /// Check if patterns exist for a specific document type and format
        /// </summary>
        public async Task<bool> HasPatternsAsync(string documentType, string format)
        {
            var patterns = await LoadPatternsAsync(documentType, format);
            return patterns.Count > 0;
        }
    }
}