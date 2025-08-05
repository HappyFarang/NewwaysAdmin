// NewwaysAdmin.SharedModels/Models/Ocr/Core/SpatialDocument.cs
using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Represents a document with spatially-positioned words
    /// Provides methods for querying words by location and relationships
    /// </summary>
    [MessagePackObject]
    public class SpatialDocument
    {
        [Key(0)]
        public List<WordBoundingBox> Words { get; set; } = new();

        [Key(1)]
        public int DocumentWidth { get; set; } = 0;

        [Key(2)]
        public int DocumentHeight { get; set; } = 0;

        [Key(3)]
        public string SourceImagePath { get; set; } = string.Empty;

        [Key(4)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Key(5)]
        public Dictionary<string, string> Metadata { get; set; } = new();

        // Helper properties
        [IgnoreMember]
        public int WordCount => Words.Count;

        [IgnoreMember]
        public float AverageConfidence => Words.Count > 0 ? Words.Average(w => w.Confidence) : 0.0f;

        [IgnoreMember]
        public string AllText => string.Join(" ", Words.Select(w => w.Text));

        /// <summary>
        /// Find words within a rectangular area (raw pixel coordinates)
        /// </summary>
        public List<WordBoundingBox> GetWordsInArea(int x1, int y1, int x2, int y2)
        {
            return Words.Where(w =>
                w.RawCenterX >= x1 && w.RawCenterX <= x2 &&
                w.RawCenterY >= y1 && w.RawCenterY <= y2
            ).ToList();
        }

        /// <summary>
        /// Find words within a rectangular area (normalized coordinates)
        /// </summary>
        public List<WordBoundingBox> GetWordsInNormalizedArea(float x1, float y1, float x2, float y2)
        {
            return Words.Where(w =>
                w.NormCenterX >= x1 && w.NormCenterX <= x2 &&
                w.NormCenterY >= y1 && w.NormCenterY <= y2
            ).ToList();
        }

        /// <summary>
        /// Find words horizontally aligned with a reference word
        /// </summary>
        public List<WordBoundingBox> GetHorizontallyAlignedWords(WordBoundingBox reference, int tolerance = 10)
        {
            if (reference == null) return new List<WordBoundingBox>();

            return Words.Where(w => w != reference && w.IsHorizontallyAlignedWith(reference, tolerance))
                       .OrderBy(w => w.RawX1) // Order left to right
                       .ToList();
        }

        /// <summary>
        /// Find words vertically aligned with a reference word
        /// </summary>
        public List<WordBoundingBox> GetVerticallyAlignedWords(WordBoundingBox reference, int tolerance = 20)
        {
            if (reference == null) return new List<WordBoundingBox>();

            return Words.Where(w => w != reference && w.IsVerticallyAlignedWith(reference, tolerance))
                       .OrderBy(w => w.RawY1) // Order top to bottom
                       .ToList();
        }

        /// <summary>
        /// Find words that contain specific text (case-insensitive)
        /// </summary>
        public List<WordBoundingBox> FindWordsByText(string searchText, bool exactMatch = false)
        {
            if (string.IsNullOrEmpty(searchText)) return new List<WordBoundingBox>();

            return Words.Where(w => exactMatch
                ? w.Text.Equals(searchText, StringComparison.OrdinalIgnoreCase)
                : w.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        /// <summary>
        /// Find words that match any of the provided text options
        /// </summary>
        public List<WordBoundingBox> FindWordsByTextOptions(params string[] textOptions)
        {
            if (textOptions == null || textOptions.Length == 0) return new List<WordBoundingBox>();

            return Words.Where(w => textOptions.Any(option =>
                w.Text.Contains(option, StringComparison.OrdinalIgnoreCase)
            )).ToList();
        }

        /// <summary>
        /// Get words within a certain distance from a reference word
        /// </summary>
        public List<WordBoundingBox> GetWordsNear(WordBoundingBox reference, int maxDistance)
        {
            if (reference == null) return new List<WordBoundingBox>();

            return Words.Where(w => w != reference)
                       .Where(w => {
                           var hDist = w.HorizontalDistanceTo(reference);
                           var vDist = w.VerticalDistanceTo(reference);
                           var totalDist = Math.Sqrt(hDist * hDist + vDist * vDist);
                           return totalDist <= maxDistance;
                       })
                       .OrderBy(w => {
                           var hDist = w.HorizontalDistanceTo(reference);
                           var vDist = w.VerticalDistanceTo(reference);
                           return Math.Sqrt(hDist * hDist + vDist * vDist);
                       })
                       .ToList();
        }

        /// <summary>
        /// Get words to the right of a reference word (horizontally aligned)
        /// </summary>
        public List<WordBoundingBox> GetWordsToRight(WordBoundingBox reference, int tolerance = 10)
        {
            if (reference == null) return new List<WordBoundingBox>();

            return Words.Where(w => w.RawX1 > reference.RawX2 &&
                               w.IsHorizontallyAlignedWith(reference, tolerance))
                       .OrderBy(w => w.RawX1)
                       .ToList();
        }

        /// <summary>
        /// Get words below a reference word (vertically aligned)
        /// </summary>
        public List<WordBoundingBox> GetWordsBelow(WordBoundingBox reference, int tolerance = 20)
        {
            if (reference == null) return new List<WordBoundingBox>();

            return Words.Where(w => w.RawY1 > reference.RawY2 &&
                               w.IsVerticallyAlignedWith(reference, tolerance))
                       .OrderBy(w => w.RawY1)
                       .ToList();
        }

        /// <summary>
        /// Get basic statistics about word distribution
        /// </summary>
        public SpatialDocumentStats GetStats()
        {
            if (!Words.Any()) return new SpatialDocumentStats();

            return new SpatialDocumentStats
            {
                WordCount = Words.Count,
                AverageConfidence = AverageConfidence,
                MinX = Words.Min(w => w.RawX1),
                MaxX = Words.Max(w => w.RawX2),
                MinY = Words.Min(w => w.RawY1),
                MaxY = Words.Max(w => w.RawY2),
                AverageWordWidth = (int)Words.Average(w => w.RawWidth),
                AverageWordHeight = (int)Words.Average(w => w.RawHeight),
                TotalTextLength = Words.Sum(w => w.Text.Length)
            };
        }

        /// <summary>
        /// Add metadata about the document
        /// </summary>
        public void AddMetadata(string key, string value)
        {
            Metadata[key] = value;
        }

        /// <summary>
        /// Get debug information as formatted string
        /// </summary>
        public string GetDebugInfo()
        {
            var stats = GetStats();
            return $"SpatialDocument: {WordCount} words, " +
                   $"avg confidence: {AverageConfidence:F2}, " +
                   $"bounds: ({stats.MinX},{stats.MinY}) to ({stats.MaxX},{stats.MaxY})";
        }
    }

    /// <summary>
    /// Statistical information about a spatial document
    /// </summary>
    [MessagePackObject]
    public class SpatialDocumentStats
    {
        [Key(0)]
        public int WordCount { get; set; }

        [Key(1)]
        public float AverageConfidence { get; set; }

        [Key(2)]
        public int MinX { get; set; }

        [Key(3)]
        public int MaxX { get; set; }

        [Key(4)]
        public int MinY { get; set; }

        [Key(5)]
        public int MaxY { get; set; }

        [Key(6)]
        public int AverageWordWidth { get; set; }

        [Key(7)]
        public int AverageWordHeight { get; set; }

        [Key(8)]
        public int TotalTextLength { get; set; }
    }
}