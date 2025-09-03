// NewwaysAdmin.SharedModels/Services/Ocr/ThaiTextProcessor.cs

using NewwaysAdmin.SharedModels.Models.Ocr.Core;

namespace NewwaysAdmin.SharedModels.Services.Ocr
{
    public static class ThaiTextProcessor
    {
        public static void MergeThaiComponents(SpatialDocument document,
            int verticalGapTolerance = 15,
            double horizontalOverlapThreshold = 0.5)
        {
            Console.WriteLine($"🔍 DEBUG: Starting Thai processing on {document.Words.Count} total words");

            if (document.Words.Count == 0) return;

            // Find Thai words and show them
            var thaiWords = document.Words.Where(w => IsThaiWord(w.Text)).ToList();
            Console.WriteLine($"🇹🇭 DEBUG: Found {thaiWords.Count} Thai words:");

            foreach (var word in thaiWords.Take(10)) // Show first 10
            {
                Console.WriteLine($"   Thai word: '{word.Text}' (length: {word.Text.Length}) at ({word.RawX1},{word.RawY1})");
            }

            if (thaiWords.Count == 0)
            {
                Console.WriteLine("❌ DEBUG: No Thai words found, skipping processing");
                return;
            }

            var originalThaiCount = thaiWords.Count;

            // Sort for consistent processing
            thaiWords = thaiWords.OrderBy(w => w.RawX1).ThenBy(w => w.RawY1).ToList();

            var mergedWords = ProcessThaiWords(thaiWords, verticalGapTolerance, horizontalOverlapThreshold, document);
            InspectThaiCharacters(document);

            // Replace Thai words with merged versions, keep non-Thai words unchanged
            var nonThaiWords = document.Words.Where(w => !IsThaiWord(w.Text)).ToList();
            Console.WriteLine($"🔍 DEBUG: {nonThaiWords.Count} non-Thai words will be kept as-is");

            document.Words = mergedWords.Concat(nonThaiWords)
                                      .OrderBy(w => w.RawY1)
                                      .ThenBy(w => w.RawX1)
                                      .ToList();

            // Log results
            var mergedCount = mergedWords.Count;
            var reduction = originalThaiCount - mergedCount;

            document.AddMetadata("ThaiMergeApplied", "true");
            document.AddMetadata("ThaiWordsOriginal", originalThaiCount.ToString());
            document.AddMetadata("ThaiWordsMerged", mergedCount.ToString());
            document.AddMetadata("ThaiFragmentsReduced", reduction.ToString());

            Console.WriteLine($"🇹🇭 Thai merge: {originalThaiCount} → {mergedCount} words (reduced {reduction} fragments)");

            if (reduction > 0)
            {
                Console.WriteLine($"✅ DEBUG: Successfully merged some fragments! Showing first few merged words:");
                foreach (var merged in mergedWords.Take(5))
                {
                    Console.WriteLine($"   Merged: '{merged.Text}' (length: {merged.Text.Length})");
                }
            }
            else
            {
                Console.WriteLine($"🤔 DEBUG: No fragments were merged. Reasons could be:");
                Console.WriteLine($"   - Text is already well-formed (not fragmented)");
                Console.WriteLine($"   - Parameters too strict (verticalGapTolerance={verticalGapTolerance}, horizontalOverlap={horizontalOverlapThreshold})");
                Console.WriteLine($"   - Components not positioned as expected");
            }
        }

        public static void InspectThaiCharacters(SpatialDocument document)
        {
            Console.WriteLine("=== THAI CHARACTER INSPECTION ===");

            var thaiWords = document.Words.Where(w => IsThaiWord(w.Text)).ToList();

            foreach (var word in thaiWords.Take(10)) // First 10 Thai words
            {
                Console.WriteLine($"Word: '{word.Text}' (Length: {word.Text.Length})");

                for (int i = 0; i < word.Text.Length; i++)
                {
                    var c = word.Text[i];
                    var unicode = ((int)c).ToString("X4");
                    var category = char.GetUnicodeCategory(c);

                    Console.WriteLine($"  [{i}] '{c}' U+{unicode} ({category})");
                }
                Console.WriteLine();
            }
        }

