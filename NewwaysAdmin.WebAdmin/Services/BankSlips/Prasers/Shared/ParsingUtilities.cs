// NewwaysAdmin.WebAdmin/Services/BankSlips/Parsers/Shared/ParsingUtilities.cs
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers.Shared
{
    public static class ParsingUtilities
    {
        private static readonly string[] ThaiMonths = {
            "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
            "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."
        };

        /// <summary>
        /// Parses Thai date format and validates result
        /// </summary>
        public static DateTime ParseThaiDate(string dateText, ILogger? logger = null)
        {
            try
            {
                // Enhanced date parsing with validation
                var datePattern = @"(\d{1,2})\s*([ก-ฮ\.]+)\s*(\d{4})";
                var match = Regex.Match(dateText, datePattern);

                if (match.Success)
                {
                    var day = int.Parse(match.Groups[1].Value);
                    var monthText = match.Groups[2].Value;
                    var year = int.Parse(match.Groups[3].Value);

                    var monthIndex = Array.FindIndex(ThaiMonths, m => monthText.Contains(m.Replace(".", "")));
                    if (monthIndex >= 0)
                    {
                        var month = monthIndex + 1;

                        // Validate date components
                        if (month < 1 || month > 12)
                        {
                            logger?.LogWarning("Invalid month {Month} in date {DateText}", month, dateText);
                            throw new ArgumentException($"Invalid month: {month}");
                        }

                        if (day < 1 || day > 31)
                        {
                            logger?.LogWarning("Invalid day {Day} in date {DateText}", day, dateText);
                            throw new ArgumentException($"Invalid day: {day}");
                        }

                        if (year < 2560 || year > 2570)
                        {
                            logger?.LogWarning("Invalid year {Year} in date {DateText}", year, dateText);
                            throw new ArgumentException($"Invalid year: {year}");
                        }

                        var parsedDate = new DateTime(year - 543, month, day);
                        logger?.LogDebug("Successfully parsed Thai date: {DateText} -> {ParsedDate}",
                            dateText, parsedDate);

                        return parsedDate;
                    }
                }

                throw new ArgumentException($"Could not parse Thai date: {dateText}");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error parsing Thai date: {DateText}", dateText);
                throw;
            }
        }

        /// <summary>
        /// Cleans and validates account numbers
        /// </summary>
        public static string CleanAccountNumber(string accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
                return string.Empty;

            // Remove common prefixes and suffixes
            var cleaned = accountNumber
                .Replace("เลขที่", "")
                .Replace("บัญชี", "")
                .Replace(":", "")
                .Trim();

            // Standardize format
            cleaned = Regex.Replace(cleaned, @"\s+", "");

            return cleaned;
        }

        /// <summary>
        /// Extracts decimal amount from Thai text
        /// </summary>
        public static decimal ExtractAmount(string amountText)
        {
            if (string.IsNullOrWhiteSpace(amountText))
                return 0;

            // Remove Thai currency indicators
            var cleaned = amountText
                .Replace("บาท", "")
                .Replace("฿", "")
                .Replace(",", "")
                .Trim();

            if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                return amount;
            }

            return 0;
        }

        /// <summary>
        /// Determines if a line should be skipped during parsing
        /// </summary>
        public static bool ShouldSkipLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 2)
                return true;

            var skipPatterns = new[]
            {
                "เค+", "สแกนตรวจสอบ", "จํานวน:", "จำนวน:", "ค่าธรรมเนียม:",
                "รหัสพร้อมเพย์", "ยอดคงเหลือ", "เลขที่รายการ", "Mobile Banking",
                "ATM", "Online", "สำเร็จ", "เสร็จสิ้น", "completed"
            };

            return skipPatterns.Any(pattern => line.Contains(pattern));
        }

        /// <summary>
        /// Validates if text represents a valid Thai name
        /// </summary>
        public static bool IsValidThaiName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                return false;

            // Should contain Thai characters
            var hasThaiChars = name.Any(c => c >= '\u0E00' && c <= '\u0E7F');

            // Should not contain system keywords
            var systemKeywords = new[] { "จํานวน", "จำนวน", "ค่าธรรมเนียม", "บาท", "พร้อมเพย์" };
            var hasSystemKeywords = systemKeywords.Any(keyword => name.Contains(keyword));

            return hasThaiChars && !hasSystemKeywords;
        }

        /// <summary>
        /// Validates if text represents a valid English name
        /// </summary>
        public static bool IsValidEnglishName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                return false;

            // Should contain mainly Latin characters
            var latinChars = name.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == ' ');
            var validRatio = (double)latinChars / name.Length;

            return validRatio > 0.7; // At least 70% Latin characters
        }
    }
}