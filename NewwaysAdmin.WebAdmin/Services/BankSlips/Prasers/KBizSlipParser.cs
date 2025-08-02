// NewwaysAdmin.WebAdmin/Services/BankSlips/Parsers/KBizSlipParser.cs
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers.Shared;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers
{
    public class KBizSlipParser : IBankSlipParser
    {
        private readonly ILogger<KBizSlipParser> _logger;

        // K-BIZ specific patterns
        private static readonly Regex KBizHeaderPattern = new(@"(KBIZ|K\s*BIZ|กสิกรไทย)", RegexOptions.IgnoreCase);
        private static readonly Regex MoneyTransferPattern = new(@"Money\s+transferred\s+successfully", RegexOptions.IgnoreCase);
        private static readonly Regex KBizAccountPattern = new(@"\b\d{3}-\d{3}-\d{4}\b");
        private static readonly Regex KBizAmountPattern = new(@"[\d,]+\.?\d*\s*บาท");
        private static readonly Regex MemoPattern = new(@"Memo[\/:]?\s*(.+)", RegexOptions.IgnoreCase);
        private static readonly Regex ToSectionPattern = new(@"To[\/:]?\s*(.+)", RegexOptions.IgnoreCase);

        public KBizSlipParser(ILogger<KBizSlipParser> logger)
        {
            _logger = logger;
        }

        public bool CanParse(string text, SlipCollection collection)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Check for K-BIZ indicators
            var hasKBizHeader = KBizHeaderPattern.IsMatch(text);
            var hasMoneyTransfer = MoneyTransferPattern.IsMatch(text);
            var hasIssuedByKBiz = text.Contains("Issued by K BIZ", StringComparison.OrdinalIgnoreCase);

            var canParse = hasKBizHeader || hasMoneyTransfer || hasIssuedByKBiz;

            _logger.LogDebug("K-BIZ parser analysis: Header={Header}, Transfer={Transfer}, Issued={Issued}, CanParse={CanParse}",
                hasKBizHeader, hasMoneyTransfer, hasIssuedByKBiz, canParse);

            return canParse;
        }

        public BankSlipData? Parse(string text, string imagePath, SlipCollection collection)
        {
            try
            {
                _logger.LogDebug("Starting K-BIZ parsing for {ImagePath}", Path.GetFileName(imagePath));

                var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                var slip = new BankSlipData
                {
                    OriginalFilePath = imagePath,
                    ParserUsed = GetParserName(),
                    IsKBizFormat = true,
                    ParsingNotes = new Dictionary<string, string>()
                };

                // Parse each section
                ParseTransactionDate(lines, slip);
                ParsePaymentSection(lines, slip);
                ParseAmount(lines, slip);
                ParseMemo(lines, slip);

                // Validate and cleanup
                ValidateAndCleanup(slip, imagePath);

                _logger.LogDebug("K-BIZ parsing completed for {ImagePath}: Date={Date}, Amount={Amount}, Receiver={Receiver}",
                    Path.GetFileName(imagePath), slip.TransactionDate, slip.Amount, slip.ReceiverName);

                return slip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing K-BIZ slip {ImagePath}", imagePath);
                return null;
            }
        }

        private void ParseTransactionDate(List<string> lines, BankSlipData slip)
        {
            // K-BIZ usually has date/time on the right side after "Money transferred successfully"
            var datePatterns = new[]
            {
                @"(\d{1,2})\s*([ก-ฮ\.]+)\s*(\d{4})\s*(\d{1,2}):(\d{2})", // Thai date with time
                @"(\d{1,2})/(\d{1,2})/(\d{4})", // Western date format
                @"(\d{1,2})\s*([ก-ฮ\.]+)\s*(\d{4})" // Thai date without time
            };

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                foreach (var pattern in datePatterns)
                {
                    var match = Regex.Match(line, pattern);
                    if (match.Success)
                    {
                        try
                        {
                            if (pattern.Contains("[ก-ฮ\\.]+")) // Thai date
                            {
                                slip.TransactionDate = ParsingUtilities.ParseThaiDate(line, _logger);
                            }
                            else // Western date
                            {
                                var day = int.Parse(match.Groups[1].Value);
                                var month = int.Parse(match.Groups[2].Value);
                                var year = int.Parse(match.Groups[3].Value);

                                // Validate date components for K-BIZ
                                if (month > 12 || month < 1)
                                {
                                    _logger.LogWarning("Invalid month {Month} in K-BIZ date, skipping", month);
                                    continue;
                                }

                                if (day > 31 || day < 1)
                                {
                                    _logger.LogWarning("Invalid day {Day} in K-BIZ date, skipping", day);
                                    continue;
                                }

                                // Convert Buddhist year if needed
                                if (year > 2500)
                                    year -= 543;

                                slip.TransactionDate = new DateTime(year, month, day);
                            }

                            slip.ParsingNotes.Add("DateSource", $"Line {i}: {line}");
                            _logger.LogDebug("Found K-BIZ date: {Date} from line: {Line}", slip.TransactionDate, line);
                            return;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("Failed to parse K-BIZ date from '{Line}': {Error}", line, ex.Message);
                        }
                    }
                }
            }

            _logger.LogWarning("No valid date found in K-BIZ slip, will use file timestamp");
        }

        private void ParsePaymentSection(List<string> lines, BankSlipData slip)
        {
            // Look for "To/To:" section which contains account and receiver info
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // Look for "To" indicator
                if (Regex.IsMatch(line, @"To[\/:]?", RegexOptions.IgnoreCase))
                {
                    _logger.LogDebug("Found To section at line {Index}: {Line}", i, line);

                    // Parse receiver account number (xxx-xxx-xxxx format)
                    ParseReceiverAccount(lines, i, slip);

                    // Parse receiver name (dual language: Thai + English)
                    ParseReceiverName(lines, i, slip);

                    break;
                }
            }
        }

        private void ParseReceiverAccount(List<string> lines, int startIndex, BankSlipData slip)
        {
            // Look for account pattern in next few lines after "To"
            for (int i = startIndex; i < Math.Min(startIndex + 5, lines.Count); i++)
            {
                var line = lines[i];
                var match = KBizAccountPattern.Match(line);

                if (match.Success)
                {
                    slip.ReceiverAccount = match.Value;
                    slip.ParsingNotes.Add("ReceiverAccountSource", $"Line {i}: {line}");
                    _logger.LogDebug("Found K-BIZ receiver account: {Account}", slip.ReceiverAccount);
                    return;
                }
            }

            _logger.LogWarning("No K-BIZ format account number found in To section");
        }

        private void ParseReceiverName(List<string> lines, int startIndex, BankSlipData slip)
        {
            // Look for names after the account number - typically Thai name then English name
            var foundNames = new List<string>();

            for (int i = startIndex; i < Math.Min(startIndex + 8, lines.Count); i++)
            {
                var line = lines[i];

                // Skip lines that are clearly not names
                if (ParsingUtilities.ShouldSkipLine(line) ||
                    KBizAccountPattern.IsMatch(line) ||
                    line.Contains("To/To", StringComparison.OrdinalIgnoreCase) ||
                    line.All(char.IsDigit))
                {
                    continue;
                }

                // Check if this looks like a name
                if (ParsingUtilities.IsValidThaiName(line))
                {
                    slip.ReceiverName = line;
                    foundNames.Add($"Thai: {line}");
                    _logger.LogDebug("Found Thai receiver name: {Name}", line);
                }
                else if (ParsingUtilities.IsValidEnglishName(line))
                {
                    slip.ReceiverNameEnglish = line;
                    foundNames.Add($"English: {line}");
                    _logger.LogDebug("Found English receiver name: {Name}", line);
                }
            }

            if (foundNames.Any())
            {
                slip.ParsingNotes.Add("ReceiverNameSource", string.Join(", ", foundNames));
            }

            // Fallback if no valid names found
            if (string.IsNullOrEmpty(slip.ReceiverName) && string.IsNullOrEmpty(slip.ReceiverNameEnglish))
            {
                slip.ReceiverName = "Could not resolve receiver";
                _logger.LogWarning("No valid receiver names found in K-BIZ To section");
            }
        }

        private void ParseAmount(List<string> lines, BankSlipData slip)
        {
            // Look for amount pattern, typically before fee section
            foreach (var line in lines)
            {
                var match = KBizAmountPattern.Match(line);
                if (match.Success)
                {
                    var amountText = match.Value;
                    slip.Amount = ParsingUtilities.ExtractAmount(amountText);

                    if (slip.Amount > 0)
                    {
                        slip.ParsingNotes.Add("AmountSource", $"Extracted from: {line}");
                        _logger.LogDebug("Found K-BIZ amount: {Amount} from line: {Line}", slip.Amount, line);
                        return;
                    }
                }
            }

            _logger.LogWarning("No valid amount found in K-BIZ slip");
        }

        private void ParseMemo(List<string> lines, BankSlipData slip)
        {
            // Look for "Memo/Memo" section specifically
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var match = MemoPattern.Match(line);

                if (match.Success && match.Groups.Count > 1)
                {
                    var memo = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(memo) && memo.Length > 2)
                    {
                        slip.Note = memo;
                        slip.ParsingNotes.Add("MemoSource", $"Line {i}: {line}");
                        _logger.LogDebug("Found K-BIZ memo: {Memo}", memo);
                        return;
                    }
                }

                // Also check if line starts with "Memo" pattern
                if (line.StartsWith("Memo", StringComparison.OrdinalIgnoreCase))
                {
                    // Look at next line for memo content
                    if (i + 1 < lines.Count)
                    {
                        var nextLine = lines[i + 1].Trim();
                        if (!string.IsNullOrWhiteSpace(nextLine) &&
                            !ParsingUtilities.ShouldSkipLine(nextLine) &&
                            nextLine.Length > 2)
                        {
                            slip.Note = nextLine;
                            slip.ParsingNotes.Add("MemoSource", $"Line {i + 1} after Memo: {nextLine}");
                            _logger.LogDebug("Found K-BIZ memo on next line: {Memo}", nextLine);
                            return;
                        }
                    }
                }
            }

            _logger.LogDebug("No memo found in K-BIZ slip");
        }

        private void ValidateAndCleanup(BankSlipData slip, string imagePath)
        {
            // Use file timestamp if date parsing failed
            if (slip.TransactionDate == default)
            {
                var fileInfo = new FileInfo(imagePath);
                slip.TransactionDate = fileInfo.LastWriteTime.AddYears(543); // Convert to Buddhist calendar
                slip.ParsingNotes.Add("DateFallback", "Used file timestamp");
                _logger.LogDebug("Using file timestamp for K-BIZ date: {Date}", slip.TransactionDate);
            }

            // Clean up receiver names
            if (!string.IsNullOrEmpty(slip.ReceiverName))
            {
                slip.ReceiverName = slip.ReceiverName.Trim();
            }

            if (!string.IsNullOrEmpty(slip.ReceiverNameEnglish))
            {
                slip.ReceiverNameEnglish = slip.ReceiverNameEnglish.Trim();
            }

            // Clean up note
            if (!string.IsNullOrEmpty(slip.Note))
            {
                slip.Note = slip.Note.Trim();
            }
        }

        public string GetParserName() => "K-BIZ Bank Slip Parser";

        public SlipFormat GetSupportedFormat() => SlipFormat.KBiz;

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
            if (string.IsNullOrEmpty(data.ReceiverName) && string.IsNullOrEmpty(data.ReceiverNameEnglish))
            {
                issues.Add("Missing receiver name");
                isValid = false;
            }

            // Account format validation for K-BIZ
            if (!string.IsNullOrEmpty(data.ReceiverAccount) && !KBizAccountPattern.IsMatch(data.ReceiverAccount))
            {
                issues.Add($"Invalid K-BIZ account format: {data.ReceiverAccount}");
                // Don't mark as invalid, just log warning
                _logger.LogWarning("K-BIZ account number doesn't match expected format: {Account}", data.ReceiverAccount);
            }

            if (!isValid)
            {
                data.ParsingNotes.Add("ValidationIssues", string.Join(", ", issues));
                _logger.LogWarning("K-BIZ validation failed: {Issues}", string.Join(", ", issues));
            }

            return isValid;
        }
    }
}