        private static List<WordBoundingBox> ProcessThaiWords(List<WordBoundingBox> thaiWords,
            int verticalGapTolerance, double horizontalOverlapThreshold, SpatialDocument document)
        {
            var mergedWords = new List<WordBoundingBox>();
            var processed = new HashSet<WordBoundingBox>();

            Console.WriteLine($"🔍 DEBUG: Processing {thaiWords.Count} Thai words for merging...");

            foreach (var word in thaiWords)
            {
                if (processed.Contains(word)) continue;

                var cluster = new List<WordBoundingBox> { word };
                processed.Add(word);

                // Find components to merge with this word
                FindNearbyComponents(word, thaiWords, cluster, processed, verticalGapTolerance, horizontalOverlapThreshold);

                if (cluster.Count > 1)
                {
                    Console.WriteLine($"📦 DEBUG: Found cluster of {cluster.Count} components to merge:");
                    foreach (var c in cluster)
                    {
                        Console.WriteLine($"   Component: '{c.Text}' at ({c.RawX1},{c.RawY1})");
                    }
                }

                // Merge the cluster
                mergedWords.Add(MergeCluster(cluster, document));
            }

            return mergedWords;
        }

        private static void FindNearbyComponents(WordBoundingBox baseWord, List<WordBoundingBox> candidates,
            List<WordBoundingBox> cluster, HashSet<WordBoundingBox> processed,
            int verticalGapTolerance, double horizontalOverlapThreshold)
        {
            foreach (var candidate in candidates)
            {
                if (processed.Contains(candidate)) continue;

                if (ShouldMerge(baseWord, candidate, verticalGapTolerance, horizontalOverlapThreshold))
                {
                    Console.WriteLine($"🔗 DEBUG: Merging '{baseWord.Text}' with '{candidate.Text}'");
                    cluster.Add(candidate);
                    processed.Add(candidate);

                    // Recursively find more components
                    FindNearbyComponents(candidate, candidates, cluster, processed, verticalGapTolerance, horizontalOverlapThreshold);
                }
            }
        }

        private static bool ShouldMerge(WordBoundingBox a, WordBoundingBox b,
            int verticalGapTolerance, double horizontalOverlapThreshold)
        {
            var horizontalOverlap = CalculateHorizontalOverlap(a, b);
            var verticalGap = Math.Abs(a.RawCenterY - b.RawCenterY);

            // Debug the decision
            var shouldMerge = horizontalOverlap >= horizontalOverlapThreshold && verticalGap <= verticalGapTolerance;

            if (!shouldMerge)
            {
                Console.WriteLine($"❌ DEBUG: Not merging '{a.Text}' + '{b.Text}' - overlap:{horizontalOverlap:F2} (need {horizontalOverlapThreshold}), vGap:{verticalGap} (need <={verticalGapTolerance})");
            }

            return shouldMerge;
        }

        private static double CalculateHorizontalOverlap(WordBoundingBox a, WordBoundingBox b)
        {
            var overlapStart = Math.Max(a.RawX1, b.RawX1);
            var overlapEnd = Math.Min(a.RawX2, b.RawX2);
            var overlapWidth = Math.Max(0, overlapEnd - overlapStart);

            var minWidth = Math.Min(a.RawWidth, b.RawWidth);
            return minWidth > 0 ? (double)overlapWidth / minWidth : 0;
        }

        private static WordBoundingBox MergeCluster(List<WordBoundingBox> cluster, SpatialDocument document)
        {
            if (cluster.Count == 1) return cluster[0];

            // Sort by Y position for correct text order (top to bottom)
            var sorted = cluster.OrderBy(w => w.RawY1).ToList();
            var combinedText = string.Join("", sorted.Select(w => w.Text));

            Console.WriteLine($"🔗 DEBUG: Merged cluster into: '{combinedText}'");

            return new WordBoundingBox
            {
                Text = combinedText,
                RawX1 = cluster.Min(w => w.RawX1),
                RawY1 = cluster.Min(w => w.RawY1),
                RawX2 = cluster.Max(w => w.RawX2),
                RawY2 = cluster.Max(w => w.RawY2),
                Confidence = cluster.Average(w => w.Confidence),
                OriginalIndex = cluster.First().OriginalIndex,
                NormX1 = document.DocumentWidth > 0 ? cluster.Min(w => w.RawX1) / (float)document.DocumentWidth : 0,
                NormY1 = document.DocumentHeight > 0 ? cluster.Min(w => w.RawY1) / (float)document.DocumentHeight : 0,
                NormX2 = document.DocumentWidth > 0 ? cluster.Max(w => w.RawX2) / (float)document.DocumentWidth : 0,
                NormY2 = document.DocumentHeight > 0 ? cluster.Max(w => w.RawY2) / (float)document.DocumentHeight : 0
            };
        }

        private static bool IsThaiWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.Any(c => c >= '\u0E00' && c <= '\u0E7F');
        }
    }
}
