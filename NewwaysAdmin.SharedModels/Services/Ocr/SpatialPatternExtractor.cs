// NewwaysAdmin.SharedModels/Services/Ocr/SpatialPatternExtractor.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;
using NewwaysAdmin.SharedModels.Models.Ocr;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Shared spatial pattern extraction service that uses the EXACT same algorithm as Settings > OCR > Pattern view
    /// This ensures consistent behavior between Settings testing and Bank Slip processing
    /// </summary>
    public class SpatialPatternExtractor
    {
        private readonly ILogger<SpatialPatternExtractor> _logger;

        public SpatialPatternExtractor(ILogger<SpatialPatternExtractor> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Extract pattern using the exact same algorithm as Settings > OCR > Pattern view
        /// Returns 100% carbon copy of Settings behavior
        /// </summary>
        public PatternExtractionResult ExtractPattern(
            SpatialDocument document,
            SearchPattern pattern)
        {
            try
            {
                _logger.LogDebug("🔍 Starting spatial pattern extraction for '{PatternName}' with keyword '{KeyWord}'",
                    pattern.SearchName, pattern.KeyWord);

                var result = pattern.PatternType.ToLower() switch
                {
                    "verticalcolumn" => ExtractVerticalColumnPattern(document, pattern),
                    "horizontal" => ExtractHorizontalPattern(document, pattern),
                    _ => new PatternExtractionResult { Success = false, ErrorMessage = $"Unsupported pattern type: {pattern.PatternType}" }
                };

                if (result.Success)
                {
                    _logger.LogDebug("✅ Pattern extraction successful: {WordCount} words found", result.GroupedWords.Count);
                }
                else
                {
                    _logger.LogDebug("❌ Pattern extraction failed: {Error}", result.ErrorMessage);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error in pattern extraction for '{PatternName}': {Error}",
                    pattern.SearchName, ex.Message);
                return new PatternExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Vertical Column Pattern - EXACT carbon copy from Settings PatternView.razor
        /// Uses sophisticated marching coordinate algorithm with collision detection
        /// </summary>
        private PatternExtractionResult ExtractVerticalColumnPattern(SpatialDocument document, SearchPattern pattern)
        {
            var result = new PatternExtractionResult();

            // Step 1: Find anchor word (exact same logic as Settings)
            var anchorWord = document.FindWordsByText(pattern.KeyWord, exactMatch: false).FirstOrDefault();
            if (anchorWord == null)
            {
                result.ErrorMessage = $"Keyword '{pattern.KeyWord}' not found in document";
                return result;
            }

            result.AnchorWord = anchorWord;
            result.GroupedWords.Add(anchorWord);

            // Step 2: Establish marching coordinate (left bottom corner of anchor) - EXACT SAME AS SETTINGS
            int marchingX = anchorWord.RawX1;
            int marchingY = anchorWord.RawY2;

            while (true)
            {
                // Step 3: March downward on Y with ±5px X tolerance - EXACT SAME AS SETTINGS
                var foundWord = document.Words
                    .Where(w => !result.GroupedWords.Contains(w))
                    .Where(w => w.RawY1 > marchingY && w.RawY1 <= marchingY + pattern.ToleranceY) // Below marching Y within tolerance
                    .Where(w => Math.Abs(w.RawX1 - marchingX) <= 5) // ±5px X tolerance from marching coordinate
                    .OrderBy(w => w.RawY1) // Closest Y first
                    .FirstOrDefault();

                if (foundWord == null)
                {
                    // No word found directly below, try to find ANY word in Y range within X tolerance
                    var anyWordInRange = document.Words
                        .Where(w => !result.GroupedWords.Contains(w))
                        .Where(w => w.RawY1 > marchingY && w.RawY1 <= marchingY + pattern.ToleranceY)
                        .Where(w => Math.Abs(w.RawX1 - marchingX) <= pattern.ToleranceX) // Within X tolerance of marching coordinate
                        .OrderBy(w => w.RawY1)
                        .FirstOrDefault();

                    if (anyWordInRange == null)
                        break; // No more lines found

                    foundWord = anyWordInRange;
                }

                // Step 4: March LEFT from found word to establish line left boundary - EXACT SAME AS SETTINGS
                var lineWords = new List<WordBoundingBox> { foundWord };
                var currentWord = foundWord;

                // March left using collision detection
                while (true)
                {
                    var leftWord = document.Words
                        .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                        .Where(w => w.RawX2 <= currentWord.RawX1) // To the left of current word
                        .Where(w => currentWord.RawX1 - w.RawX2 <= pattern.ToleranceX) // Within X gap tolerance
                        .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2)) // Y ranges would overlap if slid horizontally (collision detection)
                        .OrderByDescending(w => w.RawX2) // Closest to current word
                        .FirstOrDefault();

                    if (leftWord == null)
                        break; // No more words to the left

                    lineWords.Insert(0, leftWord); // Add to beginning
                    currentWord = leftWord;
                }

                // Update marching coordinate to leftmost word's left bottom corner
                var leftmostWord = lineWords.First();
                marchingX = leftmostWord.RawX1;

                // Step 5: March RIGHT from marching coordinate (leftmost word) - EXACT SAME AS SETTINGS
                currentWord = leftmostWord;
                while (true)
                {
                    var rightWord = document.Words
                        .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                        .Where(w => w.RawX1 >= currentWord.RawX2) // To the right of current word
                        .Where(w => w.RawX1 - currentWord.RawX2 <= pattern.ToleranceX) // Within X gap tolerance
                        .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2)) // Y ranges would overlap if slid horizontally (collision detection)
                        .OrderBy(w => w.RawX1) // Closest word to the right
                        .FirstOrDefault();

                    if (rightWord == null)
                        break; // No more words found

                    lineWords.Add(rightWord);
                    currentWord = rightWord;
                }

                // Look for symbols that might be between words (exact same logic as Settings)
                var allSymbols = document.Words
                    .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                    .Where(w => w.RawY1 >= lineWords.Min(lw => lw.RawY1) - pattern.ToleranceY &&
                               w.RawY2 <= lineWords.Max(lw => lw.RawY2) + pattern.ToleranceY + 20)
                    .Where(w => lineWords.Any(lw => w.RawX1 >= lw.RawX1 && w.RawX2 <= lw.RawX2))
                    .ToList();

                // Insert symbols in correct positions
                foreach (var symbol in allSymbols)
                {
                    var insertIndex = lineWords.Count;
                    for (int i = 0; i < lineWords.Count; i++)
                    {
                        if (symbol.RawX1 < lineWords[i].RawX1)
                        {
                            insertIndex = i;
                            break;
                        }
                    }
                    lineWords.Insert(insertIndex, symbol);
                }

                // Add all words from this line to results (including any missed symbols)
                result.GroupedWords.AddRange(lineWords);

                // Step 6: Update marching Y to continue searching down from this line - EXACT SAME AS SETTINGS
                if (lineWords.Any())
                {
                    marchingY = lineWords.Max(w => w.RawY2);
                }
                else
                {
                    // Fallback if no words found - use tolerance to continue
                    marchingY += pattern.ToleranceY;
                }
            }

            // Step 7: Generate result (exact same as Settings)
            result.Success = result.GroupedWords.Count > 1; // Need at least anchor + 1 word
            result.CombinedText = string.Join("\n", result.GroupedWords.Select(w => w.Text));

            // Add metadata like Settings does
            result.AddMetadata("SearchTerm", pattern.KeyWord);
            result.AddMetadata("PatternType", pattern.PatternType);
            result.AddMetadata("YTolerance", pattern.ToleranceY);
            result.AddMetadata("XTolerance", pattern.ToleranceX);
            result.AddMetadata("AnchorPosition", $"{anchorWord.RawX1},{anchorWord.RawY1}");
            result.AddMetadata("MarchingCoordinate", $"{marchingX},{marchingY}");

            return result;
        }

        /// <summary>
        /// Horizontal Pattern - EXACT carbon copy from Settings PatternView.razor
        /// Searches horizontally right from anchor word until stop words found
        /// </summary>
        private PatternExtractionResult ExtractHorizontalPattern(SpatialDocument document, SearchPattern pattern)
        {
            var result = new PatternExtractionResult();

            // Step 1: Find anchor word (exact same logic as Settings)
            var anchorWord = document.FindWordsByText(pattern.KeyWord, exactMatch: false).FirstOrDefault();
            if (anchorWord == null)
            {
                result.ErrorMessage = $"Keyword '{pattern.KeyWord}' not found in document";
                return result;
            }

            result.AnchorWord = anchorWord;
            result.GroupedWords.Add(anchorWord);

            // Step 2: Parse stop words (exact same as Settings)
            var stopWords = GetStopWordsList(pattern.StopWords);
            if (!stopWords.Any())
            {
                result.ErrorMessage = "Horizontal search requires stop words";
                return result;
            }

            // Step 3: Search horizontally right-only from anchor (exact same logic as Settings)
            var currentWord = anchorWord;

            while (true)
            {
                // Find next word to the right within Y tolerance
                var nextWord = document.Words
                    .Where(w => !result.GroupedWords.Contains(w))
                    .Where(w => w.RawX1 > currentWord.RawX2) // Must start after current word ends
                    .Where(w => Math.Abs(w.RawCenterY - currentWord.RawCenterY) <= pattern.ToleranceY) // Y tolerance for horizontal alignment
                    .OrderBy(w => w.RawX1) // Closest word to the right
                    .FirstOrDefault();

                if (nextWord == null)
                    break; // No more words found

                // Add word to result FIRST (exact same logic as Settings)
                result.GroupedWords.Add(nextWord);

                // Check if this is a stop word - if so, stop AFTER including it
                if (stopWords.Any(stop => nextWord.Text.Contains(stop, StringComparison.OrdinalIgnoreCase)))
                {
                    break; // Stop here, but we've already included the stop word
                }

                // Update current word for next iteration
                currentWord = nextWord;
            }

            // Step 4: Generate result (exact same as Settings)
            result.Success = result.GroupedWords.Count > 1; // Need at least anchor + 1 word
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            return result;
        }

        /// <summary>
        /// Parse stop words exactly like Settings does
        /// </summary>
        private List<string> GetStopWordsList(string stopWords)
        {
            if (string.IsNullOrWhiteSpace(stopWords))
                return new List<string>();

            return stopWords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Trim())
                .Where(w => !string.IsNullOrEmpty(w))
                .ToList();
        }
    }

    /// <summary>
    /// Result of pattern extraction - matches the structure used in Settings
    /// </summary>
    public class PatternExtractionResult
    {
        public bool Success { get; set; } = false;
        public string ErrorMessage { get; set; } = string.Empty;
        public WordBoundingBox? AnchorWord { get; set; }
        public List<WordBoundingBox> GroupedWords { get; set; } = new();
        public string CombinedText { get; set; } = string.Empty;
        public Dictionary<string, object> Metadata { get; set; } = new();

        public void AddMetadata(string key, object value)
        {
            Metadata[key] = value;
        }
    }
}