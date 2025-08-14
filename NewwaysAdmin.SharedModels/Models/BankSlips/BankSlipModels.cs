// NewwaysAdmin.SharedModels/Models/BankSlips/BankSlipModels.cs
// 🚀 UPDATED: Enhanced SlipCollection with pattern-based support

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using MessagePack;
using Key = MessagePack.KeyAttribute;

namespace NewwaysAdmin.SharedModels.BankSlips
{
    [MessagePackObject]
    public class SlipCollection
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        [Required(ErrorMessage = "Collection name is required")]
        [StringLength(100, ErrorMessage = "Collection name cannot exceed 100 characters")]
        public string Name { get; set; } = string.Empty;

        [Key(2)]
        [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
        public string Description { get; set; } = string.Empty;

        [Key(3)]
        [Required(ErrorMessage = "Source directory is required")]
        [StringLength(500, ErrorMessage = "Source directory path cannot exceed 500 characters")]
        public string SourceDirectory { get; set; } = string.Empty;

        [Key(4)]
        [Required(ErrorMessage = "Output directory is required")]
        [StringLength(500, ErrorMessage = "Output directory path cannot exceed 500 characters")]
        public string OutputDirectory { get; set; } = string.Empty;

        [Key(5)]
        [StringLength(500, ErrorMessage = "Credentials path cannot exceed 500 characters")]
        public string CredentialsPath { get; set; } = @"C:\Keys\purrfectocr-db2d9d796b58.json";

        [Key(6)]
        public string CreatedBy { get; set; } = string.Empty;

        [Key(7)]
        public DateTime CreatedAt { get; set; }

        [Key(8)]
        public bool IsActive { get; set; } = true;

        [Key(9)]
        public ProcessingParameters ProcessingSettings { get; set; } = new();

        // Legacy format support (for backward compatibility)
        [Key(10)]
        public bool IsKBizFormat { get; set; } = false;

        // 🆕 NEW: Pattern-based system fields
        [Key(11)]
        [Required(ErrorMessage = "Document type is required")]
        public string DocumentType { get; set; } = "BankSlips";

        [Key(12)]
        [Required(ErrorMessage = "Format name is required")]
        public string FormatName { get; set; } = string.Empty;

        [Key(13)]
        public bool AutoProcessNewFiles { get; set; } = false;

        // Helper properties for display and compatibility
        [IgnoreMember]
        public string FormatDisplayName =>
            !string.IsNullOrEmpty(FormatName) ? FormatName :
            (IsKBizFormat ? "K-BIZ Format" : "Original Format");

        [IgnoreMember]
        public string FormatIcon =>
            FormatName?.Contains("KBIZ", StringComparison.OrdinalIgnoreCase) == true || IsKBizFormat ?
            "bi-bank2" : "bi-bank";

        [IgnoreMember]
        public string FullPatternPath => $"{DocumentType}/{FormatName}";

        /// <summary>
        /// Migration helper to update legacy collections to pattern-based system
        /// </summary>
        public void MigrateToPatternBased()
        {
            // If DocumentType is empty, set default
            if (string.IsNullOrEmpty(DocumentType))
            {
                DocumentType = "BankSlips";
            }

            // If FormatName is empty but we have legacy format info, migrate
            if (string.IsNullOrEmpty(FormatName))
            {
                FormatName = IsKBizFormat ? "KBIZ" : "Original";
            }

            // Update processing settings for pattern compatibility
            if (ProcessingSettings == null)
            {
                ProcessingSettings = new ProcessingParameters();
            }

            // Set enhanced settings for known formats
            if (FormatName.Contains("KBIZ", StringComparison.OrdinalIgnoreCase))
            {
                ProcessingSettings.ExtractDualLanguageNames = true;
                ProcessingSettings.UseEnhancedDateValidation = true;
                IsKBizFormat = true; // Maintain backward compatibility
            }
        }

    }

    // Enum for slip format types (for future extensibility)
    public enum SlipFormat
    {
        Original = 0,
        KBiz = 1,
        // Future formats can be added here
        // SCB = 2,
        // TTB = 3,
        // etc.
    }

    // Enhanced processing parameters with pattern-specific settings
    [MessagePackObject]
    public class ProcessingParameters
    {
        [Key(0)]
        public double GaussianSigma { get; set; } = 0.5;

        [Key(1)]
        public int BinarizationWindow { get; set; } = 15;

        [Key(2)]
        public double BinarizationK { get; set; } = 0.2;

        [Key(3)]
        public bool PreserveGrays { get; set; } = true;

        [Key(4)]
        public int BorderSize { get; set; } = 20;

        [Key(5)]
        public ProcessingPass[] ProcessingPasses { get; set; } = new[]
        {
            ProcessingPass.Default,
            ProcessingPass.Fallback,
            ProcessingPass.Tablet
        };

        // Format-specific processing options
        [Key(6)]
        public bool UseEnhancedDateValidation { get; set; } = true;

        [Key(7)]
        public bool ExtractDualLanguageNames { get; set; } = false; // Auto-set for K-BIZ

        [Key(8)]
        public bool ValidateAccountFormat { get; set; } = true;

        // 🆕 NEW: Pattern-specific settings
        [Key(9)]
        public bool EnablePatternDebugging { get; set; } = false;

        [Key(10)]
        public int PatternMatchTolerance { get; set; } = 20;

        [Key(11)]
        public bool UseAdvancedPatternMatching { get; set; } = true;

