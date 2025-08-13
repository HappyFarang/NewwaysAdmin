// NewwaysAdmin.WebAdmin/Services/BankSlips/Parsers/PatternBasedBankSlipParser.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SharedModels.Models.Documents;
using NewwaysAdmin.SharedModels.Services.Ocr;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers
{
    /// <summary>
    /// Modern pattern-based bank slip parser that replaces hardcoded parsing logic
    /// Uses the new OCR pattern system for flexible document processing
    /// </summary>
    public class PatternBasedBankSlipParser : IBankSlipParser
    {
        private readonly PatternLoaderService _patternLoader;
        private readonly ILogger<PatternBasedBankSlipParser> _logger;

        public PatternBasedBankSlipParser(
            PatternLoaderService patternLoader,
            ILogger<PatternBasedBankSlipParser> logger)
        {
            _patternLoader = patternLoader ?? throw new ArgumentNullException(nameof(patternLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public bool CanParse(string text, SlipCollection collection)
        {
            // Since we're pattern-based, we can attempt to parse any text
            // Success depends on whether patterns match, not hardcoded rules
            return !string.IsNullOrWhiteSpace(text);
        }

        public BankSlipData? Parse(string text, string imagePath, SlipCollection collection)
        {
            try
            {
                _logger.LogDebug("Starting pattern-based parsing for {ImagePath} using collection {CollectionName} ({DocumentType}/{FormatName})",
                    Path.GetFileName(imagePath), collection.Name, collection.DocumentType, collection.FormatName);

                // 🚀 NEW: Ensure collection has pattern-based fields
                if (string.IsNullOrEmpty(collection.DocumentType) || string.IsNullOrEmpty(collection.FormatName))
                {
                    collection.MigrateToPatternBased();
                    _logger.LogInformation("Auto-migrated collection {CollectionName} to pattern-based system", collection.Name);
                }

                // Step 1: Extract data using pattern system
                var genericDoc = _patternLoader.ExtractPatternsAsync(
                    text, imagePath, collection.DocumentType, collection.FormatName).Result;

                if (genericDoc.Status == DocumentProcessingStatus.Failed)
                {
                    _logger.LogWarning("Pattern extraction failed for {ImagePath}: {Error}",
                        Path.GetFileName(imagePath), genericDoc.ErrorReason);
                    return CreateFailedBankSlip(imagePath, genericDoc.ErrorReason, collection);
                }

                // Step 2: Convert to BankSlipData structure
                var bankSlip = ConvertToBankSlipData(genericDoc, collection);

                _logger.LogDebug("Pattern-based parsing completed for {ImagePath}: Date={Date}, Amount={Amount}, Receiver={Receiver}",
                    Path.GetFileName(imagePath), bankSlip.TransactionDate, bankSlip.Amount, bankSlip.ReceiverName);

                return bankSlip;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during pattern-based parsing for {ImagePath}", imagePath);
                return CreateFailedBankSlip(imagePath, $"Pattern parsing error: {ex.Message}", collection);
            }
        }

        /// <summary>
        /// Convert GenericDocumentData to BankSlipData for Google Sheets compatibility
        /// </summary>
        private BankSlipData ConvertToBankSlipData(GenericDocumentData doc, SlipCollection collection)
        {
            var bankSlip = new BankSlipData
            {
                Id = doc.Id,
                OriginalFilePath = doc.FilePath,
                ParserUsed = GetParserName(),
                IsKBizFormat = IsKBizFormat(collection.FormatName),
                ProcessedAt = doc.ProcessedAt,
                Status = BankSlipProcessingStatus.Completed,
                ParsingNotes = new Dictionary<string, string>(),

                // 🆕 NEW: Pattern-based metadata
                DocumentType = doc.DocumentType,
                FormatName = doc.DocumentFormat,
                ExtractedFieldCount = doc.GetFieldNames().Count,
                SuccessfulPatterns = doc.GetFieldNames().Where(name => doc.HasField(name)).ToList(),
                FailedPatterns = doc.GetFieldNames().Where(name => !doc.HasField(name)).ToList()
            };

            // Add processing metadata
            foreach (var note in doc.ProcessingNotes)
            {
                bankSlip.ParsingNotes[note.Key] = note.Value;
            }
            bankSlip.ParsingNotes["DocumentType"] = doc.DocumentType;
            bankSlip.ParsingNotes["DocumentFormat"] = doc.DocumentFormat;
            bankSlip.ParsingNotes["FieldCount"] = doc.GetFieldNames().Count.ToString();
            bankSlip.ParsingNotes["PatternSuccessRate"] = bankSlip.PatternSuccessRate.ToString("F1");

            // Map dynamic fields to BankSlipData structure
            MapFieldsToBankSlip(doc, bankSlip);

            return bankSlip;
        }

        /// <summary>
        /// Map extracted fields to BankSlipData properties using smart field detection
        /// </summary>
        private void MapFieldsToBankSlip(GenericDocumentData doc, BankSlipData bankSlip)
        {
            // 1. Map Date/Time fields
            MapDateFields(doc, bankSlip);

            // 2. Map Amount fields  
            MapAmountFields(doc, bankSlip);

            // 3. Map Recipient/To fields
            MapRecipientFields(doc, bankSlip);

            // 4. Map Account fields
            MapAccountFields(doc, bankSlip);

            // 5. Map Note/Memo fields
            MapMemoFields(doc, bankSlip);

            // 6. Store unmapped fields in ParsingNotes for debugging
            StoreUnmappedFields(doc, bankSlip);
        }

        private void MapDateFields(GenericDocumentData doc, BankSlipData bankSlip)
        {
            // Look for date-related fields
            var dateFields = new[] { "Date", "TransactionDate", "Time", "DateTime", "When" };

            foreach (var fieldName in dateFields)
            {
                var dateText = doc.GetFieldText(fieldName);
                if (string.IsNullOrWhiteSpace(dateText))
                    continue;

                if (TryParseThaiDate(dateText, out var parsedDate))
                {
                    bankSlip.TransactionDate = parsedDate;
                    bankSlip.ParsingNotes[$"DateSource"] = fieldName;
                    bankSlip.ParsingNotes[$"DateRaw"] = dateText;
                    break;
                }
            }

            // If no date found, use file timestamp as fallback
            if (bankSlip.TransactionDate == default)
            {
                bankSlip.TransactionDate = File.GetLastWriteTime(bankSlip.OriginalFilePath);
                bankSlip.ParsingNotes["DateSource"] = "FileTimestamp";
                _logger.LogWarning("No date field found, using file timestamp for {FilePath}",
                    Path.GetFileName(bankSlip.OriginalFilePath));
            }
        }

        private void MapAmountFields(GenericDocumentData doc, BankSlipData bankSlip)
        {
            // Look for amount-related fields (prefer Total over Fee)
            var amountFields = new[] { "Total", "Amount", "GrandTotal", "NetAmount", "Fee", "Cost" };

            foreach (var fieldName in amountFields)
            {
                var amountText = doc.GetFieldText(fieldName);
                if (string.IsNullOrWhiteSpace(amountText))
                    continue;

                if (TryParseAmount(amountText, out var parsedAmount))
                {
                    bankSlip.Amount = parsedAmount;
                    bankSlip.ParsingNotes["AmountSource"] = fieldName;
                    bankSlip.ParsingNotes["AmountRaw"] = amountText;
                    break;
                }
            }
        }

        private void MapRecipientFields(GenericDocumentData doc, BankSlipData bankSlip)
        {
            // Look for recipient-related fields
            var recipientFields = new[] { "To", "Recipient", "ReceiverName", "Payee", "Beneficiary" };

            foreach (var fieldName in recipientFields)
            {
                var recipientText = doc.GetFieldText(fieldName);
                if (string.IsNullOrWhiteSpace(recipientText))
                    continue;

                bankSlip.ReceiverName = CleanRecipientName(recipientText);
                bankSlip.ParsingNotes["RecipientSource"] = fieldName;
                bankSlip.ParsingNotes["RecipientRaw"] = recipientText;
                break;
            }

            // Handle dual-language names for K-BIZ format
            if (bankSlip.IsKBizFormat)
            {
                var englishFields = new[] { "ToEnglish", "RecipientEnglish", "NameEnglish" };
                foreach (var fieldName in englishFields)
                {
                    var englishText = doc.GetFieldText(fieldName);
                    if (!string.IsNullOrWhiteSpace(englishText))
                    {
                        bankSlip.ReceiverNameEnglish = CleanRecipientName(englishText);
                        break;
                    }
                }
            }
        }

        private void MapAccountFields(GenericDocumentData doc, BankSlipData bankSlip)
        {
            var accountFields = new[] { "Account", "AccountNumber", "SenderAccount", "FromAccount" };

            foreach (var fieldName in accountFields)
            {
                var accountText = doc.GetFieldText(fieldName);
                if (string.IsNullOrWhiteSpace(accountText))
                    continue;

                bankSlip.AccountNumber = accountText.Trim();
                bankSlip.ParsingNotes["AccountSource"] = fieldName;
                break;
            }

            var recipientAccountFields = new[] { "ReceiverAccount", "ToAccount", "BeneficiaryAccount" };
            foreach (var fieldName in recipientAccountFields)
            {
                var accountText = doc.GetFieldText(fieldName);
                if (string.IsNullOrWhiteSpace(accountText))
                    continue;

                bankSlip.ReceiverAccount = accountText.Trim();
                bankSlip.ParsingNotes["ReceiverAccountSource"] = fieldName;
                break;
            }
        }

        private void MapMemoFields(GenericDocumentData doc, BankSlipData bankSlip)
        {
            var memoFields = new[] { "Memo", "Note", "Subject", "Description", "Reference", "Remark" };

            foreach (var fieldName in memoFields)
            {
                var memoText = doc.GetFieldText(fieldName);
                if (string.IsNullOrWhiteSpace(memoText))
                    continue;

                bankSlip.Note = memoText.Trim();
                bankSlip.ParsingNotes["MemoSource"] = fieldName;
                break;
            }
        }

        private void StoreUnmappedFields(GenericDocumentData doc, BankSlipData bankSlip)
        {
            var mappedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Date", "TransactionDate", "Time", "DateTime", "When",
                "Total", "Amount", "GrandTotal", "NetAmount", "Fee", "Cost",
                "To", "Recipient", "ReceiverName", "Payee", "Beneficiary",
                "ToEnglish", "RecipientEnglish", "NameEnglish",
                "Account", "AccountNumber", "SenderAccount", "FromAccount",
                "ReceiverAccount", "ToAccount", "BeneficiaryAccount",
                "Memo", "Note", "Subject", "Description", "Reference", "Remark"
            };

            foreach (var fieldName in doc.GetFieldNames())
            {
                if (!mappedFields.Contains(fieldName))
                {
                    var value = doc.GetFieldText(fieldName);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        bankSlip.ParsingNotes[$"Unmapped_{fieldName}"] = value;
                    }
                }
            }
        }

        #region Parsing Helpers

        private bool TryParseThaiDate(string dateText, out DateTime date)
        {
            date = default;

            try
            {
                // Handle various date formats including Thai Buddhist calendar
                var cleaned = dateText.Trim();

                // Try direct parsing first
                if (DateTime.TryParse(cleaned, out date))
                    return true;

                // Handle Buddhist year (subtract 543)
                var buddhistYearMatch = Regex.Match(cleaned, @"25\d{2}");
                if (buddhistYearMatch.Success)
                {
                    var buddhistYear = int.Parse(buddhistYearMatch.Value);
                    var christianYear = buddhistYear - 543;
                    var convertedDate = cleaned.Replace(buddhistYearMatch.Value, christianYear.ToString());

                    if (DateTime.TryParse(convertedDate, out date))
                        return true;
                }

                // Try different separators
                var separators = new[] { "/", "-", ".", " " };
                foreach (var sep in separators)
                {
                    if (cleaned.Contains(sep))
                    {
                        var normalized = cleaned.Replace(sep, "/");
                        if (DateTime.TryParse(normalized, out date))
                            return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseAmount(string amountText, out decimal amount)
        {
            amount = 0;

            try
            {
                var cleaned = amountText
                    .Replace("฿", "")
                    .Replace("THB", "")
                    .Replace("บาท", "")
                    .Replace("Baht", "")
                    .Replace(",", "")
                    .Trim()
                    .Split(' ')[0]; // Take first part before any space

                return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
            }
            catch
            {
                return false;
            }
        }

        private string CleanRecipientName(string recipientText)
        {
            if (string.IsNullOrWhiteSpace(recipientText))
                return string.Empty;

            // Remove common artifacts
            var cleaned = recipientText.Trim();
            var artifacts = new[] { "จํานวน:", "จำนวน:", "ค่าธรรมเนียม:", "To:", "ไปยัง:" };

            foreach (var artifact in artifacts)
            {
                cleaned = cleaned.Replace(artifact, "").Trim();
            }

            return cleaned;
        }

        private bool IsKBizFormat(string formatName)
        {
            return formatName.Contains("KBIZ", StringComparison.OrdinalIgnoreCase) ||
                   formatName.Contains("K-BIZ", StringComparison.OrdinalIgnoreCase);
        }

        private BankSlipData CreateFailedBankSlip(string imagePath, string errorReason, SlipCollection collection)
        {
            return new BankSlipData
            {
                OriginalFilePath = imagePath,
                ParserUsed = GetParserName(),
                Status = BankSlipProcessingStatus.Failed,
                ErrorReason = errorReason,
                TransactionDate = File.GetLastWriteTime(imagePath),
                ReceiverName = "Could not parse",
                DocumentType = collection.DocumentType ?? "BankSlips",
                FormatName = collection.FormatName ?? "Unknown",
                ParsingNotes = new Dictionary<string, string>
                {
                    ["FailureReason"] = errorReason,
                    ["ProcessedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["CollectionName"] = collection.Name,
                    ["PatternPath"] = $"{collection.DocumentType}/{collection.FormatName}"
                }
            };
        }

        #endregion

        #region IBankSlipParser Implementation

        public string GetParserName() => "Pattern-Based Parser";

        public SlipFormat GetSupportedFormat() => SlipFormat.Original; // Supports all formats via patterns

        public bool ValidateParsedData(BankSlipData data)
        {
            // Basic validation
            return data.TransactionDate != default &&
                   data.Amount > 0 &&
                   !string.IsNullOrWhiteSpace(data.ReceiverName);
        }

        #endregion
    }
}