// NewwaysAdmin.SharedModels/BankSlips/BankSlipProject.cs

namespace NewwaysAdmin.SharedModels.BankSlips;

/// <summary>
/// A processed bank slip project containing OCR results, parsed memo data, and review status.
/// One JSON file per project, stored in BankSlipsJson/Projects/{ProjectId}.json
/// ProjectId matches the source filename (e.g., "KBIZ_Amy_01_01_2026_19_13_27")
/// </summary>
public class BankSlipProject
{
    #region Identity

    /// <summary>
    /// Unique identifier - matches source filename without extension
    /// Example: "KBIZ_Amy_01_01_2026_19_13_27"
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    #endregion

    #region Filename Parsed Data

    /// <summary>
    /// OCR pattern set name parsed from filename (e.g., "KBIZ", "KPlus")
    /// Used to determine which OCR patterns to apply
    /// </summary>
    public string PatternSetName { get; set; } = string.Empty;

    /// <summary>
    /// Username parsed from filename (e.g., "Amy", "Thomas")
    /// The person who made the transaction
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Transaction timestamp parsed from filename (date + time)
    /// </summary>
    public DateTime TransactionTimestamp { get; set; }

    #endregion

    #region OCR Extracted Data

    /// <summary>
    /// All fields extracted by OCR - varies by pattern set
    /// Common keys: Date, Time, To, Total, Fee, Note, TransactionID
    /// Preserves all extracted data regardless of pattern set
    /// </summary>
    public Dictionary<string, string> ExtractedFields { get; set; } = new();

    #endregion

    #region Parsed Memo Data

    /// <summary>
    /// Structured data parsed from the Note field
    /// Null if note was empty or couldn't be parsed
    /// </summary>
    public ParsedMemo? StructuredMemo { get; set; }

    #endregion

    #region Status Flags

    /// <summary>
    /// True if the Note field contained valid structured format
    /// False if note was empty, missing, or couldn't be parsed
    /// </summary>
    public bool HasStructuralNote { get; set; } = false;

    /// <summary>
    /// VAT status from structured memo
    /// true = VAT included, false = No VAT, null = old format or needs review
    /// </summary>
    public bool? HasVat { get; set; } = null;

    /// <summary>
    /// Marked as private/personal transaction during review
    /// Private transactions are excluded from company reporting
    /// </summary>
    public bool IsPrivate { get; set; } = false;

    /// <summary>
    /// Project is complete - all info collected, bills uploaded, verified
    /// When true, remove from ReviewQueue
    /// </summary>
    public bool IsClosed { get; set; } = false;

    /// <summary>
    /// Flag indicating bill/receipt exists (can be true even without uploaded files)
    /// Auto-set to true when bills are uploaded, can be manually toggled
    /// </summary>
    public bool HasBill { get; set; } = false;

    #endregion

    #region Bill References

    /// <summary>
    /// References to uploaded bill/receipt images
    /// Filenames follow pattern: {ProjectId}_1.jpg, {ProjectId}_2.jpg, etc.
    /// </summary>
    public List<string> BillFileReferences { get; set; } = new();

    #endregion

    #region Processing Metadata

    /// <summary>
    /// When the OCR processing was completed
    /// </summary>
    public DateTime ProcessedAt { get; set; }

    /// <summary>
    /// Error message if processing failed, null if successful
    /// </summary>
    public string? ProcessingError { get; set; }

    #endregion

    #region Helper Properties

    /// <summary>
    /// Returns true if OCR processing was successful (no error)
    /// </summary>
    public bool IsProcessingSuccessful => string.IsNullOrEmpty(ProcessingError);

    /// <summary>
    /// Returns true if critical OCR fields are present (To and Total)
    /// </summary>
    public bool HasCriticalFields =>
        ExtractedFields.ContainsKey("To") && !string.IsNullOrWhiteSpace(ExtractedFields["To"]) &&
        ExtractedFields.ContainsKey("Total") && !string.IsNullOrWhiteSpace(ExtractedFields["Total"]);

    /// <summary>
    /// Gets the extracted "Total" value or empty string
    /// </summary>
    public string GetTotal() => ExtractedFields.TryGetValue("Total", out var val) ? val : string.Empty;

    /// <summary>
    /// Gets the extracted "To" (recipient) value or empty string
    /// </summary>
    public string GetRecipient() => ExtractedFields.TryGetValue("To", out var val) ? val : string.Empty;

    /// <summary>
    /// Gets the extracted "Note" value or empty string
    /// </summary>
    public string GetNote() => ExtractedFields.TryGetValue("Note", out var val) ? val : string.Empty;

    #endregion
}