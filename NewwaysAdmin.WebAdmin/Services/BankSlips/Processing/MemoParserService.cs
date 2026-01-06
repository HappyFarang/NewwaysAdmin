// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/MemoParserService.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Parses structured memo format from bank slip note field.
/// Expected format: "Location: X - Person: Y - Category: A + B - VAT - Memo: Z"
/// Also handles old format (no VAT segment) and missing/malformed notes.
/// </summary>
public class MemoParserService
{
    private readonly ILogger<MemoParserService> _logger;

    public MemoParserService(ILogger<MemoParserService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse a note field into structured memo data
    /// </summary>
    /// <param name="noteText">The raw note text from OCR</param>
    /// <returns>Parse result with structured data and status</returns>
    public MemoParseResult Parse(string? noteText)
    {
        var result = new MemoParseResult
        {
            RawNoteText = noteText ?? string.Empty
        };

        // Empty or whitespace-only note
        if (string.IsNullOrWhiteSpace(noteText))
        {
            _logger.LogDebug("Note field is empty");
            result.IsStructuralNote = false;
            result.FailureReason = "Note field is empty";
            return result;
        }

        try
        {
            // Split by " - " delimiter
            var segments = noteText.Split(" - ", StringSplitOptions.None);

            // Check for minimum expected segments: Location, Person, Category
            // At minimum we need "Location: X - Person: Y - Category: A + B"
            if (segments.Length < 3)
            {
                _logger.LogDebug("Note has too few segments ({Count}): {Note}", segments.Length, noteText);
                result.IsStructuralNote = false;
                result.FailureReason = "Note does not match structural format (too few segments)";
                return result;
            }

            var parsedMemo = new ParsedMemo();
            bool hasLocation = false;
            bool hasCategory = false;

            foreach (var segment in segments)
            {
                var trimmed = segment.Trim();

                // Location: X
                if (trimmed.StartsWith("Location:", StringComparison.OrdinalIgnoreCase))
                {
                    parsedMemo.LocationName = ExtractValue(trimmed, "Location:");
                    hasLocation = true;
                }
                // Person: Y
                else if (trimmed.StartsWith("Person:", StringComparison.OrdinalIgnoreCase))
                {
                    parsedMemo.PersonName = ExtractValue(trimmed, "Person:");
                }
                // Category: A + B
                else if (trimmed.StartsWith("Category:", StringComparison.OrdinalIgnoreCase))
                {
                    var categoryValue = ExtractValue(trimmed, "Category:");
                    ParseCategoryPath(categoryValue, parsedMemo);
                    hasCategory = true;
                }
                // VAT or NoVAT
                else if (trimmed.Equals("VAT", StringComparison.OrdinalIgnoreCase))
                {
                    result.HasVat = true;
                }
                else if (trimmed.Equals("NoVAT", StringComparison.OrdinalIgnoreCase))
                {
                    result.HasVat = false;
                }
                // Memo: Z (free text - take everything after "Memo:")
                else if (trimmed.StartsWith("Memo:", StringComparison.OrdinalIgnoreCase))
                {
                    parsedMemo.Memo = ExtractValue(trimmed, "Memo:");
                }
            }

            // Validate we got the minimum required fields
            if (!hasLocation || !hasCategory)
            {
                _logger.LogDebug("Note missing required fields (Location: {HasLoc}, Category: {HasCat}): {Note}",
                    hasLocation, hasCategory, noteText);
                result.IsStructuralNote = false;
                result.FailureReason = "Note missing Location and/or Category";
                return result;
            }

            // Success!
            result.IsStructuralNote = true;
            result.ParsedMemo = parsedMemo;

            // Log VAT status for debugging
            if (result.HasVat == null)
            {
                _logger.LogDebug("Parsed note (old format - no VAT segment): {Note}", noteText);
            }
            else
            {
                _logger.LogDebug("Parsed note (VAT={Vat}): {Note}", result.HasVat, noteText);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing note: {Note}", noteText);
            result.IsStructuralNote = false;
            result.FailureReason = $"Parse error: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Extract value after a prefix (e.g., "Location: Office" → "Office")
    /// </summary>
    private string? ExtractValue(string segment, string prefix)
    {
        if (segment.Length <= prefix.Length)
            return null;

        var value = segment.Substring(prefix.Length).Trim();

        // Treat "None" as null (user selected "No Location" etc.)
        if (value.Equals("None", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Parse category path "A + B" into Category and SubCategory
    /// </summary>
    private void ParseCategoryPath(string? categoryPath, ParsedMemo memo)
    {
        if (string.IsNullOrWhiteSpace(categoryPath))
            return;

        // Split by " + " (category + subcategory separator)
        var parts = categoryPath.Split(" + ", 2, StringSplitOptions.None);

        memo.CategoryName = parts[0].Trim();

        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            memo.SubCategoryName = parts[1].Trim();
        }
    }
}

/// <summary>
/// Result of parsing a bank slip memo/note field
/// </summary>
public class MemoParseResult
{
    /// <summary>
    /// The raw note text that was parsed
    /// </summary>
    public string RawNoteText { get; set; } = string.Empty;

    /// <summary>
    /// True if the note matched the structural format and was parsed successfully
    /// </summary>
    public bool IsStructuralNote { get; set; }

    /// <summary>
    /// Parsed structured data (null if IsStructuralNote is false)
    /// </summary>
    public ParsedMemo? ParsedMemo { get; set; }

    /// <summary>
    /// VAT status from note:
    /// true = "VAT" segment found
    /// false = "NoVAT" segment found  
    /// null = old format (no VAT segment) - needs review
    /// </summary>
    public bool? HasVat { get; set; }

    /// <summary>
    /// Why parsing failed (only set if IsStructuralNote is false)
    /// </summary>
    public string? FailureReason { get; set; }
}