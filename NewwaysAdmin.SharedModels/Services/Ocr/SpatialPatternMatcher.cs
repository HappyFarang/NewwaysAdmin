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
                _logger.LogDebug("🔍 Starting spatial pattern extraction for '{PatternName}' with search term '{SearchTerm}'",
                    patternName, searchTerm);

                // Parse stop words - needed for all patterns
                var stopWordsList = ParseStopWords(stopWords);

                // HANDLE POSITION-BASED PATTERNS FIRST (before keyword search)
                if (patternType.ToLower() == "positionbasedcolumn")
                {
                    var coords = ParseCoordinates(searchTerm);
                    if (coords == null)
                    {
                        _logger.LogWarning("❌ Invalid coordinates format '{SearchTerm}' for pattern '{PatternName}'. Expected format: 'X,Y'",
                            searchTerm, patternName);
                        return result;
                    }

                    return TestPositionBasedColumnPattern(document, coords.Value.x, coords.Value.y, yTolerance, xTolerance, stopWordsList, patternName);
                }

                // ADD this new case right after the column case:
                if (patternType.ToLower() == "positionbasedhorizontal")
                {
                    var coords = ParseCoordinates(searchTerm);
                    if (coords == null)
                    {
                        _logger.LogWarning("❌ Invalid coordinates format '{SearchTerm}' for pattern '{PatternName}'. Expected format: 'X,Y'",
                            searchTerm, patternName);
                        return result;
                    }

                    // X coordinate is ignored, only Y coordinate is used
                    return TestPositionBasedHorizontalPattern(document, coords.Value.x, coords.Value.y, yTolerance, xTolerance, stopWordsList, patternName);
                }

                // FOR KEYWORD-BASED PATTERNS: Find anchor word
                var anchorWord = document.FindWordsByText(searchTerm, exactMatch: false).FirstOrDefault();
                if (anchorWord == null)
                {
                    _logger.LogWarning("❌ Keyword '{SearchTerm}' not found in spatial document for pattern '{PatternName}'",
                        searchTerm, patternName);
                    return result;
                }

                result.AnchorWord = anchorWord;
                result.GroupedWords.Add(anchorWord);

                _logger.LogDebug("✅ Keyword '{SearchTerm}' found at position ({X},{Y}) for pattern '{PatternName}'",
                    searchTerm, anchorWord.RawX1, anchorWord.RawY1, patternName);

                // Extract based on pattern type
                switch (patternType.ToLower())
                {
                    case "verticalcolumn":
                        result = TestVerticalColumnPattern_CompleteAlgorithm(document, anchorWord, yTolerance, xTolerance, stopWordsList, patternName);
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

        // NEW: Parse coordinates from searchTerm like "189,373"
        private (int x, int y)? ParseCoordinates(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm)) return null;

            var parts = searchTerm.Split(',');
            if (parts.Length != 2) return null;

            if (int.TryParse(parts[0].Trim(), out int x) &&
                int.TryParse(parts[1].Trim(), out int y))
            {
                return (x, y);
            }

            return null;
        }

        // NEW: Find word at specific position using configurable grid search
        private WordBoundingBox? FindWordAtPosition(SpatialDocument document, int targetX, int targetY,
            int xTolerance = 5, int yTolerance = 26)
        {
            _logger.LogInformation("🎯 Searching for word at position ({TargetX},{TargetY}) with tolerance ±{XTol},+{YTol}",
                targetX, targetY, xTolerance, yTolerance);

            // Define search area: (targetX - xTolerance, targetY - yTolerance) to (targetX + xTolerance, targetY + yTolerance)
            var searchX1 = targetX - xTolerance;
            var searchY1 = targetY - yTolerance;
            var searchX2 = targetX + xTolerance;
            var searchY2 = targetY + yTolerance;

            _logger.LogInformation("🔍 Search rectangle: ({X1},{Y1}) to ({X2},{Y2})",
                searchX1, searchY1, searchX2, searchY2);

            // DEBUG: Show all words in document first
            _logger.LogInformation("📋 Document has {WordCount} total words", document.Words.Count);
            foreach (var word in document.Words.Take(10)) // Show first 10 words
            {
                _logger.LogInformation("   Word: '{Text}' at ({X},{Y})", word.Text, word.RawX1, word.RawY1);
            }

            // Find all words whose bounding boxes intersect with our search rectangle
            var candidateWords = document.Words
                .Where(w => !(w.RawX2 < searchX1 || w.RawX1 > searchX2 || w.RawY2 < searchY1 || w.RawY1 > searchY2))
                .ToList();

            _logger.LogInformation("📦 Found {CandidateCount} candidate words in search area:", candidateWords.Count);
            foreach (var candidate in candidateWords)
            {
                _logger.LogInformation("   Candidate: '{Text}' at ({X},{Y})", candidate.Text, candidate.RawX1, candidate.RawY1);
            }

            if (!candidateWords.Any())
            {
                _logger.LogWarning("❌ No words found in search area ({X1},{Y1}) to ({X2},{Y2})",
                    searchX1, searchY1, searchX2, searchY2);
                return null;
            }

            // Find closest word to target point (by distance from top-left corners)
            var closestWord = candidateWords
                .OrderBy(w => Math.Sqrt(Math.Pow(w.RawX1 - targetX, 2) + Math.Pow(w.RawY1 - targetY, 2)))
                .First();

            _logger.LogInformation("🎯 Closest word to ({TargetX},{TargetY}): '{WordText}' at ({WordX},{WordY})",
                targetX, targetY, closestWord.Text, closestWord.RawX1, closestWord.RawY1);

            return closestWord;
        }

        // NEW: Position-based column search algorithm
        private SpatialPatternResult TestPositionBasedColumnPattern(
     SpatialDocument document,
     int targetX,
     int targetY,
     int yTolerance,
     int xTolerance,
     List<string> stopWords,
     string patternName)
        {
            var result = new SpatialPatternResult();

            _logger.LogDebug("🔄 Starting position-based column search for pattern '{PatternName}' at ({X},{Y})",
                patternName, targetX, targetY);

            // Step 1: Find anchor word at specified position (using 10x26 default grid)
            var anchorWord = FindWordAtPosition(document, targetX, targetY, xTolerance: 5, yTolerance: 26);
            if (anchorWord == null)
            {
                _logger.LogWarning("❌ No word found at position ({X},{Y}) for pattern '{PatternName}'",
                    targetX, targetY, patternName);
                return result;
            }

            result.AnchorWord = anchorWord;

            _logger.LogDebug("✅ Found anchor word '{WordText}' at position ({X},{Y}) for pattern '{PatternName}'",
                anchorWord.Text, anchorWord.RawX1, anchorWord.RawY1, patternName);

            // Step 2: First, capture the COMPLETE LINE containing the anchor word
            var firstLineWords = new List<WordBoundingBox> { anchorWord };
            var currentWord = anchorWord;

            // March left to find line start - allow 5px overlap
            while (true)
            {
                var leftWord = document.Words
                    .Where(w => !firstLineWords.Contains(w))
                    .Where(w => w.RawX2 <= currentWord.RawX1 + 5) // Allow 5px overlap
                    .Where(w => currentWord.RawX1 - w.RawX2 <= xTolerance + 5) // Adjust gap tolerance
                    .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2))
                    .OrderByDescending(w => w.RawX2)
                    .FirstOrDefault();

                if (leftWord == null) break;
                firstLineWords.Insert(0, leftWord);
                currentWord = leftWord;
            }

            // March right to complete the line - allow 5px overlap
            currentWord = anchorWord;
            while (true)
            {
                var rightWord = document.Words
                    .Where(w => !firstLineWords.Contains(w))
                    .Where(w => w.RawX1 >= currentWord.RawX2 - 5) // Allow 5px overlap
                    .Where(w => w.RawX1 - currentWord.RawX2 <= xTolerance + 5) // Adjust gap tolerance
                    .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2))
                    .OrderBy(w => w.RawX1)
                    .FirstOrDefault();

                if (rightWord == null) break;
                firstLineWords.Add(rightWord);
                currentWord = rightWord;
            }

            // Add the complete first line to results
            result.GroupedWords.AddRange(firstLineWords);
            _logger.LogDebug("📝 Added first line with {WordCount} words: '{LineText}'",
                firstLineWords.Count, string.Join(" ", firstLineWords.Select(w => w.Text)));

            // Step 3: NOW establish marching coordinate from the bottom of this complete line
            int marchingX = firstLineWords.First().RawX1;  // Leftmost word of first line
            int marchingY = firstLineWords.Max(w => w.RawY2);  // Bottom of first line

            _logger.LogDebug("📍 Marching coordinate after first line: ({MarchingX},{MarchingY})", marchingX, marchingY);

            // Step 4: Continue scanning down for additional lines
            while (true)
            {
                // March downward to find next line
                var foundWord = document.Words
                    .Where(w => !result.GroupedWords.Contains(w))
                    .Where(w => w.RawY1 > marchingY && w.RawY1 <= marchingY + yTolerance)
                    .Where(w => Math.Abs(w.RawX1 - marchingX) <= 5) // ±5px X tolerance from marching coordinate
                    .OrderBy(w => w.RawY1)
                    .FirstOrDefault();

                if (foundWord == null)
                {
                    // Try wider tolerance
                    var anyWordInRange = document.Words
                        .Where(w => !result.GroupedWords.Contains(w))
                        .Where(w => w.RawY1 > marchingY && w.RawY1 <= marchingY + yTolerance)
                        .Where(w => Math.Abs(w.RawX1 - marchingX) <= xTolerance)
                        .OrderBy(w => w.RawY1)
                        .FirstOrDefault();

                    if (anyWordInRange == null)
                        break; // No more lines found

                    foundWord = anyWordInRange;
                }

                // Build complete line
                var lineWords = new List<WordBoundingBox> { foundWord };
                currentWord = foundWord;

                // March left to find line start - allow 5px overlap
                while (true)
                {
                    var leftWord = document.Words
                        .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                        .Where(w => w.RawX2 <= currentWord.RawX1 + 5) // Allow 5px overlap
                        .Where(w => currentWord.RawX1 - w.RawX2 <= xTolerance + 5) // Adjust gap tolerance
                        .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2))
                        .OrderByDescending(w => w.RawX2)
                        .FirstOrDefault();

                    if (leftWord == null) break;
                    lineWords.Insert(0, leftWord);
                    currentWord = leftWord;
                }

                // Update marching coordinate to leftmost word
                var leftmostWord = lineWords.First();
                marchingX = leftmostWord.RawX1;

                // March right to complete the line - allow 5px overlap
                currentWord = leftmostWord;
                while (true)
                {
                    var rightWord = document.Words
                        .Where(w => !result.GroupedWords.Contains(w) && !lineWords.Contains(w))
                        .Where(w => w.RawX1 >= currentWord.RawX2 - 5) // Allow 5px overlap
                        .Where(w => w.RawX1 - currentWord.RawX2 <= xTolerance + 5) // Adjust gap tolerance
                        .Where(w => !(w.RawY2 < currentWord.RawY1 || w.RawY1 > currentWord.RawY2))
                        .OrderBy(w => w.RawX1)
                        .FirstOrDefault();

                    if (rightWord == null) break;
                    lineWords.Add(rightWord);
                    currentWord = rightWord;
                }

                // Add all words from this line
                result.GroupedWords.AddRange(lineWords);
                _logger.LogDebug("📝 Added additional line with {WordCount} words: '{LineText}'",
                    lineWords.Count, string.Join(" ", lineWords.Select(w => w.Text)));

                // Update marching Y to continue down
                marchingY = lineWords.Max(w => w.RawY2);
            }

            result.Success = result.GroupedWords.Count >= 1; // Changed from > 1 since we want even single lines
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            _logger.LogInformation("✅ Position-based column extracted {WordCount} words for pattern '{PatternName}': '{Text}'",
                result.GroupedWords.Count, patternName, result.CombinedText);

            return result;
        }

        private SpatialPatternResult TestPositionBasedHorizontalPattern(
    SpatialDocument document,
    int targetX,  // Ignored for horizontal patterns
    int targetY,  // Used for horizontal line detection
    int yTolerance,
    int xTolerance,  // Used for word gap detection if needed
    List<string> stopWords,
    string patternName)
        {
            var result = new SpatialPatternResult();

            _logger.LogDebug("🔄 Starting position-based horizontal search for pattern '{PatternName}' at Y={Y} (±{YTol}px)",
                patternName, targetY, yTolerance);

            // Find ALL words at the target Y position (within tolerance)
            var wordsAtTargetY = document.Words
                .Where(w => Math.Abs(w.RawY1 - targetY) <= yTolerance)
                .OrderBy(w => w.RawX1)  // Sort left to right
                .ToList();

            _logger.LogInformation("🔍 Found {WordCount} words at Y position {TargetY} (±{YTolerance}px):",
                wordsAtTargetY.Count, targetY, yTolerance);

            // Debug: Show all found words
            foreach (var word in wordsAtTargetY)
            {
                _logger.LogInformation("   Word: '{Text}' at ({X},{Y})", word.Text, word.RawX1, word.RawY1);
            }

            if (!wordsAtTargetY.Any())
            {
                _logger.LogWarning("❌ No words found at Y position {Y} (±{YTolerance}px) for pattern '{PatternName}'",
                    targetY, yTolerance, patternName);
                return result;
            }

            // Use first word as anchor for consistency
            result.AnchorWord = wordsAtTargetY.First();

            // Process words with gap filtering to handle proper word spacing
            var filteredWords = new List<WordBoundingBox>();
            var previousWord = wordsAtTargetY.First();
            filteredWords.Add(previousWord);

            for (int i = 1; i < wordsAtTargetY.Count; i++)
            {
                var currentWord = wordsAtTargetY[i];
                var gap = currentWord.RawX1 - previousWord.RawX2;

                // Only include if gap is reasonable (not too large - filters out unrelated text)
                if (gap <= xTolerance)
                {
                    filteredWords.Add(currentWord);
                    previousWord = currentWord;
                }
                else
                {
                    _logger.LogDebug("⏭️ Skipping word '{WordText}' - gap too large: {Gap}px > {Tolerance}px",
                        currentWord.Text, gap, xTolerance);
                }
            }

            // Add all filtered words to result
            result.GroupedWords.AddRange(filteredWords);

            // Check for stop words if specified
            if (stopWords.Any())
            {
                var stopWordFound = false;
                var wordsUpToStop = new List<WordBoundingBox>();

                foreach (var word in filteredWords)
                {
                    wordsUpToStop.Add(word);

                    if (stopWords.Any(stop => word.Text.Contains(stop, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogDebug("🛑 Found stop word '{StopWord}' in '{WordText}', stopping horizontal scan",
                            stopWords.First(stop => word.Text.Contains(stop, StringComparison.OrdinalIgnoreCase)), word.Text);
                        stopWordFound = true;
                        break;
                    }
                }

                if (stopWordFound)
                {
                    result.GroupedWords.Clear();
                    result.GroupedWords.AddRange(wordsUpToStop);
                }
            }

            result.Success = result.GroupedWords.Any();
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            _logger.LogInformation("✅ Position-based horizontal extracted {WordCount} words for pattern '{PatternName}': '{Text}'",
                result.GroupedWords.Count, patternName, result.CombinedText);

            return result;
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
        /// 🔧 EXACT COPY of TestHorizontalPattern from Settings → PatternView.razor
        /// Simple horizontal search that works perfectly in Settings
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

            _logger.LogDebug("🔄 Starting EXACT Settings horizontal algorithm for pattern '{PatternName}'", patternName);

            // Parse stop words (required for horizontal search)
            if (!stopWords.Any())
            {
                _logger.LogWarning("❌ Horizontal search requires stop words for pattern '{PatternName}'", patternName);
                return result; // Horizontal search requires stop words
            }

            _logger.LogDebug("📍 Stop words for horizontal search: {StopWords}", string.Join(", ", stopWords));

            // Search horizontally right-only from anchor using bounding box gaps
            var currentWord = anchorWord;
            while (true)
            {
                // Find next word to the right within Y tolerance
                // For horizontal search: unlimited X range, just find next word in line
                var nextWord = document.Words
                    .Where(w => !result.GroupedWords.Contains(w))
                    .Where(w => w.RawX1 > currentWord.RawX2) // Must start after current word ends
                    .Where(w => Math.Abs(w.RawCenterY - currentWord.RawCenterY) <= yTolerance) // Y tolerance for horizontal alignment
                    .OrderBy(w => w.RawX1) // Closest word to the right
                    .FirstOrDefault();

                if (nextWord == null)
                {
                    _logger.LogDebug("🛑 No more words found to the right in horizontal search");
                    break; // No more words found
                }

                _logger.LogDebug("➡️ Found next horizontal word: '{WordText}' at ({X},{Y})",
                    nextWord.Text, nextWord.RawX1, nextWord.RawY1);

                // FIXED: Add word to result FIRST
                result.GroupedWords.Add(nextWord);

                // Check if this is a stop word - if so, stop AFTER including it
                if (stopWords.Any(stop => nextWord.Text.Contains(stop, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug("🛑 Found stop word '{WordText}', stopping horizontal search", nextWord.Text);
                    break; // Stop here, but we've already included the stop word
                }

                // Update current word for next iteration
                currentWord = nextWord;
            }

            result.Success = result.GroupedWords.Count > 1; // Need at least anchor + 1 word
            result.CombinedText = string.Join(" ", result.GroupedWords.Select(w => w.Text));

            _logger.LogInformation("✅ EXACT Settings horizontal algorithm extracted {WordCount} words for pattern '{PatternName}': '{Text}'",
                result.GroupedWords.Count, patternName, result.CombinedText);

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