// NewwaysAdmin.SharedModels/BankSlips/ParsedMemo.cs

namespace NewwaysAdmin.SharedModels.BankSlips;

/// <summary>
/// Structured data parsed from bank slip note field
/// Format: "Location: X - Person: Y - Category: A + B - VAT - Memo: Z"
/// </summary>
public class ParsedMemo
{
    /// <summary>
    /// Location name from "Location: X" segment
    /// </summary>
    public string? LocationName { get; set; }

    /// <summary>
    /// Person name from "Person: Y" segment
    /// </summary>
    public string? PersonName { get; set; }

    /// <summary>
    /// Category name from "Category: A + B" segment (the "A" part)
    /// </summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// SubCategory name from "Category: A + B" segment (the "B" part)
    /// </summary>
    public string? SubCategoryName { get; set; }

    /// <summary>
    /// Free-text memo from "Memo: Z" segment
    /// </summary>
    public string? Memo { get; set; }
}