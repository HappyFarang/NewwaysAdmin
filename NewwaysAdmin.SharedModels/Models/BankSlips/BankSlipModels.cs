// NewwaysAdmin.SharedModels/Models/BankSlips/BankSlipModels.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using MessagePack;
using Key = MessagePack.KeyAttribute; // This resolves the ambiguous reference

namespace NewwaysAdmin.SharedModels.BankSlips
{
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
    }

    public enum BankSlipProcessingStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Failed = 3,
        Skipped = 4
    }

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
    }

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
    }

    public enum ProcessingPass
    {
        Default = 0,
        Fallback = 1,
        Tablet = 2
    }

    [MessagePackObject]
    public class BankSlipProcessingResult
    {
        [Key(0)]
        public List<BankSlipData> ProcessedSlips { get; set; } = new();

        [Key(1)]
        public List<ProcessingError> Errors { get; set; } = new();

        [Key(2)]
        public ProcessingSummary Summary { get; set; } = new();

        [Key(3)]
        public DateTime ProcessingStarted { get; set; }

        [Key(4)]
        public DateTime ProcessingCompleted { get; set; }
    }

    [MessagePackObject]
    public class ProcessingError
    {
        [Key(0)]
        public string FilePath { get; set; } = string.Empty;

        [Key(1)]
        public string Reason { get; set; } = string.Empty;

        [Key(2)]
        public ProcessingPass FailedPass { get; set; }

        [Key(3)]
        public DateTime ErrorTime { get; set; }
    }

    [MessagePackObject]
    public class ProcessingSummary
    {
        [Key(0)]
        public int TotalFiles { get; set; }

        [Key(1)]
        public int ProcessedFiles { get; set; }

        [Key(2)]
        public int FailedFiles { get; set; }

        [Key(3)]
        public int DateOutOfRangeFiles { get; set; }

        [Key(4)]
        public int OcrFailures { get; set; }

        [Key(5)]
        public int GhostFiles { get; set; }

        [Key(6)]
        public TimeSpan ProcessingDuration { get; set; }
    }

    public class UserBankSlipConfig
    {
        public List<SlipCollection> Collections { get; set; } = new();
        public string DefaultCredentialsPath { get; set; } = string.Empty;
        public string DefaultOutputDirectory { get; set; } = string.Empty;
    }
}