// NewwaysAdmin.WebAdmin/Services/BankSlips/Parsers/OriginalSlipParser.cs
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers.Shared;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers
{
    public class OriginalSlipParser : IBankSlipParser
    {
        private readonly ILogger<OriginalSlipParser> _logger;

        // Original slip patterns (extracted from existing code)
        private static readonly Regex AccountNumberPattern = new(@"[xX]{3,}[-\d]+|[\d]+-[xX]{3,}|\d{3}-\d-\d{5}-\d");
        private static readonly Regex AmountPattern = new(@"[\d,]+\.?\d*\s*บาท");

        public OriginalSlipParser(ILogger<OriginalSlipParser> logger)
        {
            _logger = logger;
        }

        public bool CanParse(string text, SlipCollection collection)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Original format indicators (absence of K-BIZ indicators)
            var hasKBizIndicators = text.Contains("KBIZ", StringComparison.OrdinalIgnoreCase) ||
                                   text.Contains("K BIZ", StringComparison.OrdinalIgnoreCase) ||
                                   text.Contains("Issued by K BIZ", StringComparison.OrdinalIgnoreCase);

            // If not K-BIZ and has typical bank slip elements, can parse
            var hasBankElements = text.Contains("พร้อมเพย์") ||
                                 text.Contains("PromptPay") ||
                                 AmountPattern.IsMatch(text) ||
                                 AccountNumberPattern.IsMatch(text);

            var canParse = !hasKBizIndicators && hasBankElements;

            _logger.LogDebug("Original parser analysis: NoKBiz={NoKBiz}, HasBankElements={HasElements}, CanParse={CanParse}",
                !hasKBizIndicators, hasBankElements, canParse);

            return canParse;
        }

        public BankSlipData? Parse(string text, string imagePath, SlipCollection collection)
        {
            try
            {
                _logger.LogDebug("Starting original parsing for {ImagePath}", Path.GetFileName(imagePath));

                var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                var slip = new BankSlipData
                {
                    OriginalFilePath = imagePath,
                    ParserUsed = GetParserName(),
                    IsKBizFormat = false,
                    ParsingNotes = new Dictionary<string, string>()
                };

                // Parse using original logic (extracted and cleaned)
                ParseOriginalSlip(lines, slip);

                // Validate and cleanup
                ValidateAndCleanup(slip, imagePath);

                _logger.LogDebug("Original parsing completed for {ImagePath}: Date={Date}, Amount={Amount}, Receiver={Receiver}",
                    Path.GetFileName(imagePath), slip.TransactionDate, slip.Amount, slip.ReceiverName);

                return slip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing original slip {ImagePath}", imagePath);
                return null;
            }
        }

        private void ParseOriginalSlip(List<string> lines, BankSlipData slip)
        {
            bool foundSenderAccount = false;
            bool foundAmount = false;
            bool foundPromptPaySection = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var nextLine = i + 1 < lines.Count ? lines[i + 1] : "";

                // Skip system lines
                if (ParsingUtilities.ShouldSkipLine(line))
                {
                    _logger.LogDebug("Skipping system line: '{Line}'", line);
                    continue;
                }

                // Date parsing
                if (IsDateLine(line) && slip.TransactionDate == default)
                {
                    try
                    {
                        slip.TransactionDate = ParsingUtilities.ParseThaiDate(line, _logger);
                        slip.ParsingNotes.Add("DateSource", $"Line {i}: {line}");
                        _logger.LogDebug("Found date: {Date}", slip.TransactionDate);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Failed to parse date from '{Line}': {Error}", line, ex.Message);
                    }
                }

                // Check for PromptPay section
                else if (line.Contains("พร้อมเพย์") || line.Contains("Prompt") || line.Contains("PromptPay"))
                {
                    foundPromptPaySection = true;
                    _logger.LogDebug("Found PromptPay section at line {Index}", i);
                }

                // Account number detection - sender account first
                else if (!foundSenderAccount && AccountNumberPattern.IsMatch(line))
                {
                    var cleanLine = ParsingUtilities.CleanAccountNumber(line);
                    slip.AccountNumber = cleanLine;
                    foundSenderAccount = true;
                    slip.ParsingNotes.Add("SenderAccountSource", $"Line {i}: {line}");
                    _logger.LogDebug("Found sender account: {Account}", slip.AccountNumber);
                }

                // Receiver account detection (after sender)
                else if (foundSenderAccount && !foundAmount && AccountNumberPattern.IsMatch(line))
                {
                    var cleanLine = ParsingUtilities.CleanAccountNumber(line);
                    if (cleanLine != slip.AccountNumber)
                    {
                        slip.ReceiverAccount = cleanLine;
                        slip.ParsingNotes.Add("ReceiverAccountSource", $"Line {i}: {line}");
                        _logger.LogDebug("Found receiver account: {Account}", slip.ReceiverAccount);
                    }
                }

                // Amount detection
                else if (!foundAmount && AmountPattern.IsMatch(line))
                {
                    slip.Amount = ParsingUtilities.ExtractAmount(line);
                    if (slip.Amount > 0)
                    {
                        foundAmount = true;
                        slip.ParsingNotes.Add("AmountSource", $"Line {i}: {line}");
                        _logger.LogDebug("Found amount: {Amount}", slip.Amount);
                    }
                }

                // Receiver name detection
                else if (foundPromptPaySection && string.IsNullOrEmpty(slip.ReceiverName))
                {
                    if (IsValidReceiverName(line, slip.AccountName))
                    {
                        // Check for multi-line names
                        var fullName = line;
                        if (ShouldContinueNameOnNextLine(line, nextLine))
                        {
                            fullName += " " + nextLine;
                            i++; // Skip next line as it's part of the name
                        }

                        slip.ReceiverName = fullName.Trim();
                        slip.ParsingNotes.Add("ReceiverNameSource", $"Line {i}: {line}");
                        _logger.LogDebug("Found receiver name: {Name}", slip.ReceiverName);
                    }
                }

                // Note detection (after PromptPay section)
                else if (foundPromptPaySection && string.IsNullOrEmpty(slip.Note))
                {
                    if (IsValidNoteContent(line))
                    {
                        var cleanNote = CleanNoteText(line);
                        if (!IsNoteTextInvalid(cleanNote))
                        {
                            slip.Note = cleanNote;
                            slip.ParsingNotes.Add("NoteSource", $"Line {i}: {line}");
                            _logger.LogDebug("Found note: {Note}", slip.Note);
                        }
                    }
                }
            }
        }

        private bool IsDateLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 8)
                return false;

            // Thai month patterns
            var thaiMonthPattern = @"[ก-ฮ\.]+(ค|พ|ย|ส)\.?";

            // Check for Thai date patterns
            var patterns = new[]
            {
                @"\d{1,2}\s*[ก-ฮ\.]+\s*\d{4}",  // Basic Thai date
                @"\d{1,2}/\d{1,2}/\d{4}",       // Western date
                @"\d{4}-\d{1,2}-\d{1,2}"        // ISO date
            };

            return patterns.Any(pattern => Regex.IsMatch(line, pattern));
        }

        private bool IsValidReceiverName(string line, string senderName)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
                return false;

            // Skip if same as sender
            if (!string.IsNullOrEmpty(senderName) && line.Equals(senderName, StringComparison.OrdinalIgnoreCase))
                return false;

            // Skip system keywords
            var systemKeywords = new[]
            {
                "จํานวน", "จำนวน", "ค่าธรรมเนียม", "บาท", "พร้อมเพย์",
                "รหัส", "สแกน", "ตรวจสอบ", "ยอด", "คงเหลือ", "เลขที่", "รายการ", "บันทึก"
            };

            if (systemKeywords.Any(keyword => line.Contains(keyword)))
                return false;

            // Skip account numbers
            if (AccountNumberPattern.IsMatch(line) || line.Count(char.IsDigit) > line.Length / 2)
                return false;

            // Thai name patterns
            var thaiNamePatterns = new[] { "นาย", "นาง", "น.ส.", "บจ.", "บมจ.", "ห้าง", "บริษัท" };
            var hasThaiNamePattern = thaiNamePatterns.Any(pattern => line.Contains(pattern));

            // Has Thai characters
            var hasThaiChars = line.Any(c => c >= '\u0E00' && c <= '\u0E7F');

            return hasThaiNamePattern || hasThaiChars;
        }

        private bool ShouldContinueNameOnNextLine(string currentLine, string nextLine)
        {
            if (string.IsNullOrWhiteSpace(nextLine) || nextLine.Length < 3)
                return false;

            var continuationPatterns = new[]
            {
                "จำกัด", "มหาชน", "กรุ๊ป", "เซ็นเตอร์", "คอร์ปอเรชั่น",
                "เทรดดิ้ง", "ดีเวลลอปเมนท์", "แมนเนจเมนท์", "อินเตอร์เนชั่นแนล"
            };

            if (currentLine.EndsWith("จำกัด") || currentLine.EndsWith("มหาชน"))
                return false;

            if (continuationPatterns.Any(pattern => nextLine.StartsWith(pattern)))
                return true;

            var completeEndings = new[] { "จำกัด", "มหาชน", "บริษัท", "ห้าง" };
            if (!completeEndings.Any(ending => currentLine.EndsWith(ending)))
            {
                if (nextLine.Any(c => c >= '\u0E00' && c <= '\u0E7F') && nextLine.Length < 20)
                    return true;
            }

            return false;
        }

        private bool IsValidNoteContent(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3)
                return false;

            // Should not be mostly numbers
            if (line.Count(char.IsDigit) > line.Length / 2)
                return false;

            // Should not contain system keywords
            var systemKeywords = new[]
            {
                "ยอดคงเหลือ", "ค่าธรรมเนียม", "เลขที่รายการ",
                "สแกนตรวจสอบ", "รหัสพร้อมเพย์", "Mobile Banking"
            };

            return !systemKeywords.Any(keyword => line.Contains(keyword));
        }

        private string CleanNoteText(string note)
        {
            if (string.IsNullOrWhiteSpace(note))
                return string.Empty;

            // Remove common prefixes
            var prefixes = new[] { "บันทึก:", "หมายเหตุ:", "Note:", "Memo:" };
            foreach (var prefix in prefixes)
            {
                if (note.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    note = note.Substring(prefix.Length).Trim();
                    break;
                }
            }

            // Clean up punctuation
            note = note.Replace(':', ',').Replace(';', ',');
            note = note.TrimStart(':', '-', ' ', '.', ',');

            return note.Trim();
        }

        private bool IsNoteTextInvalid(string note)
        {
            if (string.IsNullOrWhiteSpace(note) || note.Length < 3)
                return true;

            // Reject if it's just OCR noise or fragments
            var invalidTexts = new[]
            {
                "ช่วย", "จ่า", "จํา", "ช่วยจำ", "ช่วยจํา", "บันทึก", "หมายเหตุ",
                "เค", "สแกน", "ตรวจ", "เลข", "รหัส"
            };

            if (invalidTexts.Any(invalid => note.Equals(invalid, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Reject if it still contains system keywords
            var systemKeywords = new[]
            {
                "ยอดคงเหลือ", "ค่าธรรมเนียม", "เลขที่รายการ",
                "สแกนตรวจสอบ", "รหัสพร้อมเพย์", "Mobile Banking"
            };

            return systemKeywords.Any(keyword => note.Contains(keyword));
        }

        private void ValidateAndCleanup(BankSlipData slip, string imagePath)
        {
            // Use file timestamp if date parsing failed
            if (slip.TransactionDate == default)
            {
                var fileInfo = new FileInfo(imagePath);
                slip.TransactionDate = fileInfo.LastWriteTime.AddYears(543);
                slip.ParsingNotes.Add("DateFallback", "Used file timestamp");
                _logger.LogDebug("Using file timestamp for date: {Date}", slip.TransactionDate);
            }

            // Set default message when receiver name couldn't be resolved
            if (string.IsNullOrEmpty(slip.ReceiverName))
            {
                slip.ReceiverName = "Could not resolve receiver";
                slip.ParsingNotes.Add("ReceiverNameFallback", "Default message");
                _logger.LogDebug("No valid receiver name found, setting default message");
            }

            // Clean up receiver name if it contains unwanted text
            if (!string.IsNullOrEmpty(slip.ReceiverName))
            {
                var unwantedPrefixes = new[] { "จํานวน:", "จำนวน:", "ค่าธรรมเนียม:" };
                foreach (var prefix in unwantedPrefixes)
                {
                    if (slip.ReceiverName.StartsWith(prefix))
                    {
                        var colonIndex = slip.ReceiverName.IndexOf(':');
                        if (colonIndex >= 0 && colonIndex < slip.ReceiverName.Length - 1)
                        {
                            slip.ReceiverName = slip.ReceiverName.Substring(colonIndex + 1).Trim();
                        }
                    }
                }

                var systemKeywords = new[] { "จํานวน", "จำนวน", "ค่าธรรมเนียม", "บาท" };
                if (systemKeywords.Any(keyword => slip.ReceiverName.Contains(keyword)))
                {
                    _logger.LogDebug("Clearing invalid receiver name: '{Name}'", slip.ReceiverName);
                    slip.ReceiverName = "Could not resolve receiver";
                }
            }
        }

        public string GetParserName() => "Original Bank Slip Parser";

        public SlipFormat GetSupportedFormat() => SlipFormat.Original;

        public bool ValidateParsedData(BankSlipData data)
        {
            if (data == null) return false;

            var isValid = true;
            var issues = new List<string>();

            // Date validation
            if (data.TransactionDate == default)
            {
                issues.Add("Missing transaction date");
                isValid = false;
            }
            else if (data.TransactionDate.Year < 2017 || data.TransactionDate.Year > 2030)
            {
                issues.Add($"Invalid transaction year: {data.TransactionDate.Year}");
                isValid = false;
            }

            // Amount validation
            if (data.Amount <= 0)
            {
                issues.Add("Missing or invalid amount");
                isValid = false;
            }

            // Receiver validation
            if (string.IsNullOrEmpty(data.ReceiverName))
            {
                issues.Add("Missing receiver name");
                isValid = false;
            }

            if (!isValid)
            {
                data.ParsingNotes.Add("ValidationIssues", string.Join(", ", issues));
                _logger.LogWarning("Original slip validation failed: {Issues}", string.Join(", ", issues));
            }

            return isValid;
        }
    }
}