// NewwaysAdmin.SharedModels/Models/Ocr/Core/OcrExtractionResult.cs
using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Result of spatial OCR extraction process
    /// Contains the extracted document, processing metadata, and error information
    /// </summary>
    [MessagePackObject]
    public class OcrExtractionResult
    {
        [Key(0)]
        public bool Success { get; set; } = false;

        [Key(1)]
        public SpatialDocument? Document { get; set; }

        [Key(2)]
        public string ErrorMessage { get; set; } = string.Empty;

        [Key(3)]
        public List<string> Warnings { get; set; } = new();

        [Key(4)]
        public TimeSpan ProcessingTime { get; set; }

        [Key(5)]
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        [Key(6)]
        public string SourceImagePath { get; set; } = string.Empty;

        [Key(7)]
        public string ProcessedImagePath { get; set; } = string.Empty;

        [Key(8)]
        public OcrProcessingMetadata Metadata { get; set; } = new();

        // Helper properties
        [IgnoreMember]
        public bool HasWarnings => Warnings.Count > 0;

        [IgnoreMember]
        public int WordCount => Document?.WordCount ?? 0;

        [IgnoreMember]
        public float AverageConfidence => Document?.AverageConfidence ?? 0.0f;

        [IgnoreMember]
        public string ExtractedText => Document?.AllText ?? string.Empty;

        /// <summary>
        /// Add a warning message
        /// </summary>
        public void AddWarning(string warning)
        {
            if (!string.IsNullOrEmpty(warning))
            {
                Warnings.Add($"[{DateTime.Now:HH:mm:ss}] {warning}");
            }
        }

        /// <summary>
        /// Mark the extraction as failed with error message
        /// </summary>
        public void SetError(string error)
        {
            Success = false;
            ErrorMessage = error;
        }

        /// <summary>
        /// Mark the extraction as successful with document
        /// </summary>
        public void SetSuccess(SpatialDocument document)
        {
            Success = true;
            Document = document;
            ErrorMessage = string.Empty;
        }

        /// <summary>
        /// Get summary information for logging
        /// </summary>
        public string GetSummary()
        {
            if (!Success)
            {
                return $"FAILED: {ErrorMessage}";
            }

            var warningText = HasWarnings ? $" ({Warnings.Count} warnings)" : "";
            return $"SUCCESS: {WordCount} words extracted in {ProcessingTime.TotalMilliseconds:F0}ms{warningText}";
        }

        /// <summary>
        /// Get detailed information for debugging
        /// </summary>
        public string GetDetailedInfo()
        {
            var info = new List<string>
            {
                $"Status: {(Success ? "SUCCESS" : "FAILED")}",
                $"Processing Time: {ProcessingTime.TotalMilliseconds:F2}ms",
                $"Source: {System.IO.Path.GetFileName(SourceImagePath)}",
                $"Processed At: {ProcessedAt:yyyy-MM-dd HH:mm:ss}"
            };

            if (Success && Document != null)
            {
                info.Add($"Words Extracted: {WordCount}");
                info.Add($"Average Confidence: {AverageConfidence:F2}");
                info.Add($"Document Size: {Document.DocumentWidth}x{Document.DocumentHeight}");

                if (Document.Metadata.Count > 0)
                {
                    info.Add($"Metadata: {Document.Metadata.Count} entries");
                }
            }

            if (!Success)
            {
                info.Add($"Error: {ErrorMessage}");
            }

            if (HasWarnings)
            {
                info.Add($"Warnings: {string.Join("; ", Warnings)}");
            }

            return string.Join(Environment.NewLine, info);
        }
    }

    /// <summary>
    /// Metadata about the OCR processing operation
    /// </summary>
    [MessagePackObject]
    public class OcrProcessingMetadata
    {
        [Key(0)]
        public string GoogleVisionApiVersion { get; set; } = string.Empty;

        [Key(1)]
        public string CollectionName { get; set; } = string.Empty;

        [Key(2)]
        public string ProcessingEngine { get; set; } = "SpatialOcrService";

        [Key(3)]
        public bool ImageWasPreprocessed { get; set; } = false;

        [Key(4)]
        public Dictionary<string, string> PreprocessingSettings { get; set; } = new();

        [Key(5)]
        public int OriginalImageWidth { get; set; } = 0;

        [Key(6)]
        public int OriginalImageHeight { get; set; } = 0;

        [Key(7)]
        public int ProcessedImageWidth { get; set; } = 0;

        [Key(8)]
        public int ProcessedImageHeight { get; set; } = 0;

        [Key(9)]
        public float ImageScaleFactor { get; set; } = 1.0f;

        [Key(10)]
        public int RawAnnotationsCount { get; set; } = 0;

        [Key(11)]
        public int FilteredWordsCount { get; set; } = 0;

        [Key(12)]
        public Dictionary<string, object> AdditionalData { get; set; } = new();

        /// <summary>
        /// Add additional metadata
        /// </summary>
        public void AddData(string key, object value)
        {
            AdditionalData[key] = value;
        }

        /// <summary>
        /// Get preprocessing information summary
        /// </summary>
        public string GetPreprocessingSummary()
        {
            if (!ImageWasPreprocessed)
            {
                return "No preprocessing applied";
            }

            var settings = string.Join(", ", PreprocessingSettings.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            return $"Preprocessed: {settings}";
        }
    }
}