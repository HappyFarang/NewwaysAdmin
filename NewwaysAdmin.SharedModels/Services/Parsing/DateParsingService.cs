// NewwaysAdmin.SharedModels/Services/Parsing/DateParsingService.cs
// 🗓️ Standalone date parsing service - reusable across the entire system
// Handles Thai and English date formats with Buddhist era conversion

using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.SharedModels.Services.Parsing
{
    /// <summary>
    /// Date parsing types supported by the system
    /// </summary>
    public enum DateParsingType
    {
        Thai,     // Parses "30 ส.ค. 68" → "2025/08/30"
        English,  // Parses "27 Aug 25" → "2025/08/27"
        Reverse   // Parses "17/08/2025" → "2025/08/17" (NEW)
    }

    /// <summary>
    /// Standalone date parsing service - can be used by OCR, PDF parsing, Excel imports, etc.
    /// Handles Thai Buddhist era dates and English dates with consistent YYYY/MM/DD output
    /// </summary>
    public class DateParsingService
    {
        private readonly ILogger<DateParsingService> _logger;

        // Thai month abbreviations mapping
        private readonly Dictionary<string, int> _thaiMonths = new()
        {
            { "ม.ค.", 1 }, { "มกรา", 1 }, { "มกราคม", 1 },
            { "ก.พ.", 2 }, { "กุมภา", 2 }, { "กุมภาพันธ์", 2 },
            { "มี.ค.", 3 }, { "มีนา", 3 }, { "มีนาคม", 3 },
            { "เม.ย.", 4 }, { "เมษา", 4 }, { "เมษายน", 4 },
            { "พ.ค.", 5 }, { "พฤษภา", 5 }, { "พฤษภาคม", 5 },
            { "มิ.ย.", 6 }, { "มิถุนา", 6 }, { "มิถุนายน", 6 },
            { "ก.ค.", 7 }, { "กรกฎา", 7 }, { "กรกฎาคม", 7 },
            { "ส.ค.", 8 }, { "สิงหา", 8 }, { "สิงหาคม", 8 },
            { "ก.ย.", 9 }, { "กันยา", 9 }, { "กันยายน", 9 },
            { "ต.ค.", 10 }, { "ตุลา", 10 }, { "ตุลาคม", 10 },
            { "พ.ย.", 11 }, { "พฤศจิกา", 11 }, { "พฤศจิกายน", 11 },
            { "ธ.ค.", 12 }, { "ธันวา", 12 }, { "ธันวาคม", 12 }
        };

        // English month abbreviations mapping
        private readonly Dictionary<string, int> _englishMonths = new()
        {
            { "jan", 1 }, { "january", 1 },
            { "feb", 2 }, { "february", 2 },
            { "mar", 3 }, { "march", 3 },
            { "apr", 4 }, { "april", 4 },
            { "may", 5 },
            { "jun", 6 }, { "june", 6 },
            { "jul", 7 }, { "july", 7 },
            { "aug", 8 }, { "august", 8 },
            { "sep", 9 }, { "september", 9 }, { "sept", 9 },
            { "oct", 10 }, { "october", 10 },
            { "nov", 11 }, { "november", 11 },
            { "dec", 12 }, { "december", 12 }
        };

        public DateParsingService(ILogger<DateParsingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Main entry point - parse date text using specified parsing type
        /// </summary>
        /// <param name="text">Date text to parse (e.g., "30 ส.ค. 68" or "27 Aug 25")</param>
        /// <param name="parsingType">Thai or English parsing</param>
        /// <param name="contextInfo">Optional context for logging (e.g., filename, pattern name)</param>
        /// <returns>Parsed date in YYYY/MM/DD format, or original text if parsing fails</returns>
        public string ParseDate(string text, DateParsingType parsingType, string contextInfo = "")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text ?? "";
            }

            try
            {
                _logger.LogDebug("🗓️ Parsing {ParsingType} date: '{Text}' {Context}",
                    parsingType, text, string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})");

                string result = parsingType switch
                {
                    DateParsingType.Thai => ParseThaiDate(text),
                    DateParsingType.English => ParseEnglishDate(text),
                    DateParsingType.Reverse => ParseReverseSlashDate(text), 
                    _ => text
                };

                if (result != text)
                {
                    _logger.LogDebug("✅ Date parsing successful {Context}: '{OriginalText}' → '{ParsedDate}'",
                        string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text, result);
                }
                else
                {
                    _logger.LogDebug("❌ Date parsing failed {Context}, keeping original: '{Text}'",
                        string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "💥 Error parsing date {Context}: '{Text}' - {Error}",
                    string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text, ex.Message);
                return text; // Return original on error
            }
        }

        /// <summary>
        /// Parse Thai date format: "30 ส.ค. 68" → "2025/08/30"
        /// Handles Buddhist era to Gregorian conversion
        /// </summary>
        public string ParseThaiDate(string text)
        {
            try
            {
                // Regex to capture: day, thai_month, year
                var regex = new Regex(@"(\d+)\s+([ก-๙ก-์\.\s]+)\s+(\d+)", RegexOptions.IgnoreCase);
                var match = regex.Match(text.Trim());

                if (!match.Success)
                {
                    return text; // Return original if no match
                }

                var dayStr = match.Groups[1].Value;
                var monthStr = match.Groups[2].Value.Trim();
                var yearStr = match.Groups[3].Value;

                // Parse day
                if (!int.TryParse(dayStr, out var day) || day < 1 || day > 31)
                {
                    return text;
                }

                // Parse Thai month
                if (!_thaiMonths.TryGetValue(monthStr, out var month))
                {
                    return text;
                }

                // Parse year and convert Buddhist era to Gregorian
                if (!int.TryParse(yearStr, out var year))
                {
                    return text;
                }

                // Convert 2-digit Buddhist year to 4-digit Gregorian year
                // 67-99 = 2024-2056, 00-66 = 2057-2123
                if (year >= 67 && year <= 99)
                {
                    year = 1957 + year; // 67 becomes 2024, 68 becomes 2025, etc.
                }
                else if (year >= 0 && year <= 66)
                {
                    year = 2057 + year; // 00 becomes 2057, 01 becomes 2058, etc.
                }
                else if (year >= 2467) // Full Buddhist era year
                {
                    year = year - 543; // Convert to Gregorian
                }

                // Validate final date
                if (year < 1900 || year > 2200)
                {
                    return text;
                }

                // Format as YYYY/MM/DD for Google Sheets
                return $"{year:D4}/{month:D2}/{day:D2}";
            }
            catch
            {
                return text; // Return original on any error
            }
        }

        /// <summary>
        /// Parse English date format: "27 Aug 25" → "2025/08/27"
        /// </summary>
        public string ParseEnglishDate(string text)
        {
            try
            {
                // Regex to capture: day, english_month, year
                var regex = new Regex(@"(\d+)\s+([A-Za-z]+)\s+(\d+)", RegexOptions.IgnoreCase);
                var match = regex.Match(text.Trim());

                if (!match.Success)
                {
                    return text; // Return original if no match
                }

                var dayStr = match.Groups[1].Value;
                var monthStr = match.Groups[2].Value.ToLower();
                var yearStr = match.Groups[3].Value;

                // Parse day
                if (!int.TryParse(dayStr, out var day) || day < 1 || day > 31)
                {
                    return text;
                }

                // Parse English month
                if (!_englishMonths.TryGetValue(monthStr, out var month))
                {
                    return text;
                }

                // Parse year
                if (!int.TryParse(yearStr, out var year))
                {
                    return text;
                }

                // Convert 2-digit year to 4-digit year
                // Assume years 00-30 are 2000-2030, 31-99 are 1931-1999
                // But for bank slips, more likely 00-99 are 2000-2099
                if (year >= 0 && year <= 99)
                {
                    if (year >= 0 && year <= 30)
                        year = 2000 + year; // 00-30 = 2000-2030
                    else
                        year = 1900 + year; // 31-99 = 1931-1999
                }

                // Validate final date
                if (year < 1900 || year > 2200)
                {
                    return text;
                }

                // Format as YYYY/MM/DD for Google Sheets
                return $"{year:D4}/{month:D2}/{day:D2}";
            }
            catch
            {
                return text; // Return original on any error
            }
        }
        /// <summary>
        /// Parse and reverse slash date format: "17/08/2025" → "2025/08/17"
        /// Handles DD/MM/YYYY or D/M/YYYY formats
        /// </summary>
        public string ParseReverseSlashDate(string text)
        {
            try
            {
                // Regex to capture: day/month/year with 1-2 digit day/month and 4 digit year
                var regex = new Regex(@"(\d{1,2})/(\d{1,2})/(\d{4})");
                var match = regex.Match(text.Trim());

                if (!match.Success)
                {
                    return text; // Return original if no match
                }

                var dayStr = match.Groups[1].Value;
                var monthStr = match.Groups[2].Value;
                var yearStr = match.Groups[3].Value;

                // Parse and validate
                if (!int.TryParse(dayStr, out var day) || day < 1 || day > 31)
                {
                    return text;
                }

                if (!int.TryParse(monthStr, out var month) || month < 1 || month > 12)
                {
                    return text;
                }

                if (!int.TryParse(yearStr, out var year) || year < 1900 || year > 2100)
                {
                    return text;
                }

                // Format with zero padding
                return $"{year:D4}/{month:D2}/{day:D2}";
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to parse reverse slash date: {Text} - {Error}", text, ex.Message);
                return text; // Return original on error
            }
        }
        /// <summary>
        /// Check if text contains a recognizable date pattern
        /// </summary>
        /// <param name="text">Text to check</param>
        /// <param name="parsingType">Type of date format to check for</param>
        /// <returns>True if text appears to contain a parseable date</returns>
        public bool ContainsDate(string text, DateParsingType parsingType)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return parsingType switch
            {
                DateParsingType.Thai => Regex.IsMatch(text, @"\d+\s+[ก-๙ก-์\.\s]+\s+\d+"),
                DateParsingType.English => Regex.IsMatch(text, @"\d+\s+[A-Za-z]+\s+\d+"),
                _ => false
            };
        }
    }
}