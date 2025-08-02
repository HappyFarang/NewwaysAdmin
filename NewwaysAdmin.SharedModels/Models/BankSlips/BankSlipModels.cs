// NewwaysAdmin.SharedModels/Models/BankSlips/BankSlipModels.cs
// Enhanced SlipCollection with K-BIZ format support

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

        // NEW: K-BIZ format support
        [Key(10)]
        public bool IsKBizFormat { get; set; } = false;

        // Helper method to get format display name
        [IgnoreMember]
        public string FormatDisplayName => IsKBizFormat ? "K-BIZ Format" : "Original Format";

        // Helper method to get format icon
        [IgnoreMember]
        public string FormatIcon => IsKBizFormat ? "bi-bank2" : "bi-bank";
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
    /// <summary>
    /// Enum for tracking bank slip processing status
    /// </summary>
    public enum BankSlipProcessingStatus
    {
        /// <summary>
        /// Processing not started yet
        /// </summary>
        Pending = 0,

        /// <summary>
        /// Currently being processed
        /// </summary>
        Processing = 1,

        /// <summary>
        /// Successfully completed processing
        /// </summary>
        Completed = 2,

        /// <summary>
        /// Failed during processing
        /// </summary>
        Failed = 3,

        /// <summary>
        /// Completed but needs manual review
        /// </summary>
        RequiresReview = 4,

        /// <summary>
        /// Skipped during processing
        /// </summary>
        Skipped = 5
    }

    /// <summary>
    /// Enum for different image processing passes
    /// </summary>
    public enum ProcessingPass
    {
        /// <summary>
        /// Default processing settings
        /// </summary>
        Default = 0,

        /// <summary>
        /// Fallback processing with enhanced settings
        /// </summary>
        Fallback = 1,

        /// <summary>
        /// Optimized for tablet-captured images
        /// </summary>
        Tablet = 2
    }
    // Enhanced processing parameters with format-specific settings
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

        // NEW: Format-specific processing options
        [Key(6)]
        public bool UseEnhancedDateValidation { get; set; } = true;

        [Key(7)]
        public bool ExtractDualLanguageNames { get; set; } = false; // Auto-set for K-BIZ

        [Key(8)]
        public bool ValidateAccountFormat { get; set; } = true;
    }

    // Enhanced bank slip data with parsing metadata
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

        // NEW: Parsing metadata
        [Key(14)]
        public string ParserUsed { get; set; } = string.Empty;

        [Key(15)]
        public bool IsKBizFormat { get; set; } = false;

        [Key(16)]
        public string ReceiverNameEnglish { get; set; } = string.Empty; // For K-BIZ dual language

        [Key(17)]
        public Dictionary<string, string> ParsingNotes { get; set; } = new();

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
    }

    // Migration helper for existing collections
    public static class SlipCollectionMigration
    {
        public static void MigrateToV2(SlipCollection collection)
        {
            // Set default values for new properties
            if (collection.ProcessingSettings != null)
            {
                collection.ProcessingSettings.UseEnhancedDateValidation = true;
                collection.ProcessingSettings.ExtractDualLanguageNames = collection.IsKBizFormat;
                collection.ProcessingSettings.ValidateAccountFormat = true;
            }
        }
    }
    /// <summary>
    /// Result of bank slip processing operation
    /// </summary>
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

    /// <summary>
    /// Summary statistics for processing operation
    /// </summary>
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

    /// <summary>
    /// Error that occurred during processing
    /// </summary>
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
}