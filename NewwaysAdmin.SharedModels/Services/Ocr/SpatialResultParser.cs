// NewwaysAdmin.SharedModels/Services/Ocr/SpatialResultParser.cs
// 🔧 Parser that processes SpatialPatternMatcher results through regex patterns
// This creates the final dictionary output ready for display/export

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Ocr;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Processes SpatialPatternMatcher results through regex patterns to create final output dictionary
    /// </summary>
    public class SpatialResultParser
    {
        private readonly ILogger<SpatialResultParser> _logger;

        public SpatialResultParser(ILogger<SpatialResultParser> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Process a spatial pattern result through regex patterns and create final extracted value
        /// </summary>
        /// <param name="spatialResult">Result from SpatialPatternMatcher</param>
        /// <param name="pattern">SearchPattern containing regex patterns</param>
        /// <param name="patternKey">The pattern key/name for logging</param>
        /// <param name="fileName">Source file name for logging</param>
        /// <returns>Final processed text ready for display/export</returns>
        public string ProcessSpatialResult(
            SpatialPatternResult spatialResult,
            SearchPattern pattern,
            string patternKey,
            string fileName = "")
        {
            try
            {
                // If spatial extraction failed, return specific error
                if (!spatialResult.Success || !spatialResult.GroupedWords.Any())
                {
                    _logger.LogDebug("❌ Spatial extraction failed for pattern '{PatternKey}' on {FileName}",
                        patternKey, fileName);
                    return "Missing Pattern";
                }

                // Get the extracted text (excluding anchor word)
                var extractedText = spatialResult.GetExtractedTextOnly();

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    _logger.LogDebug("❌ No text extracted after spatial matching for pattern '{PatternKey}' on {FileName}",
                        patternKey, fileName);
                    return "Missing Text";
                }

                _logger.LogDebug("📝 Raw spatial text for pattern '{PatternKey}' on {FileName}: '{ExtractedText}'",
                    patternKey, fileName, extractedText);

                // If no regex patterns defined, return the full extracted text (e.g., "To:" field)
                if (pattern.RegexPatterns == null || pattern.RegexPatterns.Count == 0)
                {
                    _logger.LogDebug("✅ No regex patterns defined for '{PatternKey}' on {FileName}, returning full extracted text",
                        patternKey, fileName);
                    return extractedText.Trim();
                }

                // Apply regex patterns to find a match
                var finalValue = ApplyRegexPatterns(extractedText, pattern.RegexPatterns, patternKey, fileName);

                if (!string.IsNullOrWhiteSpace(finalValue) && !finalValue.StartsWith("Missing"))
                {
                    _logger.LogInformation("🎉 Pattern '{PatternKey}' successfully processed for {FileName}: '{FinalValue}'",
                        patternKey, fileName, finalValue);
                }

                return finalValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing spatial result for pattern '{PatternKey}' on {FileName}: {Error}",
                    patternKey, fileName, ex.Message);
                return "Error";
            }
        }

        /// <summary>
        /// Process multiple spatial results into a final dictionary
        /// </summary>
        /// <param name="spatialResults">Dictionary of pattern keys to spatial results</param>
        /// <param name="patterns">Dictionary of pattern keys to SearchPattern objects</param>
        /// <param name="fileName">Source file name for logging</param>
        /// <returns>Final dictionary ready for display/export</returns>
        public Dictionary<string, string> ProcessMultipleResults(
            Dictionary<string, SpatialPatternResult> spatialResults,
            Dictionary<string, SearchPattern> patterns,
            string fileName = "")
        {
            var finalResults = new Dictionary<string, string>();
            var successCount = 0;
            var totalCount = spatialResults.Count;

            try
            {
                _logger.LogDebug("🔄 Processing {Count} spatial results for {FileName}", totalCount, fileName);

                foreach (var kvp in spatialResults)
                {
                    var patternKey = kvp.Key;
                    var spatialResult = kvp.Value;

                    // Get the corresponding pattern
                    if (!patterns.TryGetValue(patternKey, out var pattern))
                    {
                        _logger.LogWarning("❌ No pattern definition found for key '{PatternKey}' on {FileName}",
                            patternKey, fileName);
                        finalResults[patternKey] = "Missing";
                        continue;
                    }

                    // Process the spatial result
                    var processedValue = ProcessSpatialResult(spatialResult, pattern, patternKey, fileName);

                    if (!string.IsNullOrWhiteSpace(processedValue))
                    {
                        finalResults[patternKey] = processedValue;
                        successCount++;
                    }
                    else
                    {
                        finalResults[patternKey] = "Missing";
                    }
                }

                _logger.LogInformation("✅ Processed {SuccessCount}/{TotalCount} patterns successfully for {FileName}",
                    successCount, totalCount, fileName);

                return finalResults;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing multiple spatial results for {FileName}: {Error}",
                    fileName, ex.Message);
                return finalResults;
            }
        }

        /// <summary>
        /// Apply regex patterns to extract specific portions of text
        /// Loops through multiple patterns until one matches, then returns that match
        /// Used for keywords that can have multiple solutions (e.g., "transfer", "bill payment", "fee")
        /// </summary>
        private string ApplyRegexPatterns(string text, List<string> regexPatterns, string patternKey, string fileName)
        {
            _logger.LogDebug("🔄 Applying {Count} regex patterns to text '{Text}' for pattern '{PatternKey}' on {FileName}",
                regexPatterns.Count, text, patternKey, fileName);

            // Loop through each regex pattern until we find a match
            foreach (var regexPattern in regexPatterns)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(regexPattern))
                        continue;

                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    var match = regex.Match(text);

                    if (match.Success)
                    {
                        var result = match.Value.Trim();
                        _logger.LogDebug("✅ Regex pattern matched for '{PatternKey}' on {FileName}: '{Pattern}' → '{Result}'",
                            patternKey, fileName, regexPattern, result);
                        return result; // Found a match, break out of loop and return
                    }
                    else
                    {
                        _logger.LogDebug("❌ Regex pattern '{Pattern}' did not match text '{Text}' for pattern '{PatternKey}' on {FileName}",
                            regexPattern, text, patternKey, fileName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "💥 Invalid regex pattern for '{PatternKey}' on {FileName}: '{Pattern}' - {Error}",
                        patternKey, fileName, regexPattern, ex.Message);
                }
            }

            // If no regex patterns matched any of the alternatives, return specific error message
            _logger.LogWarning("❌ No regex patterns matched for '{PatternKey}' on {FileName}, returning 'Missing Regex Match'",
                patternKey, fileName);
            return "Missing Regex Match";
        }

        /// <summary>
        /// Create a summary of processing results for debugging/logging
        /// </summary>
        /// <param name="results">Final processed results</param>
        /// <param name="fileName">Source file name</param>
        /// <returns>Processing summary</returns>
        public ProcessingSummary CreateSummary(Dictionary<string, string> results, string fileName = "")
        {
            var summary = new ProcessingSummary
            {
                FileName = fileName,
                TotalPatterns = results.Count,
                SuccessfulPatterns = results.Values.Count(v => !string.IsNullOrEmpty(v) && v != "Missing"),
                MissingPatterns = results.Values.Count(v => string.IsNullOrEmpty(v) || v == "Missing"),
                ProcessedAt = DateTime.UtcNow
            };

            summary.ExtractedFields = results
                .Where(kvp => !string.IsNullOrEmpty(kvp.Value) && kvp.Value != "Missing")
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            summary.MissingFields = results
                .Where(kvp => string.IsNullOrEmpty(kvp.Value) || kvp.Value == "Missing")
                .Select(kvp => kvp.Key)
                .ToList();

            _logger.LogInformation("📊 Processing Summary for {FileName}: {Success}/{Total} patterns extracted",
                fileName, summary.SuccessfulPatterns, summary.TotalPatterns);

            return summary;
        }
    }

    /// <summary>
    /// Summary of processing results
    /// </summary>
    public class ProcessingSummary
    {
        public string FileName { get; set; } = string.Empty;
        public int TotalPatterns { get; set; }
        public int SuccessfulPatterns { get; set; }
        public int MissingPatterns { get; set; }
        public DateTime ProcessedAt { get; set; }
        public Dictionary<string, string> ExtractedFields { get; set; } = new();
        public List<string> MissingFields { get; set; } = new();

        public double SuccessRate => TotalPatterns > 0 ? (double)SuccessfulPatterns / TotalPatterns * 100 : 0;
    }
}