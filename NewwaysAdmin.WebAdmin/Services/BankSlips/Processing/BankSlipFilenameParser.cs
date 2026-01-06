// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/BankSlipFilenameParser.cs

using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Parses bank slip filenames to extract pattern set, username, and timestamp.
/// Filename format: {PatternSet}_{Username}_{DD}_{MM}_{YYYY}_{HH}_{MM}_{SS}.bin
/// Example: KBIZ_Amy_01_01_2026_19_13_27.bin
/// </summary>
public class BankSlipFilenameParser
{
    private readonly ILogger<BankSlipFilenameParser> _logger;

    public BankSlipFilenameParser(ILogger<BankSlipFilenameParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parse a bank slip filename into its components
    /// </summary>
    /// <param name="filename">Filename with or without extension</param>
    /// <returns>Parsed info or null if parsing failed</returns>
    public BankSlipFilenameInfo? Parse(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            _logger.LogWarning("Cannot parse empty filename");
            return null;
        }

        try
        {
            // Remove extension if present
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);

            // Split by underscore
            var parts = nameWithoutExt.Split('_');

            // Minimum: PatternSet_Username_DD_MM_YYYY_HH_MM_SS = 8 parts
            if (parts.Length < 8)
            {
                _logger.LogWarning("Filename has too few parts ({Count}): {Filename}", parts.Length, filename);
                return null;
            }

            // Pattern set is first part
            var patternSet = parts[0];

            // Username is second part
            var username = parts[1];

            // Date and time are the last 6 parts
            // Work backwards from the end to handle usernames with underscores
            var timeIndex = parts.Length - 6;

            // If username might have underscores, join middle parts
            if (timeIndex > 2)
            {
                username = string.Join("_", parts.Skip(1).Take(timeIndex - 1));
            }

            // Parse date parts (DD_MM_YYYY)
            if (!int.TryParse(parts[timeIndex], out var day) ||
                !int.TryParse(parts[timeIndex + 1], out var month) ||
                !int.TryParse(parts[timeIndex + 2], out var year))
            {
                _logger.LogWarning("Failed to parse date from filename: {Filename}", filename);
                return null;
            }

            // Parse time parts (HH_MM_SS)
            if (!int.TryParse(parts[timeIndex + 3], out var hour) ||
                !int.TryParse(parts[timeIndex + 4], out var minute) ||
                !int.TryParse(parts[timeIndex + 5], out var second))
            {
                _logger.LogWarning("Failed to parse time from filename: {Filename}", filename);
                return null;
            }

            // Validate and create DateTime
            DateTime timestamp;
            try
            {
                timestamp = new DateTime(year, month, day, hour, minute, second);
            }
            catch (ArgumentOutOfRangeException)
            {
                _logger.LogWarning("Invalid date/time values in filename: {Filename}", filename);
                return null;
            }

            var result = new BankSlipFilenameInfo
            {
                PatternSetName = patternSet,
                Username = username,
                Timestamp = timestamp,
                IsValid = true,
                OriginalFilename = filename
            };

            _logger.LogDebug("Parsed filename: {Filename} → Pattern: {Pattern}, User: {User}, Time: {Time}",
                filename, patternSet, username, timestamp);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing filename: {Filename}", filename);
            return null;
        }
    }

    /// <summary>
    /// Generate a filename from components (reverse of Parse)
    /// </summary>
    public string GenerateFilename(string patternSet, string username, DateTime timestamp, string extension = ".json")
    {
        var dateStr = timestamp.ToString("dd_MM_yyyy");
        var timeStr = timestamp.ToString("HH_mm_ss");
        return $"{patternSet}_{username}_{dateStr}_{timeStr}{extension}";
    }
}

/// <summary>
/// Result of parsing a bank slip filename
/// </summary>
public class BankSlipFilenameInfo
{
    /// <summary>
    /// OCR pattern set name (e.g., "KBIZ", "KPlus")
    /// </summary>
    public string PatternSetName { get; set; } = string.Empty;

    /// <summary>
    /// Username who made the transaction (e.g., "Amy", "Thomas")
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Transaction timestamp from filename
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// True if parsing was successful
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Original filename that was parsed
    /// </summary>
    public string OriginalFilename { get; set; } = string.Empty;

    /// <summary>
    /// Get the project ID (filename without extension)
    /// </summary>
    public string GetProjectId() => Path.GetFileNameWithoutExtension(OriginalFilename);
}