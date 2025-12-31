// NewwaysAdmin.SharedModels/Services/Parsing/TimeParsingService.cs
// ⏰ Standalone time parsing service - reusable across the entire system
// Converts 12-hour AM/PM format to 24-hour format for consistent data processing

/*
Magic Patterns:
"Time"        → Converts to 24-hour format (default)
"Time 24"     → Same as above, explicit 24-hour output
"Time 12"     → Keeps/converts to 12-hour AM/PM format

Examples:
"12:50 AM" → "00:50"
"12:50 PM" → "12:50"
"2:30 PM"  → "14:30"
"11:45 AM" → "11:45"
"14:30"    → "14:30" (already 24-hour, kept as-is)
*/

using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.SharedModels.Services.Parsing
{
    /// <summary>
    /// Time parsing types supported by the system
    /// </summary>
    public enum TimeParsingType
    {
        Hour24,     // Convert to 24-hour format: "2:30 PM" → "14:30"
        Hour12      // Convert to 12-hour format: "14:30" → "2:30 PM"
    }

    /// <summary>
    /// Standalone time parsing service - converts between 12-hour and 24-hour formats
    /// Provides consistent time output for data processing, sorting, and storage
    /// </summary>
    public class TimeParsingService
    {
        private readonly ILogger<TimeParsingService> _logger;

        // Patterns for detecting AM/PM
        private static readonly Regex AmPmPattern = new(
            @"(\d{1,2})[:\.](\d{2})(?:[:\.](\d{2}))?\s*(AM|PM|A\.M\.|P\.M\.|am|pm|a\.m\.|p\.m\.)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Pattern for 24-hour format
        private static readonly Regex Hour24Pattern = new(
            @"(\d{1,2})[:\.](\d{2})(?:[:\.](\d{2}))?",
            RegexOptions.Compiled);

        // Thai time indicators (optional - for future enhancement)
        private static readonly string[] ThaiTimeIndicators = { "น.", "นาฬิกา", "โมง" };

        public TimeParsingService(ILogger<TimeParsingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Main entry point - parse time text using specified parsing type
        /// </summary>
        /// <param name="text">Time text to parse (e.g., "12:50 AM" or "14:30")</param>
        /// <param name="parsingType">Type of time parsing to perform</param>
        /// <param name="contextInfo">Optional context for logging (e.g., filename, pattern name)</param>
        /// <returns>Parsed time as string in requested format, or original text if parsing fails</returns>
        public string ParseTime(string text, TimeParsingType parsingType, string contextInfo = "")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text ?? "";
            }

            try
            {
                _logger.LogDebug("⏰ Parsing {ParsingType} time: '{Text}' {Context}",
                    parsingType, text, string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})");

                string result = parsingType switch
                {
                    TimeParsingType.Hour24 => ConvertTo24Hour(text),
                    TimeParsingType.Hour12 => ConvertTo12Hour(text),
                    _ => text
                };

                if (result != text)
                {
                    _logger.LogDebug("✅ Time parsing successful {Context}: '{OriginalText}' → '{ParsedTime}'",
                        string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text, result);
                }
                else
                {
                    _logger.LogDebug("⏰ Time kept as-is {Context}: '{Text}'",
                        string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "💥 Error parsing time {Context}: '{Text}' - {Error}",
                    string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text, ex.Message);
                return text; // Return original on error
            }
        }

        /// <summary>
        /// Convert time to 24-hour format
        /// "12:50 AM" → "00:50"
        /// "12:50 PM" → "12:50"
        /// "2:30 PM"  → "14:30"
        /// "14:30"    → "14:30" (kept as-is)
        /// </summary>
        private string ConvertTo24Hour(string text)
        {
            try
            {
                var cleaned = text.Trim();

                // Check for AM/PM format
                var amPmMatch = AmPmPattern.Match(cleaned);
                if (amPmMatch.Success)
                {
                    var hour = int.Parse(amPmMatch.Groups[1].Value);
                    var minute = int.Parse(amPmMatch.Groups[2].Value);
                    var second = amPmMatch.Groups[3].Success ? int.Parse(amPmMatch.Groups[3].Value) : -1;
                    var amPm = amPmMatch.Groups[4].Value.ToUpperInvariant().Replace(".", "");

                    // Convert to 24-hour
                    if (amPm == "AM" || amPm == "A")
                    {
                        // 12 AM = 00:xx (midnight)
                        if (hour == 12)
                            hour = 0;
                    }
                    else if (amPm == "PM" || amPm == "P")
                    {
                        // 12 PM = 12:xx (noon), others add 12
                        if (hour != 12)
                            hour += 12;
                    }

                    // Format output
                    if (second >= 0)
                        return $"{hour:D2}:{minute:D2}:{second:D2}";
                    else
                        return $"{hour:D2}:{minute:D2}";
                }

                // Check if already 24-hour format
                var hour24Match = Hour24Pattern.Match(cleaned);
                if (hour24Match.Success)
                {
                    var hour = int.Parse(hour24Match.Groups[1].Value);
                    var minute = int.Parse(hour24Match.Groups[2].Value);
                    var second = hour24Match.Groups[3].Success ? int.Parse(hour24Match.Groups[3].Value) : -1;

                    // Validate hour range
                    if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                    {
                        if (second >= 0 && second <= 59)
                            return $"{hour:D2}:{minute:D2}:{second:D2}";
                        else
                            return $"{hour:D2}:{minute:D2}";
                    }
                }

                // Return original if no pattern matched
                return text;
            }
            catch
            {
                return text;
            }
        }

        /// <summary>
        /// Convert time to 12-hour format with AM/PM
        /// "14:30" → "2:30 PM"
        /// "00:50" → "12:50 AM"
        /// "2:30 PM" → "2:30 PM" (kept as-is)
        /// </summary>
        private string ConvertTo12Hour(string text)
        {
            try
            {
                var cleaned = text.Trim();

                // Check if already has AM/PM
                var amPmMatch = AmPmPattern.Match(cleaned);
                if (amPmMatch.Success)
                {
                    // Already in 12-hour format, normalize it
                    var hour = int.Parse(amPmMatch.Groups[1].Value);
                    var minute = int.Parse(amPmMatch.Groups[2].Value);
                    var second = amPmMatch.Groups[3].Success ? int.Parse(amPmMatch.Groups[3].Value) : -1;
                    var amPm = amPmMatch.Groups[4].Value.ToUpperInvariant().Replace(".", "");

                    var period = (amPm == "AM" || amPm == "A") ? "AM" : "PM";

                    if (second >= 0)
                        return $"{hour}:{minute:D2}:{second:D2} {period}";
                    else
                        return $"{hour}:{minute:D2} {period}";
                }

                // Convert from 24-hour format
                var hour24Match = Hour24Pattern.Match(cleaned);
                if (hour24Match.Success)
                {
                    var hour = int.Parse(hour24Match.Groups[1].Value);
                    var minute = int.Parse(hour24Match.Groups[2].Value);
                    var second = hour24Match.Groups[3].Success ? int.Parse(hour24Match.Groups[3].Value) : -1;

                    // Validate and convert
                    if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59)
                    {
                        string period;
                        int hour12;

                        if (hour == 0)
                        {
                            hour12 = 12;
                            period = "AM";
                        }
                        else if (hour < 12)
                        {
                            hour12 = hour;
                            period = "AM";
                        }
                        else if (hour == 12)
                        {
                            hour12 = 12;
                            period = "PM";
                        }
                        else
                        {
                            hour12 = hour - 12;
                            period = "PM";
                        }

                        if (second >= 0 && second <= 59)
                            return $"{hour12}:{minute:D2}:{second:D2} {period}";
                        else
                            return $"{hour12}:{minute:D2} {period}";
                    }
                }

                // Return original if no pattern matched
                return text;
            }
            catch
            {
                return text;
            }
        }

        /// <summary>
        /// Check if text contains a recognizable time pattern
        /// </summary>
        public bool ContainsTime(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return AmPmPattern.IsMatch(text) || Hour24Pattern.IsMatch(text);
        }

        /// <summary>
        /// Extract TimeSpan value for calculations
        /// </summary>
        public TimeSpan? ExtractTimeSpan(string text)
        {
            var parsed = ConvertTo24Hour(text);

            var match = Hour24Pattern.Match(parsed);
            if (match.Success)
            {
                var hour = int.Parse(match.Groups[1].Value);
                var minute = int.Parse(match.Groups[2].Value);
                var second = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

                if (hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59 && second >= 0 && second <= 59)
                {
                    return new TimeSpan(hour, minute, second);
                }
            }

            return null;
        }
    }
}