// NewwaysAdmin.SharedModels/Services/Ocr/SpatialPatternMatcher.cs
// 🎯 Shared Spatial Pattern Engine - EXACT COPY of Settings Ground Truth
// This service contains the IDENTICAL algorithms used in Settings → OCR Analyzer

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;
using NewwaysAdmin.SharedModels.Models.Ocr;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Spatial pattern matching service that contains the EXACT algorithms from Settings → OCR Analyzer.
    /// This is the single source of truth for spatial pattern extraction logic.
    /// </summary>
    public class SpatialPatternMatcher
    {
        private readonly ILogger<SpatialPatternMatcher> _logger;

        public SpatialPatternMatcher(ILogger<SpatialPatternMatcher> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Extract words using spatial pattern matching - EXACT same logic as Settings
        /// </summary>
        /// <param name="document">Spatial document containing all words</param>
        /// <param name="searchTerm">Keyword to find as anchor</param>
        /// <param name="patternType">Type of pattern: "VerticalColumn" or "Horizontal"</param>
        /// <param name="yTolerance">Y tolerance in pixels</param>
        /// <param name="xTolerance">X tolerance in pixels</param>
        /// <param name="stopWords">Stop words to halt extraction (comma-separated)</param>
        /// <param name="patternName">Pattern name for logging</param>
        /// <returns>Extracted text result</returns>
        public SpatialPatternResult ExtractPattern(
            SpatialDocument document,
            string searchTerm,
            string patternType,
            int yTolerance,
            int xTolerance,
            string stopWords = "",
            string patternName = "")
        {
            var result = new SpatialPatternResult();

            try
            {
                _logger.LogDebug("🔍 Starting spatial pattern extraction for '{PatternName}' with keyword '{SearchTerm}'",
                    patternName, searchTerm);

                // Find anchor word using EXACT same logic as Settings
                var anchorWord = document.FindWordsByText(searchTerm, exactMatch: false).FirstOrDefault();
                if (anchorWord == null)
                {
                    _logger.LogWarning("❌ Keyword '{SearchTerm}' not found in spatial document for pattern '{PatternName}'",
                        searchTerm, patternName);
                    return result; // Success = false by default
                }

                result.AnchorWord = anchorWord;
                result.GroupedWords.Add(anchorWord);

                _logger.LogDebug("✅ Keyword '{SearchTerm}' found at position ({X},{Y}) for pattern '{PatternName}'",
                    searchTerm, anchorWord.RawX1, anchorWord.RawY1, patternName);

                // Parse stop words - EXACT same logic as Settings
                var stopWordsList = ParseStopWords(stopWords);

                // Extract based on pattern type using EXACT Settings algorithms
                switch (patternType.ToLower())
                {
                    case "verticalcolumn":
                        result = TestVerticalColumnPattern_ExactSettingsAlgorithm(document, anchorWord, yTolerance, xTolerance, stopWordsList, patternName);
                        break;

                    case "horizontal":
                        result = TestHorizontalPattern_ExactSettingsAlgorithm(document, anchorWord, yTolerance, xTolerance, stopWordsList, patternName);
                        break;

                    default:
                        _logger.LogWarning("❌ Unsupported pattern type '{PatternType}' for pattern '{PatternName}'",
                            patternType, patternName);
                        return result;
                }

                if (result.Success)
                {
                    _logger.LogInformation("🎉 Pattern '{PatternName}' successfully extracted {WordCount} words: '{Text}'",
                        patternName, result.GroupedWords.Count, result.CombinedText);
                }
                else
                {
                    _logger.LogWarning("❌ Pattern '{PatternName}' extraction failed", patternName);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error extracting spatial pattern '{PatternName}': {Error}",
                    patternName, ex.Message);
                return result;
            }
        }

        /// <summary>
        /// EXACT COPY of TestVerticalColumnPattern from PatternTester.razor
        /// </summary>
        private SpatialPatternResult TestVerticalColumnPattern_ExactSettingsAlgorithm(
            SpatialDocument document,
            WordBoundingBox anchorWord,
            int yTolerance,
            int xTolerance,
            List<string> stopWords,
            string patternName)
        {
            var result = new SpatialPatternResult
            {
                AnchorWord = anchorWord
            };
            result.GroupedWords.Add(anchorWord);

            // EXACT COPY from PatternTester.razor - Simple vertical search implementation
            var currentY = anchorWord.RawY2; // Start below anchor
            var searchX = anchorWord.RawX1;  // Use anchor's left edge for reference

            while (true)
            {
                // Find words at the next Y level - EXACT COPY from PatternTester.razor
                var nextWords = document.Words
                    .Where(w => w != anchorWord && !result.GroupedWords.Contains(w))
                    .Where(w => w.RawY1 >= currentY && w.RawY1 <= currentY + yTolerance)
                    .Where(w => Math.Abs(w.RawX1 - searchX) <= xTolerance)
                    .OrderBy(w => w.RawY1)
                    .ThenBy(w => w.RawX1)
                    .ToList();

                if (!nextWords.Any())
                    break;

                // Check for stop words (hard stop) - EXACT COPY from PatternTester.razor
                if (stopWords.Any() && nextWords.Any(w => stopWords.Any(stop =>
                    w.Text.Contains(stop, StringComparison.OrdinalIgnoreCase))))
                {
                    break; // Hard stop as in Settings
                }

                result.GroupedWords.AddRange(nextWords);  // EXACT: Add ALL words at same Y level
                currentY = nextWords.Max(w => w.RawY2);   // EXACT: Move to bottom of found words
            }

            result.Success = result.GroupedWords.Count > 1;
            result.CombinedText = string.Join("\n", result.GroupedWords.Select(w => w.Text));

            return result;
        }

        /// <summary>
        /// EXACT COPY of TestHorizontalPattern from PatternView.razor
        /// </summary>
        private SpatialPatternResult TestHorizontalPattern_ExactSettingsAlgorithm(
            SpatialDocument document,
            WordBoundingBox anchorWord,
            int yTolerance,
            int xTolerance,
            List<string> stopWords,
            string patternName)
        {
            var result = new SpatialPatternResult
            {
                AnchorWord = anchorWord
            };
            result.GroupedWords.Add(anchorWord);

            // Horizontal search requires stop words - EXACT logic from PatternView
            if (!stopWords.Any())
            {
                _logger.LogWarning("❌ Horizontal pattern '{PatternName}' requires stop words", patternName);
                return result; // Horizontal search requires stop words
            }

            // Search horizontally right-only from anchor - EXACT logic from PatternView
            var currentWord = anchorWord;

            while (true)
            {
                // Find next word to the right within Y tolerance - EXACT logic from PatternView
                var nextWord = document.Words
                    .Where(w => !result.GroupedWords.Contains(w))
                    .Where(w => w.RawX1 > currentWord.RawX2) // Must start after current word ends
                    .Where(w => Math.Abs(w.RawCenterY - currentWord.RawCenterY) <= yTolerance) // Y tolerance for horizontal alignment
                    .OrderBy(w => w.RawX1) // Closest word to the right
                    .FirstOrDefault();

                if (nextWord == null)
                    break; // No more words found

                // Add word to result FIRST - EXACT logic from PatternView
                result.GroupedWords.Add(nextWord);

                // Check if this is a stop word - if so, stop AFTER including it - EXACT logic from PatternView
                if (stopWords.Any(stop => nextWord.Text.Contains(stop, StringComparison.OrdinalIgnoreCase)))
                {
                    break; // Stop here, but we've already included the stop word
                }

                // Update current word for next iteration
                currentWord = nextWord;
            }

            result.Success = result.GroupedWords.Count > 1; // Need at least anchor + 1 word
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            return result;
        }

        /// <summary>
        /// Parse stop words string into list - EXACT logic from Settings
        /// </summary>
        private List<string> ParseStopWords(string stopWordsInput)
        {
            if (string.IsNullOrEmpty(stopWordsInput))
                return new List<string>();

            return stopWordsInput.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(w => w.Trim())
                                .Where(w => !string.IsNullOrEmpty(w))
                                .ToList();
        }
    }

    /// <summary>
    /// Result of spatial pattern extraction
    /// </summary>
    public class SpatialPatternResult
    {
        public bool Success { get; set; } = false;
        public WordBoundingBox? AnchorWord { get; set; }
        public List<WordBoundingBox> GroupedWords { get; set; } = new List<WordBoundingBox>();
        public string CombinedText { get; set; } = string.Empty;

        /// <summary>
        /// Get combined text with custom separator
        /// </summary>
        public string GetCombinedText(string separator = " ")
        {
            if (!GroupedWords.Any()) return string.Empty;
            return string.Join(separator, GroupedWords.Select(w => w.Text));
        }

        /// <summary>
        /// Get text from grouped words excluding the anchor word
        /// </summary>
        public string GetExtractedTextOnly(string separator = " ")
        {
            var extractedWords = GroupedWords.Where(w => w != AnchorWord).ToList();
            if (!extractedWords.Any()) return string.Empty;
            return string.Join(separator, extractedWords.Select(w => w.Text));
        }
    }
}