// NewwaysAdmin.SharedModels/Services/Ocr/SpatialPatternMatcher.cs
// 🎯 FIXED: Now uses complete marching algorithm from Settings (100% mirror)
// This service contains the EXACT algorithms used in Settings → OCR Analyzer

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;
using NewwaysAdmin.SharedModels.Models.Ocr;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    /// <summary>
    /// Spatial pattern matching service that contains the EXACT algorithms from Settings → OCR Analyzer.
    /// 🔧 FIXED: Now implements complete marching algorithm with collision detection
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
                        result = TestVerticalColumnPattern_CompleteAlgorithm(document, anchorWord, yTolerance, xTolerance, stopWordsList, patternName);
                        break;

                    case "horizontal":
                        result = TestHorizontalPattern_CompleteAlgorithm(document, anchorWord, yTolerance, xTolerance, stopWordsList, patternName);
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
        /// 🔧 FIXED: Complete vertical column algorithm with marching coordinate & collision detection
        /// EXACT COPY from Settings → PatternView.razor TestVerticalColumnPattern()
        /// </summary>
        private SpatialPatternResult TestVerticalColumnPattern_CompleteAlgorithm(
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

            _logger.LogDebug("🔄 Starting complete marching algorithm for pattern '{PatternName}'", patternName);

            // Step 2: Establish marching coordinate (left bottom corner of anchor)
            int marchingX = anchorWord.RawX1;
            int marchingY = anchorWord.RawY2;

            _logger.LogDebug("📍 Initial marching coordinate: ({MarchingX},{MarchingY})", marchingX, marchingY);

            while (true)
            {
                // Step 3: March downward on Y with ±5px X tolerance first, then wider tolerance
                var foundWord = document.Words
                    .Where(w => !result.GroupedWords.Contains(w))
                    .Where(w => w.RawY1 > marchingY && w.RawY1 <= marchingY + yTolerance) // Below marching Y within tolerance
                    .Where(w => Math.Abs(w.RawX1 - marchingX) <= 5) // ±5px X tolerance from marching coordinate
                    .OrderBy(w => w.RawY1) // Closest Y first
                    .FirstOrDefault();

                if (foundWord == null)
                {
                    // No word found directly below, try to find ANY word in Y range within X tolerance
                    var anyWordInRange = document.Words
                        .Where(w => !result.GroupedWords.Contains(w))
                        .Where(w => w.RawY1 > marchingY && w.RawY1 <= marchingY + yTolerance)
                        .Where(w => Math.Abs(w.RawX1 - marchingX) <= xTolerance) // Within X tolerance of marching coordinate
                        .OrderBy(w => w.RawY1)
                        .FirstOrDefault();

                    if (anyWordInRange == null)
                    {
                        _logger.LogDebug("🛑 No more words found below marching coordinate ({MarchingX},{MarchingY})", marchingX, marchingY);
                        break; // No more lines found
                    }

                    foundWord = anyWordInRange;
                }

                _logger.LogDebug("🎯 Found word '{WordText}' at ({X},{Y})", foundWord.Text, foundWord.RawX1, foundWord.RawY1);

                // Step 4: March LEFT from found word to establish line left boundary
                var lineWords = new List<WordBoundingBox> { foundWord };
                var currentWord = foundWord;

                // March left using collision detection
                while (true)
                {
                    var leftWord = document.Words
                        .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                        .Where(w => w.RawX2 <= currentWord.RawX1) // To the left of current word
                        .Where(w => currentWord.RawX1 - w.RawX2 <= xTolerance) // Within X gap tolerance
                        .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2)) // Y ranges would overlap if slid horizontally (collision detection)
                        .OrderByDescending(w => w.RawX2) // Closest to current word
                        .FirstOrDefault();

                    if (leftWord == null)
                        break; // No more words to the left

                    lineWords.Insert(0, leftWord); // Add to beginning
                    currentWord = leftWord;
                    _logger.LogDebug("⬅️ Added left word '{WordText}' at ({X},{Y})", leftWord.Text, leftWord.RawX1, leftWord.RawY1);
                }

                // Update marching coordinate to leftmost word's left bottom corner
                var leftmostWord = lineWords.First();
                marchingX = leftmostWord.RawX1;

                _logger.LogDebug("📍 Updated marching X to leftmost word: {MarchingX}", marchingX);

                // Step 5: March RIGHT from marching coordinate (leftmost word)
                currentWord = leftmostWord;
                while (true)
                {
                    var rightWord = document.Words
                        .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                        .Where(w => w.RawX1 >= currentWord.RawX2) // To the right of current word
                        .Where(w => w.RawX1 - currentWord.RawX2 <= xTolerance) // Within X gap tolerance
                        .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2)) // Y ranges would overlap if slid horizontally (collision detection)
                        .OrderBy(w => w.RawX1) // Closest to current word
                        .FirstOrDefault();

                    if (rightWord == null)
                        break; // No more words to the right

                    lineWords.Add(rightWord); // Add to end
                    currentWord = rightWord;
                    _logger.LogDebug("➡️ Added right word '{WordText}' at ({X},{Y})", rightWord.Text, rightWord.RawX1, rightWord.RawY1);
                }

                // Check for stop words
                if (stopWords.Any() && lineWords.Any(w => stopWords.Any(stop =>
                    w.Text.Contains(stop, StringComparison.OrdinalIgnoreCase))))
                {
                    _logger.LogDebug("🛑 Stop word found in line, halting extraction");
                    break; // Stop here
                }

                // Safety check: Look for missed symbols within the line boundaries
                var lineYTop = lineWords.Min(w => w.RawY1);
                var lineYBottom = lineWords.Max(w => w.RawY2);
                var lineXLeft = lineWords.Min(w => w.RawX1);
                var lineXRight = lineWords.Max(w => w.RawX2);

                // Look for any small symbols in the same Y area that might have been missed
                var missedSymbols = document.Words
                    .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                    .Where(w => w.RawY1 >= lineYTop && w.RawY2 <= lineYBottom) // Within line height
                    .Where(w => w.RawX1 >= lineXLeft && w.RawX2 <= lineXRight) // Within line width
                    .Where(w => w.Text.Length <= 2) // Only small symbols/characters
                    .ToList();

                // Insert missed symbols in the correct X position within lineWords
                foreach (var symbol in missedSymbols)
                {
                    // Find correct insertion point based on X position
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
                    _logger.LogDebug("🔍 Inserted missed symbol '{SymbolText}' at position {Position}", symbol.Text, insertIndex);
                }

                // Add the entire line to grouped words
                result.GroupedWords.AddRange(lineWords);
                _logger.LogDebug("📝 Added line with {WordCount} words: '{LineText}'",
                    lineWords.Count, string.Join(" ", lineWords.Select(w => w.Text)));

                // Update marching coordinate to bottom of current line
                marchingY = lineWords.Max(w => w.RawY2);
            }

            result.Success = result.GroupedWords.Count > 1;
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            _logger.LogInformation("✅ Complete algorithm extracted {WordCount} words for pattern '{PatternName}': '{Text}'",
                result.GroupedWords.Count, patternName, result.CombinedText);

            return result;
        }

        /// <summary>
        /// Complete horizontal pattern algorithm (placeholder for now)
        /// </summary>
        private SpatialPatternResult TestHorizontalPattern_CompleteAlgorithm(
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

            // TODO: Implement complete horizontal algorithm from Settings
            // For now, use simple horizontal search
            var currentX = anchorWord.RawX2; // Start to the right of anchor
            var searchY = anchorWord.RawY1;  // Use anchor's top edge for reference

            while (true)
            {
                var nextWords = document.Words
                    .Where(w => w != anchorWord && !result.GroupedWords.Contains(w))
                    .Where(w => w.RawX1 >= currentX && w.RawX1 <= currentX + xTolerance)
                    .Where(w => Math.Abs(w.RawY1 - searchY) <= yTolerance)
                    .OrderBy(w => w.RawX1)
                    .ThenBy(w => w.RawY1)
                    .ToList();

                if (!nextWords.Any())
                    break;

                // Check for stop words
                if (stopWords.Any() && nextWords.Any(w => stopWords.Any(stop =>
                    w.Text.Contains(stop, StringComparison.OrdinalIgnoreCase))))
                {
                    break;
                }

                result.GroupedWords.AddRange(nextWords);
                currentX = nextWords.Max(w => w.RawX2);
            }

            result.Success = result.GroupedWords.Count > 1;
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            return result;
        }

        /// <summary>
        /// Parse stop words string into list - EXACT same logic as Settings
        /// </summary>
        private List<string> ParseStopWords(string stopWords)
        {
            if (string.IsNullOrWhiteSpace(stopWords))
                return new List<string>();

            return stopWords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                           .Select(w => w.Trim())
                           .Where(w => !string.IsNullOrEmpty(w))
                           .ToList();
        }
    }
}