        [Key(12)]
        public bool EnableAutoProcessing { get; set; } = false;

        [Key(13)]
        public int AutoProcessIntervalMinutes { get; set; } = 5;

        [Key(14)]
        public string[] AutoProcessFileExtensions { get; set; } = new[] { ".jpg", ".jpeg", ".png", ".pdf", ".tiff" };
    }

    // Processing pass enumeration
    public enum ProcessingPass
    {
        Default = 0,
        Fallback = 1,
        Tablet = 2,
        HighContrast = 3
    }

    // Enhanced bank slip data with pattern-based processing metadata
    [MessagePackObject]
    public class BankSlipData
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public DateTime TransactionDate { get; set; }

        [Key(2)]
        public string AccountName { get; set; } = string.Empty;

        [Key(3)]
        public string AccountNumber { get; set; } = string.Empty;

        [Key(4)]
        public string ReceiverName { get; set; } = string.Empty;

        [Key(5)]
        public string ReceiverAccount { get; set; } = string.Empty;

        [Key(6)]
        public decimal Amount { get; set; }

        [Key(7)]
        public string Note { get; set; } = string.Empty;

        [Key(8)]
        public string OriginalFilePath { get; set; } = string.Empty;

        [Key(9)]
        public string ProcessedBy { get; set; } = string.Empty;

        [Key(10)]
        public DateTime ProcessedAt { get; set; }

        [Key(11)]
        public string SlipCollectionName { get; set; } = string.Empty;

        [Key(12)]
        public BankSlipProcessingStatus Status { get; set; }

        [Key(13)]
        public string ErrorReason { get; set; } = string.Empty;

        // Parsing metadata
        [Key(14)]
        public string ParserUsed { get; set; } = string.Empty;

        [Key(15)]
        public bool IsKBizFormat { get; set; } = false;

        [Key(16)]
        public string ReceiverNameEnglish { get; set; } = string.Empty; // For K-BIZ dual language

        [Key(17)]
        public Dictionary<string, string> ParsingNotes { get; set; } = new();

        // 🆕 NEW: Pattern-based processing metadata
        [Key(18)]
        public string DocumentType { get; set; } = string.Empty;

        [Key(19)]
        public string FormatName { get; set; } = string.Empty;

        [Key(20)]
        public int ExtractedFieldCount { get; set; } = 0;

        [Key(21)]
        public List<string> SuccessfulPatterns { get; set; } = new();

        [Key(22)]
        public List<string> FailedPatterns { get; set; } = new();

        // Helper method to get combined receiver name
        [IgnoreMember]
        public string CombinedReceiverName
        {
            get
            {
                if (IsKBizFormat && !string.IsNullOrEmpty(ReceiverNameEnglish))
                {
                    return string.IsNullOrEmpty(ReceiverName)
                        ? ReceiverNameEnglish
                        : $"{ReceiverName} / {ReceiverNameEnglish}";
                }
                return ReceiverName;
            }
        }

        [IgnoreMember]
        public string PatternPath => $"{DocumentType}/{FormatName}";

        [IgnoreMember]
        public double PatternSuccessRate =>
            (SuccessfulPatterns.Count + FailedPatterns.Count) > 0 ?
            (double)SuccessfulPatterns.Count / (SuccessfulPatterns.Count + FailedPatterns.Count) * 100 : 0;
    }

    // Processing status enumeration
    public enum BankSlipProcessingStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        RequiresReview = 4
    }

    // Result of bank slip processing operation
    public class BankSlipProcessingResult
    {
        public DateTime ProcessingStarted { get; set; } = DateTime.UtcNow;
        public DateTime ProcessingCompleted { get; set; }
        public ProcessingSummary Summary { get; set; } = new();
        public List<BankSlipData> ProcessedSlips { get; set; } = new();
        public List<ProcessingError> Errors { get; set; } = new();

        /// <summary>
        /// Total processing duration
        /// </summary>
        public TimeSpan ProcessingDuration => ProcessingCompleted - ProcessingStarted;

        /// <summary>
        /// Whether processing completed successfully (no critical errors)
        /// </summary>
        public bool IsSuccessful => Errors.Count == 0 || Errors.All(e => !e.IsCritical);
    }

    // Summary statistics for processing operation
    public class ProcessingSummary
    {
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int FailedFiles { get; set; }
        public TimeSpan ProcessingDuration { get; set; }

        /// <summary>
        /// Success rate as percentage
        /// </summary>
        public double SuccessRate => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;

        /// <summary>
        /// Files remaining to process
        /// </summary>
        public int RemainingFiles => TotalFiles - ProcessedFiles - FailedFiles;
    }

    // Error that occurred during processing
    public class ProcessingError
    {
        public string FilePath { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime ErrorTime { get; set; } = DateTime.UtcNow;
        public bool IsCritical { get; set; } = false;
        public string? StackTrace { get; set; }

        /// <summary>
        /// User-friendly error message
        /// </summary>
        public string FriendlyMessage => $"{Path.GetFileName(FilePath)}: {Reason}";
    }

    // Migration helper for existing collections
    public static class SlipCollectionMigration
    {
        public static void MigrateToPatternBased(SlipCollection collection)
        {
            collection.MigrateToPatternBased();
        }

        /// <summary>
        /// Batch migration for multiple collections
        /// </summary>
        public static void MigrateCollections(List<SlipCollection> collections)
        {
            foreach (var collection in collections)
            {
                MigrateToPatternBased(collection);
            }
        }
    }
}