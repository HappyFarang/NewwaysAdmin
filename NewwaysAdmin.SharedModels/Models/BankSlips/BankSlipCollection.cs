//NewwaysAdmin.SharedModels/BankSlips/BankSlipCollection.cs

using System.ComponentModel.DataAnnotations;

namespace NewwaysAdmin.SharedModels.BankSlips;

/// <summary>
/// Represents a collection of bank slip files from an external folder (NAS, network drive, local folder)
/// that will be automatically monitored and processed via OCR
/// </summary>
public class BankSlipCollection
{
    /// <summary>
    /// Unique identifier for the collection
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Human-readable name for the collection (e.g. "Company_KBIZ", "John_Personal_KPlus")
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of what this collection contains
    /// </summary>
    [StringLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the external folder to monitor (e.g. \\NAS\BankSlips\2024\Company)
    /// </summary>
    [Required]
    public string ExternalFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// File extensions that should be processed (e.g. .jpg, .png, .pdf)
    /// </summary>
    public string[] SupportedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".pdf"];

    /// <summary>
    /// List of user IDs who can access this collection's processed results
    /// </summary>
    public List<string> AuthorizedUserIds { get; set; } = new();

    /// <summary>
    /// When this collection was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;



    /// <summary>
    /// Whether this collection is actively being monitored and processed
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last time the external folder was scanned for changes
    /// </summary>
    public DateTime? LastScanned { get; set; }

    /// <summary>
    /// Last time a file was processed from this collection
    /// </summary>
    public DateTime? LastProcessed { get; set; }

    /// <summary>
    /// Number of files successfully processed from this collection
    /// </summary>
    public int ProcessedFileCount { get; set; } = 0;

    /// <summary>
    /// Number of files that failed to process
    /// </summary>
    public int FailedFileCount { get; set; } = 0;
}