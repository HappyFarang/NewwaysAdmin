// NewwaysAdmin.SharedModels/Models/Ocr/Core/WordBoundingBox.cs
using System;
using MessagePack;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Represents a single word or text element with its spatial coordinates
    /// Contains both raw pixel coordinates and normalized coordinates for flexibility
    /// </summary>
    [MessagePackObject]
    public class WordBoundingBox
    {
        [Key(0)]
        public string Text { get; set; } = string.Empty;

        [Key(1)]
        public float Confidence { get; set; } = 0.0f;

        // Raw pixel coordinates from Google Vision
        [Key(2)]
        public int RawX1 { get; set; }

        [Key(3)]
        public int RawY1 { get; set; }

        [Key(4)]
        public int RawX2 { get; set; }

        [Key(5)]
        public int RawY2 { get; set; }

        // Normalized coordinates (0.0 to 1.0) for resolution-independent processing
        [Key(6)]
        public float NormX1 { get; set; }

        [Key(7)]
        public float NormY1 { get; set; }

        [Key(8)]
        public float NormX2 { get; set; }

        [Key(9)]
        public float NormY2 { get; set; }

        // Word index in the original OCR response (for debugging/tracing)
        [Key(10)]
        public int OriginalIndex { get; set; }

        // Helper properties for common calculations
        [IgnoreMember]
        public int RawWidth => RawX2 - RawX1;

        [IgnoreMember]
        public int RawHeight => RawY2 - RawY1;

        [IgnoreMember]
        public float NormWidth => NormX2 - NormX1;

        [IgnoreMember]
        public float NormHeight => NormY2 - NormY1;

        [IgnoreMember]
        public int RawCenterX => RawX1 + (RawWidth / 2);

        [IgnoreMember]
        public int RawCenterY => RawY1 + (RawHeight / 2);

        [IgnoreMember]
        public float NormCenterX => NormX1 + (NormWidth / 2f);

        [IgnoreMember]
        public float NormCenterY => NormY1 + (NormHeight / 2f);

        /// <summary>
        /// Check if this word is horizontally aligned with another word within tolerance
        /// </summary>
        public bool IsHorizontallyAlignedWith(WordBoundingBox other, int tolerance = 10)
        {
            if (other == null) return false;
            return Math.Abs(RawCenterY - other.RawCenterY) <= tolerance;
        }

        /// <summary>
        /// Check if this word is vertically aligned with another word within tolerance
        /// </summary>
        public bool IsVerticallyAlignedWith(WordBoundingBox other, int tolerance = 20)
        {
            if (other == null) return false;
            return Math.Abs(RawCenterX - other.RawCenterX) <= tolerance;
        }

        /// <summary>
        /// Calculate horizontal distance between this word and another
        /// </summary>
        public int HorizontalDistanceTo(WordBoundingBox other)
        {
            if (other == null) return int.MaxValue;

            // Distance between closest edges
            if (RawX2 < other.RawX1) return other.RawX1 - RawX2; // Other is to the right
            if (other.RawX2 < RawX1) return RawX1 - other.RawX2; // Other is to the left
            return 0; // Overlapping horizontally
        }

        /// <summary>
        /// Calculate vertical distance between this word and another
        /// </summary>
        public int VerticalDistanceTo(WordBoundingBox other)
        {
            if (other == null) return int.MaxValue;

            // Distance between closest edges
            if (RawY2 < other.RawY1) return other.RawY1 - RawY2; // Other is below
            if (other.RawY2 < RawY1) return RawY1 - other.RawY2; // Other is above
            return 0; // Overlapping vertically
        }

        /// <summary>
        /// Check if this word overlaps with another word
        /// </summary>
        public bool OverlapsWith(WordBoundingBox other)
        {
            if (other == null) return false;

            return !(RawX2 < other.RawX1 ||
                    other.RawX2 < RawX1 ||
                    RawY2 < other.RawY1 ||
                    other.RawY2 < RawY1);
        }

        /// <summary>
        /// Get area of this word's bounding box
        /// </summary>
        public int GetArea() => RawWidth * RawHeight;

        public override string ToString()
        {
            return $"'{Text}' at ({RawX1},{RawY1})-({RawX2},{RawY2}) conf:{Confidence:F2}";
        }
    }
}