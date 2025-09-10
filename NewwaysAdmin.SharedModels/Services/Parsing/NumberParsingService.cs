// NewwaysAdmin.SharedModels/Services/Parsing/NumberParsingService.cs
// 💰 Standalone number parsing service - reusable across the entire system
// Handles Thai currency extraction with บาท, สตางค์, and various number formats

/*
"Number Thai"     → Thai currency extraction (perfect for your bank slips!)
"Number Decimal"  → Standard decimal parsing  
"Number Integer"  → Whole numbers only
"Number Currency" → International currency formats
"Date Thai"       → Thai date parsing (30 ส.ค. 68 → 2025/08/30)
"Date English"    → English dates (27 Aug 25 → 2025/08/27)
*/

using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.SharedModels.Services.Parsing
{
    /// <summary>
    /// Number parsing types supported by the system
    /// </summary>
    public enum NumberParsingType
    {
        Thai,           // Parses "1,500.50บาท" → "1500.50"
        Decimal,        // Parses any decimal number from text → "1500.50"
        Integer,        // Parses any integer from text → "1500"
        Currency        // Parses currency with symbols → "1500.50" (removes ฿, บาท, etc.)
    }

    /// <summary>
    /// Standalone number parsing service - can be used by OCR, PDF parsing, Excel imports, etc.
    /// Handles Thai currency formats and various number representations with consistent decimal output
    /// </summary>
    public class NumberParsingService
    {
        private readonly ILogger<NumberParsingService> _logger;

        // Thai currency indicators to remove during parsing
        private readonly string[] _thaiCurrencyIndicators =
        {
            "บาท", "฿", "สตางค์", "THB", "Baht"
        };

        // Common number separators and formatting
        private readonly string[] _thousandsSeparators = { ",", " ", "'" };

        public NumberParsingService(ILogger<NumberParsingService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Main entry point - parse number text using specified parsing type
        /// </summary>
        /// <param name="text">Number text to parse (e.g., "1,500.50บาท" or "123,456.78")</param>
        /// <param name="parsingType">Type of number parsing to perform</param>
        /// <param name="contextInfo">Optional context for logging (e.g., filename, pattern name)</param>
        /// <returns>Parsed number as string in decimal format, or original text if parsing fails</returns>
        public string ParseNumber(string text, NumberParsingType parsingType, string contextInfo = "")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text ?? "";
            }

            try
            {
                _logger.LogDebug("💰 Parsing {ParsingType} number: '{Text}' {Context}",
                    parsingType, text, string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})");

                string result = parsingType switch
                {
                    NumberParsingType.Thai => ParseThaiCurrency(text),
                    NumberParsingType.Decimal => ParseDecimalNumber(text),
                    NumberParsingType.Integer => ParseIntegerNumber(text),
                    NumberParsingType.Currency => ParseCurrencyNumber(text),
                    _ => text
                };

                if (result != text)
                {
                    _logger.LogDebug("✅ Number parsing successful {Context}: '{OriginalText}' → '{ParsedNumber}'",
                        string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text, result);
                }
                else
                {
                    _logger.LogDebug("❌ Number parsing failed {Context}, keeping original: '{Text}'",
                        string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "💥 Error parsing number {Context}: '{Text}' - {Error}",
                    string.IsNullOrEmpty(contextInfo) ? "" : $"({contextInfo})", text, ex.Message);
                return text; // Return original on error
            }
        }

        /// <summary>
        /// Parse Thai currency format: "1,500.50บาท" → "1500.50"
        /// Handles various Thai currency formats and separators
        /// </summary>
        private string ParseThaiCurrency(string text)
        {
            try
            {
                // Clean the text - remove all Thai currency indicators
                var cleaned = text;
                foreach (var indicator in _thaiCurrencyIndicators)
                {
                    cleaned = cleaned.Replace(indicator, "", StringComparison.OrdinalIgnoreCase);
                }

                // Remove extra whitespace
                cleaned = cleaned.Trim();

                // Parse as decimal number
                return ParseDecimalNumber(cleaned);
            }
            catch
            {
                return text; // Return original on any error
            }
        }

        /// <summary>
        /// Parse decimal number: "1,500.50" → "1500.50"
        /// Handles thousands separators and decimal points
        /// NEW: Enhanced to handle Thai decimal comma format
        /// </summary>
        private string ParseDecimalNumber(string text)
        {
            try
            {
                // NEW: First check for Thai decimal comma format (e.g., "1,100,20" = 1100.20)
                var thaiDecimalPattern = @"^(\d{1,3}(?:,\d{3})*),(\d{2})$";
                var thaiMatch = Regex.Match(text, thaiDecimalPattern);

                if (thaiMatch.Success)
                {
                    var wholePart = thaiMatch.Groups[1].Value.Replace(",", ""); // Remove thousands separators
                    var decimalPart = thaiMatch.Groups[2].Value;
                    var thaiResult = $"{wholePart}.{decimalPart}";

                    Console.WriteLine($"🔢 Thai decimal comma format: '{text}' → '{thaiResult}'");
                    return thaiResult;
                }

                // Standard decimal processing
                // Extract all numeric characters, commas, and decimal points
                var numberPattern = @"([0-9,]+(?:\.[0-9]+)?)";
                var match = Regex.Match(text, numberPattern);

                if (!match.Success)
                {
                    return text;
                }

                var numberText = match.Groups[1].Value;

                // Remove thousands separators but keep decimal point
                foreach (var separator in _thousandsSeparators)
                {
                    // Only remove if it's not the last dot (decimal point)
                    if (separator == "." && numberText.LastIndexOf('.') == numberText.IndexOf('.'))
                        continue; // Keep the decimal point

                    numberText = numberText.Replace(separator, "");
                }

                // Validate and parse the number
                if (decimal.TryParse(numberText, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalResult))
                {
                    // Return as string to maintain precision
                    return decimalResult.ToString(CultureInfo.InvariantCulture);
                }

                return text;
            }
            catch
            {
                return text; // Return original on any error
            }
        }

        /// <summary>
        /// Parse integer number: "1,500" → "1500"
        /// Extracts only whole numbers, no decimal places
        /// </summary>
        private string ParseIntegerNumber(string text)
        {
            try
            {
                // Extract only integer numeric characters and separators
                var numberPattern = @"([0-9,]+)";
                var match = Regex.Match(text, numberPattern);

                if (!match.Success)
                {
                    return text;
                }

                var numberText = match.Groups[1].Value;

                // Remove all separators
                foreach (var separator in _thousandsSeparators)
                {
                    numberText = numberText.Replace(separator, "");
                }

                // Validate and parse as integer
                if (long.TryParse(numberText, out var result))
                {
                    return result.ToString();
                }

                return text;
            }
            catch
            {
                return text; // Return original on any error
            }
        }

        /// <summary>
        /// Parse currency number: removes currency symbols and parses as decimal
        /// Handles multiple currency formats: $, €, ¥, ฿, etc.
        /// </summary>
        private string ParseCurrencyNumber(string text)
        {
            try
            {
                // Remove common currency symbols
                var currencySymbols = new[] { "$", "€", "¥", "£", "₹", "₩", "₨" };
                var cleaned = text;

                foreach (var symbol in currencySymbols.Concat(_thaiCurrencyIndicators))
                {
                    cleaned = cleaned.Replace(symbol, "", StringComparison.OrdinalIgnoreCase);
                }

                cleaned = cleaned.Trim();

                // Parse as decimal
                return ParseDecimalNumber(cleaned);
            }
            catch
            {
                return text; // Return original on any error
            }
        }

        /// <summary>
        /// Check if text contains a recognizable number pattern
        /// </summary>
        /// <param name="text">Text to check</param>
        /// <param name="parsingType">Type of number format to check for</param>
        /// <returns>True if text appears to contain a parseable number</returns>
        public bool ContainsNumber(string text, NumberParsingType parsingType)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return parsingType switch
            {
                NumberParsingType.Thai => Regex.IsMatch(text, @"[0-9,.]+(บาท|฿|THB)", RegexOptions.IgnoreCase),
                NumberParsingType.Decimal => Regex.IsMatch(text, @"[0-9,]+(\.[0-9]+)?"),
                NumberParsingType.Integer => Regex.IsMatch(text, @"[0-9,]+"),
                NumberParsingType.Currency => Regex.IsMatch(text, @"[\$€¥£₹₩₨฿]?[0-9,]+(\.[0-9]+)?"),
                _ => false
            };
        }

        /// <summary>
        /// Extract decimal value for calculations (similar to existing ExtractAmount)
        /// </summary>
        /// <param name="text">Text containing number</param>
        /// <param name="parsingType">Type of parsing to use</param>
        /// <returns>Decimal value or 0 if parsing fails</returns>
        public decimal ExtractDecimalValue(string text, NumberParsingType parsingType = NumberParsingType.Currency)
        {
            var parsedText = ParseNumber(text, parsingType);

            if (decimal.TryParse(parsedText, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return 0;
        }
    }
}