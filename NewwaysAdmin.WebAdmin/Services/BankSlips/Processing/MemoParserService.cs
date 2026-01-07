// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/MemoParserService.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Parses structured memo format from bank slip note field.
/// Expected format: "Location: X - Person: Y - Category: A + B - VAT - Memo: Z"
/// Also handles OCR variations like "Location : X" (space before colon)
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
    public MemoParseResult Parse(string? noteText)
    {
        var result = new MemoParseResult
        {
            RawNoteText = noteText ?? string.Empty
        };

        if (string.IsNullOrWhiteSpace(noteText))
        {
            _logger.LogDebug("Note field is empty");
            result.IsStructuralNote = false;
            result.FailureReason = "Note field is empty";
            return result;
        }

        try
        {
            // Normalize the text - handle OCR variations like "Location :" vs "Location:"
            var normalizedText = NormalizeNoteText(noteText);
            _logger.LogDebug("Normalized note: {Original} -> {Normalized}", noteText, normalizedText);

            // Split by " - " delimiter
            var segments = normalizedText.Split(" - ", StringSplitOptions.None);

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
                if (StartsWithKey(trimmed, "Location"))
                {
                    parsedMemo.LocationName = ExtractValue(trimmed, "Location");
                    hasLocation = true;
                }
                // Person: Y
                else if (StartsWithKey(trimmed, "Person"))
                {
                    parsedMemo.PersonName = ExtractValue(trimmed, "Person");
                }
                // Category: A + B
                else if (StartsWithKey(trimmed, "Category"))
                {
                    var categoryValue = ExtractValue(trimmed, "Category");
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
                // Memo: Z
                else if (StartsWithKey(trimmed, "Memo"))
                {
                    parsedMemo.Memo = ExtractValue(trimmed, "Memo");
                }
            }

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

            _logger.LogDebug("✅ Parsed structural note: Location={Loc}, Person={Per}, Category={Cat}/{Sub}, VAT={Vat}",
                parsedMemo.LocationName, parsedMemo.PersonName,
                parsedMemo.CategoryName, parsedMemo.SubCategoryName, result.HasVat);

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
    /// Normalize OCR text variations - handle "Location :" vs "Location:"
    /// </summary>
    private string NormalizeNoteText(string text)
    {
        // Replace " :" with ":" to handle OCR spacing variations
        // But be careful not to break " - " delimiters
        return text
            .Replace(" :", ":")      // "Location :" -> "Location:"
            .Replace(":  ", ": ");   // "Location:  X" -> "Location: X" (double space)
    }

    /// <summary>
    /// Check if segment starts with a key (handles "Key:" or "Key :" patterns)
    /// </summary>
    private bool StartsWithKey(string segment, string key)
    {
        // After normalization, we should have "Key:" format
        return segment.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase) ||
               segment.StartsWith($"{key} :", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extract value after a key (handles "Key: Value" or "Key : Value")
    /// </summary>
    private string? ExtractValue(string segment, string key)
    {
        // Find the colon position
        var colonIndex = segment.IndexOf(':');
        if (colonIndex < 0 || colonIndex >= segment.Length - 1)
            return null;

        var value = segment.Substring(colonIndex + 1).Trim();

        // Treat "None" as null
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
    public string RawNoteText { get; set; } = string.Empty;
    public bool IsStructuralNote { get; set; }
    public ParsedMemo? ParsedMemo { get; set; }
    public bool? HasVat { get; set; }
    public string? FailureReason { get; set; }
}