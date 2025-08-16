// NewwaysAdmin.WebAdmin/Services/BankSlips/DocumentParser.cs
// 🚀 NEW: Clean, focused document parser - no interfaces, no legacy baggage!

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Services.Ocr;
using NewwaysAdmin.SharedModels.Models.Ocr;
using System.Text.RegularExpressions;
using NewwaysAdmin.SharedModels.Models.Documents;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    /// <summary>
    /// Modern document parser - transforms OCR text into flexible dictionary data
    /// Single responsibility: OCR text → Dictionary using pattern-based regex processing
    /// </summary>
    public class DocumentParser
    {
        private readonly PatternLoaderService _patternLoader;
        private readonly PatternManagementService _patternManagement;
        private readonly ILogger<DocumentParser> _logger;

        public DocumentParser(
            PatternLoaderService patternLoader,
            PatternManagementService patternManagement,
            ILogger<DocumentParser> logger)
        {
            _patternLoader = patternLoader ?? throw new ArgumentNullException(nameof(patternLoader));
            _patternManagement = patternManagement ?? throw new ArgumentNullException(nameof(patternManagement));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Parse OCR text into flexible dictionary using pattern-based regex processing
        /// </summary>
        /// <param name="ocrText">Raw OCR text from document</param>
        /// <param name="imagePath">Full path to the source image file</param>
        /// <param name="documentType">Document type (e.g., "BankSlips")</param>
        /// <param name="formatName">Format name (e.g., "KBIZ", "KBank")</param>
        /// <returns>Dictionary with extracted fields + FileName</returns>
        public async Task<Dictionary<string, string>> ParseAsync(
            string ocrText,
            string imagePath,
            string documentType,
            string formatName)
        {
            var result = new Dictionary<string, string>();

            try
            {
                // Always add filename for tracking/debugging
                result["FileName"] = Path.GetFileName(imagePath);

                _logger.LogDebug("Starting document parsing for {FileName} using {DocumentType}/{FormatName}",
                    result["FileName"], documentType, formatName);

                // Step 1: Extract patterns using OCR service
                var genericDoc = await _patternLoader.ExtractPatternsAsync(ocrText, imagePath, documentType, formatName);

                if (genericDoc.Status == DocumentProcessingStatus.Failed)
                {
                    _logger.LogWarning("Pattern extraction failed for {FileName}: {Error}",
                        result["FileName"], genericDoc.ErrorReason);
                    return result; // Return with just FileName
                }

                // Step 2: Get the pattern definitions for regex processing
                var patterns = await GetPatternsForFormat(documentType, formatName);

                // Step 3: Process each field with regex loops
                foreach (var fieldName in genericDoc.GetFieldNames())
                {
                    var extractedText = genericDoc.GetFieldText(fieldName);
                    if (string.IsNullOrWhiteSpace(extractedText))
                        continue;

                    // Apply regex processing if patterns exist for this field
                    var processedValue = ProcessFieldWithRegex(fieldName, extractedText, patterns);

                    if (!string.IsNullOrWhiteSpace(processedValue))
                    {
                        result[fieldName] = processedValue;
                        _logger.LogDebug("Processed field {FieldName}: '{ProcessedValue}' for {FileName}",
                            fieldName, processedValue, result["FileName"]);
                    }
                }

                _logger.LogDebug("Document parsing completed for {FileName}: {FieldCount} fields extracted",
                    result["FileName"], result.Count - 1); // -1 for FileName

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing document {FileName}", result["FileName"]);
                return result; // Return with just FileName in case of error
            }
        }

        /// <summary>
        /// Process a field value through regex patterns - first match wins
        /// </summary>
        /// <param name="fieldName">Name of the field being processed</param>
        /// <param name="extractedText">Raw text extracted from OCR</param>
        /// <param name="patterns">Available patterns for this format</param>
        /// <returns>Processed value or original text if no patterns match</returns>
        private string ProcessFieldWithRegex(
            string fieldName,
            string extractedText,
            Dictionary<string, List<string>> patterns)
        {
            // Check if we have regex patterns for this field
            if (!patterns.ContainsKey(fieldName) || !patterns[fieldName].Any())
            {
                // No regex patterns - return cleaned original text
                return CleanExtractedText(extractedText);
            }

            // Try each regex pattern until one matches
            foreach (var regexPattern in patterns[fieldName])
            {
                try
                {
                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    var match = regex.Match(extractedText);

                    if (match.Success)
                    {
                        // Use the first capture group if available, otherwise the full match
                        var value = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                        var cleanedValue = CleanExtractedText(value);

                        _logger.LogDebug("Regex pattern matched for {FieldName}: '{Pattern}' → '{Result}'",
                            fieldName, regexPattern, cleanedValue);

                        return cleanedValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid regex pattern for {FieldName}: {Pattern}",
                        fieldName, regexPattern);
                    // Continue to next pattern
                }
            }

            // No regex patterns matched - return cleaned original text
            _logger.LogDebug("No regex patterns matched for {FieldName}, using original text", fieldName);
            return CleanExtractedText(extractedText);
        }

        /// <summary>
        /// Get regex patterns for the specified document format
        /// </summary>
        private async Task<Dictionary<string, List<string>>> GetPatternsForFormat(
            string documentType,
            string formatName)
        {
            var patterns = new Dictionary<string, List<string>>();

            try
            {
                _logger.LogDebug("Loading regex patterns for {DocumentType}/{FormatName}",
                    documentType, formatName);

                // Load patterns from the existing pattern management system
                var patternNames = await _patternManagement.GetSearchPatternNamesAsync(documentType, formatName);

                foreach (var patternName in patternNames)
                {
                    var searchPattern = await _patternManagement.LoadSearchPatternAsync(documentType, formatName, patternName);

                    if (searchPattern?.RegexPatterns != null && searchPattern.RegexPatterns.Any())
                    {
                        patterns[patternName] = searchPattern.RegexPatterns;
                        _logger.LogDebug("Loaded {Count} regex patterns for field {FieldName}",
                            searchPattern.RegexPatterns.Count, patternName);
                    }
                }

                _logger.LogDebug("Loaded regex patterns for {FieldCount} fields in {DocumentType}/{FormatName}",
                    patterns.Count, documentType, formatName);

                return patterns;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading regex patterns for {DocumentType}/{FormatName}",
                    documentType, formatName);
                return patterns;
            }
        }

        /// <summary>
        /// Clean and normalize extracted text
        /// </summary>
        private static string CleanExtractedText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim()
                      .Replace("\r\n", " ")
                      .Replace("\n", " ")
                      .Replace("\t", " ")
                      .Trim();
        }
    }
}