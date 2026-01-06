// NewwaysAdmin.SharedModels/BankSlips/BankSlipProjectIndexEntry.cs

namespace NewwaysAdmin.SharedModels.BankSlips;

/// <summary>
/// Index entry for fast searching of bank slip project data.
/// Contains fields not searchable from filename (category, status flags, amounts).
/// Stored in BankSlipsJson/ProjectIndex.json
/// </summary>
public class BankSlipProjectIndexEntry
{
    /// <summary>
    /// Project ID (matches filename and BankSlipProject.ProjectId)
    /// Example: "KBIZ_Amy_01_01_2026_19_13_27"
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    #region From Structured Memo (Searchable)

    /// <summary>
    /// Category name from parsed memo (for filtering by category)
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// SubCategory name from parsed memo (for filtering by subcategory)
    /// </summary>
    public string? SubCategoryName { get; set; }

    /// <summary>
    /// Location name from parsed memo (for filtering by location)
    /// </summary>
    public string? LocationName { get; set; }

    /// <summary>
    /// Person name from parsed memo (for filtering by person)
    /// </summary>
    public string? PersonName { get; set; }

    #endregion

    #region Status Flags (Searchable)

    /// <summary>
    /// Whether the note field had valid structured format
    /// </summary>
    public bool HasStructuralNote { get; set; }

    /// <summary>
    /// Whether this is a private/personal transaction
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// VAT status (null = needs review)
    /// </summary>
    public bool? HasVat { get; set; }

    /// <summary>
    /// Whether the project is closed (all info collected)
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// Whether bill/receipt exists
    /// </summary>
    public bool HasBill { get; set; }

    #endregion

    #region Quick Reference (Avoid Opening File for Lists)

    /// <summary>
    /// Transaction amount from OCR "Total" field (parsed to decimal)
    /// Null if parsing failed
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Recipient name from OCR "To" field
    /// </summary>
    public string? RecipientName { get; set; }

    #endregion

    #region Factory Method

    /// <summary>
    /// Create index entry from a BankSlipProject
    /// </summary>
    public static BankSlipProjectIndexEntry FromProject(BankSlipProject project)
    {
        decimal? amount = null;
        var totalStr = project.GetTotal();
        if (!string.IsNullOrWhiteSpace(totalStr))
        {
            // Try to parse amount (remove commas, currency symbols)
            var cleanAmount = totalStr
                .Replace(",", "")
                .Replace("฿", "")
                .Replace("THB", "")
                .Trim();

            if (decimal.TryParse(cleanAmount, out var parsed))
            {
                amount = parsed;
            }
        }

        return new BankSlipProjectIndexEntry
        {
            ProjectId = project.ProjectId,

            // From structured memo
            CategoryName = project.StructuredMemo?.CategoryName,
            SubCategoryName = project.StructuredMemo?.SubCategoryName,
            LocationName = project.StructuredMemo?.LocationName,
            PersonName = project.StructuredMemo?.PersonName,

            // Status flags
            HasStructuralNote = project.HasStructuralNote,
            IsPrivate = project.IsPrivate,
            HasVat = project.HasVat,
            IsClosed = project.IsClosed,
            HasBill = project.HasBill,

            // Quick reference
            Amount = amount,
            RecipientName = project.GetRecipient()
        };
    }

    #endregion
}