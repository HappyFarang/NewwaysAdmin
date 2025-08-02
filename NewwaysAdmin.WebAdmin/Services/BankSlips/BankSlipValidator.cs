// NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipValidator.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class BankSlipValidator
    {
        private readonly ILogger<BankSlipValidator> _logger;

        public BankSlipValidator(ILogger<BankSlipValidator> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Validates parsed slip data based on collection settings and format type
        /// </summary>
        public bool ValidateSlipData(BankSlipData slipData, SlipCollection collection)
        {
            if (slipData == null)
            {
                _logger.LogWarning("Validation failed: Slip data is null");
                return false;
            }

            var issues = new List<string>();
            var isValid = true;

            // Basic validation
            isValid &= ValidateDate(slipData, issues);
            isValid &= ValidateAmount(slipData, issues);
            isValid &= ValidateReceiverInfo(slipData, issues);

            // Format-specific validation
            if (collection.IsKBizFormat)
            {
                isValid &= ValidateKBizSpecific(slipData, issues);
            }
            else
            {
                isValid &= ValidateOriginalSpecific(slipData, issues);
            }

            // Enhanced validation if enabled
            if (collection.ProcessingSettings.UseEnhancedDateValidation)
            {
                isValid &= ValidateEnhancedDate(slipData, issues);
            }

            if (collection.ProcessingSettings.ValidateAccountFormat)
            {
                isValid &= ValidateAccountFormats(slipData, collection, issues);
            }

            // Log validation results
            if (!isValid)
            {
                var issueText = string.Join(", ", issues);
                slipData.ErrorReason = issueText;
                slipData.ParsingNotes.Add("ValidationIssues", issueText);
                _logger.LogWarning("Validation failed for {FilePath}: {Issues}",
                    Path.GetFileName(slipData.OriginalFilePath), issueText);
            }
            else
            {
                _logger.LogDebug("Validation passed for {FilePath}",
                    Path.GetFileName(slipData.OriginalFilePath));
            }

            return isValid;
        }

        private bool ValidateDate(BankSlipData slipData, List<string> issues)
        {
            if (slipData.TransactionDate == default)
            {
                issues.Add("Missing transaction date");
                return false;
            }

            // Date range validation (reasonable business dates)
            var minDate = new DateTime(2017, 1, 1);
            var maxDate = DateTime.Now.AddDays(1); // Allow next day for different timezones

            if (slipData.TransactionDate < minDate || slipData.TransactionDate > maxDate)
            {
                issues.Add($"Transaction date out of valid range: {slipData.TransactionDate:yyyy-MM-dd}");
                return false;
            }

            return true;
        }

        private bool ValidateEnhancedDate(BankSlipData slipData, List<string> issues)
        {
            // Additional date validation for impossible dates (like month > 12)
            var date = slipData.TransactionDate;

            if (date.Month > 12 || date.Month < 1)
            {
                issues.Add($"Invalid month in date: {date.Month}");
                return false;
            }

            if (date.Day > 31 || date.Day < 1)
            {
                issues.Add($"Invalid day in date: {date.Day}");
                return false;
            }

            // Check for common OCR errors (like 2568-34-09)
            if (date.Year > 2570 || date.Year < 2560)
            {
                issues.Add($"Suspicious year in Buddhist calendar: {date.Year}");
                return false;
            }

            return true;
        }

        private bool ValidateAmount(BankSlipData slipData, List<string> issues)
        {
            if (slipData.Amount <= 0)
            {
                issues.Add("Missing or invalid amount");
                return false;
            }

            // Sanity check for extremely large amounts
            if (slipData.Amount > 10_000_000) // 10 million baht
            {
                issues.Add($"Amount seems unusually large: {slipData.Amount:N2}");
                // Don't fail validation, just warn
                _logger.LogWarning("Unusually large amount detected: {Amount} in {FilePath}",
                    slipData.Amount, Path.GetFileName(slipData.OriginalFilePath));
            }

            return true;
        }

        private bool ValidateReceiverInfo(BankSlipData slipData, List<string> issues)
        {
            // For K-BIZ, check both Thai and English names
            if (slipData.IsKBizFormat)
            {
                if (string.IsNullOrEmpty(slipData.ReceiverName) && string.IsNullOrEmpty(slipData.ReceiverNameEnglish))
                {
                    issues.Add("Missing receiver name (both Thai and English)");
                    return false;
                }
            }
            else
            {
                if (string.IsNullOrEmpty(slipData.ReceiverName))
                {
                    issues.Add("Missing receiver name");
                    return false;
                }
            }

            return true;
        }

        private bool ValidateKBizSpecific(BankSlipData slipData, List<string> issues)
        {
            // K-BIZ specific validations
            var isValid = true;

            // Account format validation (xxx-xxx-xxxx)
            if (!string.IsNullOrEmpty(slipData.ReceiverAccount))
            {
                var kbizAccountPattern = @"^\d{3}-\d{3}-\d{4}$";
                if (!Regex.IsMatch(slipData.ReceiverAccount, kbizAccountPattern))
                {
                    issues.Add($"K-BIZ account format invalid: {slipData.ReceiverAccount} (expected xxx-xxx-xxxx)");
                    isValid = false;
                }
            }

            // Check for dual language names if setting is enabled
            if (!string.IsNullOrEmpty(slipData.ReceiverName) && string.IsNullOrEmpty(slipData.ReceiverNameEnglish))
            {
                _logger.LogInformation("K-BIZ slip has only Thai name, English name missing for {FilePath}",
                    Path.GetFileName(slipData.OriginalFilePath));
            }

            return isValid;
        }

        private bool ValidateOriginalSpecific(BankSlipData slipData, List<string> issues)
        {
            // Original format specific validations
            var isValid = true;

            // Check for PromptPay indicators
            if (string.IsNullOrEmpty(slipData.ReceiverAccount))
            {
                _logger.LogInformation("Original slip missing receiver account for {FilePath}",
                    Path.GetFileName(slipData.OriginalFilePath));
            }

            return isValid;
        }

        private bool ValidateAccountFormats(BankSlipData slipData, SlipCollection collection, List<string> issues)
        {
            var isValid = true;

            // Validate sender account format
            if (!string.IsNullOrEmpty(slipData.AccountNumber))
            {
                if (collection.IsKBizFormat)
                {
                    // K-BIZ may have different sender account formats
                    if (slipData.AccountNumber.Length < 5)
                    {
                        issues.Add($"Sender account too short: {slipData.AccountNumber}");
                        isValid = false;
                    }
                }
                else
                {
                    // Original format patterns
                    var originalPatterns = new[]
                    {
                        @"[xX]{3,}[-\d]+",
                        @"[\d]+-[xX]{3,}",
                        @"\d{3}-\d-\d{5}-\d"
                    };

                    var matchesPattern = originalPatterns.Any(pattern =>
                        Regex.IsMatch(slipData.AccountNumber, pattern));

                    if (!matchesPattern && slipData.AccountNumber.Length < 8)
                    {
                        issues.Add($"Sender account format suspicious: {slipData.AccountNumber}");
                        // Don't fail validation, just warn
                        _logger.LogWarning("Suspicious sender account format: {Account} in {FilePath}",
                            slipData.AccountNumber, Path.GetFileName(slipData.OriginalFilePath));
                    }
                }
            }

            return isValid;
        }

        /// <summary>
        /// Validates that required fields are present based on slip format
        /// </summary>
        public bool ValidateRequiredFields(BankSlipData slipData, SlipCollection collection)
        {
            var requiredFields = new List<string>();

            if (slipData.TransactionDate == default)
                requiredFields.Add("Transaction Date");

            if (slipData.Amount <= 0)
                requiredFields.Add("Amount");

            if (collection.IsKBizFormat)
            {
                if (string.IsNullOrEmpty(slipData.ReceiverName) && string.IsNullOrEmpty(slipData.ReceiverNameEnglish))
                    requiredFields.Add("Receiver Name");
            }
            else
            {
                if (string.IsNullOrEmpty(slipData.ReceiverName))
                    requiredFields.Add("Receiver Name");
            }

            if (requiredFields.Any())
            {
                var missing = string.Join(", ", requiredFields);
                slipData.ErrorReason = $"Missing required fields: {missing}";
                _logger.LogWarning("Missing required fields for {FilePath}: {Fields}",
                    Path.GetFileName(slipData.OriginalFilePath), missing);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Performs post-processing validation and cleanup
        /// </summary>
        public void PostProcessValidation(BankSlipData slipData, SlipCollection collection)
        {
            // Clean up any remaining invalid data
            if (!string.IsNullOrEmpty(slipData.ReceiverName))
            {
                slipData.ReceiverName = slipData.ReceiverName.Trim();

                // Remove system artifacts
                var systemArtifacts = new[] { "จํานวน:", "จำนวน:", "ค่าธรรมเนียม:" };
                foreach (var artifact in systemArtifacts)
                {
                    slipData.ReceiverName = slipData.ReceiverName.Replace(artifact, "").Trim();
                }
            }

            if (!string.IsNullOrEmpty(slipData.ReceiverNameEnglish))
            {
                slipData.ReceiverNameEnglish = slipData.ReceiverNameEnglish.Trim();
            }

            if (!string.IsNullOrEmpty(slipData.Note))
            {
                slipData.Note = slipData.Note.Trim();

                // Remove note artifacts
                var noteArtifacts = new[] { "บันทึก:", "หมายเหตุ:", "Memo:", "Note:" };
                foreach (var artifact in noteArtifacts)
                {
                    if (slipData.Note.StartsWith(artifact, StringComparison.OrdinalIgnoreCase))
                    {
                        slipData.Note = slipData.Note.Substring(artifact.Length).Trim();
                    }
                }
            }

            // Set validation metadata
            slipData.ParsingNotes.Add("PostProcessed", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
            slipData.ParsingNotes.Add("CollectionFormat", collection.IsKBizFormat ? "K-BIZ" : "Original");
        }
    }
}
