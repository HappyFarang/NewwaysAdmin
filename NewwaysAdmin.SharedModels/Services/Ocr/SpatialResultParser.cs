// NewwaysAdmin.SharedModels/Services/Ocr/SpatialResultParser.cs
// 🔧 Parser that processes SpatialPatternMatcher results through regex patterns
// This creates the final dictionary output ready for display/export
// UPDATED with Number Parsing Support + Magic Pattern Detection

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Ocr;
using NewwaysAdmin.SharedModels.Services.Parsing;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Processes SpatialPatternMatcher results through regex patterns to create final output dictionary
    /// NEW: Supports magic regex patterns for easy parsing (e.g., "Number Thai", "Date English")
    /// </summary>
    public class SpatialResultParser
    {
        private readonly ILogger<SpatialResultParser> _logger;
        private readonly DateParsingService _dateParsingService;
        private readonly NumberParsingService _numberParsingService;

        public SpatialResultParser(
            ILogger<SpatialResultParser> logger,
            DateParsingService dateParsingService,
            NumberParsingService numberParsingService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dateParsingService = dateParsingService ?? throw new ArgumentNullException(nameof(dateParsingService));
            _numberParsingService = numberParsingService ?? throw new ArgumentNullException(nameof(numberParsingService));
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
                _logger.LogDebug("🔄 Processing {Count} spatial results for {FileName}",
                    totalCount, fileName);

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

                    if (!string.IsNullOrWhiteSpace(processedValue) && !processedValue.StartsWith("Missing"))
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
        /// Process a spatial pattern result through regex patterns and create final extracted value
        /// NEW: Supports magic regex patterns that trigger special parsing modes
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
                Console.WriteLine($"🔧 ProcessSpatialResult called for pattern '{patternKey}' with {pattern.RegexPatterns?.Count ?? 0} regex patterns");

                // If spatial extraction failed, return specific error
                if (!spatialResult.Success || !spatialResult.GroupedWords.Any())
                {
                    _logger.LogDebug("❌ Spatial extraction failed for pattern '{PatternKey}' on {FileName}",
                        patternKey, fileName);
                    return "Missing Pattern";
                }

                // NEW: Check for magic regex patterns that trigger special parsing modes
                var (needsSpecialParsing, parsingType, useFullText) = DetectMagicPatterns(pattern.RegexPatterns);

                // Determine which text to process
                string textToProcess;
                if (pattern.NeedNumberParsing || needsSpecialParsing == "number" || useFullText)
                {
                    // Use full combined text for number parsing to get complete numbers
                    textToProcess = spatialResult.CombinedText;
                    Console.WriteLine($"💰 Using full combined text for parsing '{patternKey}': '{textToProcess}'");
                    _logger.LogDebug("💰 Using full combined text for parsing '{PatternKey}' on {FileName}: '{CombinedText}'",
                        patternKey, fileName, textToProcess);
                }
                else
                {
                    // Use extracted text (excluding anchor word) for other processing
                    textToProcess = spatialResult.GetExtractedTextOnly();
                    Console.WriteLine($"📝 Using extracted text for pattern '{patternKey}': '{textToProcess}'");
                    _logger.LogDebug("📝 Using extracted text for pattern '{PatternKey}' on {FileName}: '{ExtractedText}'",
                        patternKey, fileName, textToProcess);
                }

                if (string.IsNullOrWhiteSpace(textToProcess))
                {
                    _logger.LogDebug("❌ No text to process for pattern '{PatternKey}' on {FileName}",
                        patternKey, fileName);
                    return "Missing Text";
                }

                string processedText;

                // NEW: Handle magic regex patterns
                if (needsSpecialParsing != null)
                {
                    Console.WriteLine($"🪄 Magic pattern detected: '{needsSpecialParsing} {parsingType}' for '{patternKey}'");
                    _logger.LogDebug("🪄 Magic pattern detected: '{MagicType} {SubType}' for '{PatternKey}' on {FileName}",
                        needsSpecialParsing, parsingType, patternKey, fileName);

                    processedText = textToProcess.Trim();

                    // Apply the magic parsing
                    switch (needsSpecialParsing)
                    {
                        case "number":
                            var numberType = parsingType switch
                            {
                                "thai" => NumberParsingType.Thai,
                                "decimal" => NumberParsingType.Decimal,
                                "integer" => NumberParsingType.Integer,
                                "currency" => NumberParsingType.Currency,
                                _ => NumberParsingType.Thai
                            };
                            var contextInfo = $"{patternKey} on {fileName} (magic pattern)";
                            Console.WriteLine($"🔢 Applying number parsing ({numberType}) to: '{processedText}'");
                            processedText = _numberParsingService.ParseNumber(processedText, numberType, contextInfo);
                            Console.WriteLine($"✅ Number parsing result: '{processedText}'");
                            break;

                        case "date":
                            var dateType = parsingType == "english" ? DateParsingType.English : DateParsingType.Thai;
                            var dateContextInfo = $"{patternKey} on {fileName} (magic pattern)";
                            Console.WriteLine($"📅 Applying date parsing ({dateType}) to: '{processedText}'");
                            processedText = _dateParsingService.ParseDate(processedText, dateType, dateContextInfo);
                            Console.WriteLine($"✅ Date parsing result: '{processedText}'");
                            break;
                    }
                }
                else if (pattern.RegexPatterns == null || pattern.RegexPatterns.Count == 0)
                {
                    // No regex patterns defined, use the full text
                    _logger.LogDebug("✅ No regex patterns defined for '{PatternKey}' on {FileName}, using full text",
                        patternKey, fileName);
                    processedText = textToProcess.Trim();
                }
                else
                {
                    // Apply normal regex patterns to find a match
                    processedText = ApplyRegexPatterns(textToProcess, pattern.RegexPatterns, patternKey, fileName);

                    // If regex failed, return the error
                    if (processedText.StartsWith("Missing"))
                    {
                        return processedText;
                    }
                }

                // Apply normal configured parsing (if not already handled by magic patterns)
                if (needsSpecialParsing == null)
                {
                    // Apply date parsing if configured
                    if (pattern.NeedDateParsing)
                    {
                        var contextInfo = $"{patternKey} on {fileName}";
                        processedText = _dateParsingService.ParseDate(processedText, pattern.DateParsingType, contextInfo);
                    }

                    // Apply number parsing if configured
                    if (pattern.NeedNumberParsing)
                    {
                        var contextInfo = $"{patternKey} on {fileName}";
                        processedText = _numberParsingService.ParseNumber(processedText, pattern.NumberParsingType, contextInfo);
                    }
                }

                if (!string.IsNullOrWhiteSpace(processedText) && !processedText.StartsWith("Missing"))
                {
                    Console.WriteLine($"🎉 Final result for '{patternKey}': '{processedText}'");
                    _logger.LogInformation("🎉 Pattern '{PatternKey}' successfully processed for {FileName}: '{FinalValue}'",
                        patternKey, fileName, processedText);
                }

                return processedText;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Error in ProcessSpatialResult: {ex.Message}");
                _logger.LogError(ex, "💥 Error processing spatial result for pattern '{PatternKey}' on {FileName}: {Error}",
                    patternKey, fileName, ex.Message);
                return "Processing Error";
            }
        }

        /// <summary>
        /// Apply regex patterns to find a match, skipping magic patterns
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

                    // Skip magic patterns - they're handled separately
                    if (IsMagicPattern(regexPattern))
                        continue;

                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    var match = regex.Match(text);

                    if (match.Success)
                    {
                        // Use first capturing group if available, otherwise use full match
                        var result = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                        _logger.LogDebug("✅ Regex pattern matched for '{PatternKey}' on {FileName}: '{Pattern}' → '{Result}'",
                            patternKey, fileName, regexPattern, result);
                        return result.Trim(); // Found a match, break out of loop and return
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
        /// NEW: Detect magic regex patterns that trigger special parsing modes
        /// Magic patterns: "Number Thai", "Number Decimal", "Date Thai", "Date English", etc.
        /// </summary>
        private (string? parsingType, string? subType, bool useFullText) DetectMagicPatterns(List<string>? regexPatterns)
        {
            Console.WriteLine($"🔍 DetectMagicPatterns called with {regexPatterns?.Count ?? 0} patterns");

            if (regexPatterns == null)
            {
                Console.WriteLine("❌ No regex patterns provided");
                return (null, null, false);
            }

            foreach (var pattern in regexPatterns)
            {
                Console.WriteLine($"🔍 Checking pattern: '{pattern}'");
                var normalized = pattern.Trim().ToLowerInvariant();
                Console.WriteLine($"🔍 Normalized pattern: '{normalized}'");

                // Number magic patterns
                if (normalized == "number thai")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Number Thai!");
                    return ("number", "thai", true);
                }
                if (normalized == "number decimal")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Number Decimal!");
                    return ("number", "decimal", true);
                }
                if (normalized == "number integer")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Number Integer!");
                    return ("number", "integer", true);
                }
                if (normalized == "number currency")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Number Currency!");
                    return ("number", "currency", true);
                }
                if (normalized == "number")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Number (default Thai)!");
                    return ("number", "thai", true);
                }

                // Date magic patterns
                if (normalized == "date thai")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Date Thai!");
                    return ("date", "thai", false);
                }
                if (normalized == "date english")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Date English!");
                    return ("date", "english", false);
                }
                if (normalized == "date")
                {
                    Console.WriteLine("🪄 MAGIC PATTERN DETECTED: Date (default Thai)!");
                    return ("date", "thai", false);
                }
            }

            Console.WriteLine("❌ No magic patterns detected");
            return (null, null, false);
        }

        /// <summary>
        /// Check if a pattern is a magic pattern
        /// </summary>
        private bool IsMagicPattern(string pattern)
        {
            var normalized = pattern.Trim().ToLowerInvariant();
            return normalized.StartsWith("number") || normalized.StartsWith("date");
        }
    }
}