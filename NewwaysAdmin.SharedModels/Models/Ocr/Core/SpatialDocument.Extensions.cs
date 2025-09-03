// NewwaysAdmin.SharedModels/Models/Ocr/Core/SpatialDocument.Extensions.cs

using NewwaysAdmin.SharedModels.Services.Ocr;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Extensions for SpatialDocument to keep the main class clean
    /// </summary>
    public static class SpatialDocumentExtensions
    {
        /// <summary>
        /// Apply Thai text processing to merge fragmented diacritics
        /// </summary>
        public static void ProcessThaiText(this SpatialDocument document)
        {
            ThaiTextProcessor.MergeThaiComponents(document);
        }

        /// <summary>
        /// Apply Thai text processing with custom settings
        /// </summary>
        public static void ProcessThaiText(this SpatialDocument document,
            int verticalGapTolerance, double horizontalOverlapThreshold)
        {
            ThaiTextProcessor.MergeThaiComponents(document, verticalGapTolerance, horizontalOverlapThreshold);
        }

        /// <summary>
        /// Check if document contains Thai content
        /// </summary>
        public static bool HasThaiContent(this SpatialDocument document)
        {
            return document.Words.Any(w => w.Text.Any(c => c >= '\u0E00' && c <= '\u0E7F'));
        }
    }
}
