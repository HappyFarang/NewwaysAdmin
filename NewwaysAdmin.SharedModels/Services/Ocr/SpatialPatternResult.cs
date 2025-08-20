// NewwaysAdmin.SharedModels/Models/Ocr/SpatialPatternResult.cs
// 🔧 Missing class needed for SpatialPatternMatcher

using NewwaysAdmin.SharedModels.Models.Ocr.Core;

namespace NewwaysAdmin.SharedModels.Models.Ocr
{
    /// <summary>
    /// Result from spatial pattern extraction
    /// </summary>
    public class SpatialPatternResult
    {
        /// <summary>
        /// Whether the pattern extraction was successful
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// The anchor word that was found
        /// </summary>
        public WordBoundingBox? AnchorWord { get; set; }

        /// <summary>
        /// All words that were grouped together (including anchor)
        /// </summary>
        public List<WordBoundingBox> GroupedWords { get; set; } = new();

        /// <summary>
        /// Combined text from all grouped words
        /// </summary>
        public string CombinedText { get; set; } = "";

        /// <summary>
        /// Get extracted text without the anchor word
        /// </summary>
        public string GetExtractedTextOnly()
        {
            if (AnchorWord == null)
                return CombinedText;

            var wordsWithoutAnchor = GroupedWords.Where(w => w != AnchorWord).ToList();
            return string.Join(" ", wordsWithoutAnchor.Select(w => w.Text));
        }

        /// <summary>
        /// Additional metadata about the extraction
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// Add metadata entry
        /// </summary>
        public void AddMetadata(string key, object value)
        {
            Metadata[key] = value;
        }
    }
}