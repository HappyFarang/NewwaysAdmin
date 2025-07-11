// NewwaysAdmin.GoogleSheets/Services/BankSlipExportService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Interfaces;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.GoogleSheets.Services
{
    public class BankSlipExportService
    {
        private readonly GoogleSheetsService _googleSheetsService;
        private readonly UserSheetConfigService _configService;
        private readonly ISheetLayout<BankSlipData> _bankSlipLayout;
        private readonly GoogleSheetsConfig _config;
        private readonly ILogger<BankSlipExportService> _logger;

        public BankSlipExportService(
            GoogleSheetsService googleSheetsService,
            UserSheetConfigService configService,
            ISheetLayout<BankSlipData> bankSlipLayout,
            GoogleSheetsConfig config,
            ILogger<BankSlipExportService> logger)
        {
            _googleSheetsService = googleSheetsService ?? throw new ArgumentNullException(nameof(googleSheetsService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _bankSlipLayout = bankSlipLayout ?? throw new ArgumentNullException(nameof(bankSlipLayout));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Export bank slip data to Google Sheets
        /// </summary>
        public async Task<ExportResult> ExportBankSlipsAsync(IEnumerable<BankSlipData> bankSlips, string username, string templateId = "")
        {
            var result = new ExportResult();

            try
            {
                // Get user's email for ownership transfer
                var userEmail = await _configService.GetSettingAsync<string>(username, "BankSlip", "UserShareEmail", "");

                if (string.IsNullOrEmpty(userEmail))
                {
                    result.Success = false;
                    result.Errors.Add("User email is required for ownership transfer. Please set your email in the configuration.");
                    return result;
                }

                _logger.LogInformation("Starting bank slip export with ownership transfer for user {Username} to email {Email}", username, userEmail);

                // Get the sheet configuration/template
                SheetConfiguration? config = null;
                if (!string.IsNullOrEmpty(templateId))
                {
                    config = await _sheetConfigService.LoadConfigurationAsync(username, "BankSlip", templateId);
                }

                // Create sheet data using the layout service
                var sheetData = await _layoutService.CreateSheetDataAsync(bankSlips, config);

                if (sheetData?.Rows?.Any() != true)
                {
                    result.Success = false;
                    result.Errors.Add("No data to export");
                    return result;
                }

                // Generate spreadsheet title
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
                var templateName = config?.ConfigurationName ?? "Default";
                var title = $"BankSlips_{username}_{templateName}_{timestamp}";

                _logger.LogInformation("Creating spreadsheet '{Title}' for user {Username}", title, username);

                // Use the new create and transfer method
                var (success, spreadsheetId, url, error) = await _googleSheetsService.CreateAndTransferSpreadsheetAsync(
                    title,
                    sheetData,
                    userEmail,
                    "Bank Slips"
                );

                if (success && !string.IsNullOrEmpty(spreadsheetId))
                {
                    result.Success = true;
                    result.SheetId = spreadsheetId;
                    result.SheetUrl = url ?? $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";
                    result.RowsExported = sheetData.Rows.Count;

                    _logger.LogInformation("✅ Successfully exported {RowCount} bank slip records with ownership transferred to {Email}. " +
                                         "Spreadsheet: {SheetUrl}", result.RowsExported, userEmail, result.SheetUrl);

                    // Update user's last export info
                    await _configService.SetUserSettingAsync(username, "BankSlip", "LastExportDate", DateTime.UtcNow, username);
                    await _configService.SetUserSettingAsync(username, "BankSlip", "LastExportSheetId", spreadsheetId, username);
                    await _configService.SetUserSettingAsync(username, "BankSlip", "LastExportOwner", userEmail, username);

                    // Optional: Still share with admin for visibility (as editor, not owner)
                    if (!string.IsNullOrEmpty(_config.DefaultShareEmail) && _config.DefaultShareEmail != userEmail)
                    {
                        try
                        {
                            await _googleSheetsService.ShareSpreadsheetAsync(spreadsheetId, _config.DefaultShareEmail, "writer");
                            _logger.LogInformation("Also shared spreadsheet with admin email {AdminEmail} as editor", _config.DefaultShareEmail);
                        }
                        catch (Exception shareEx)
                        {
                            _logger.LogWarning(shareEx, "Failed to share with admin email, but export was successful");
                        }
                    }
                }
                else
                {
                    result.Success = false;
                    result.Errors.Add(error ?? "Unknown error during spreadsheet creation and transfer");
                    _logger.LogError("Failed to create and transfer spreadsheet for user {Username}: {Error}", username, error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during bank slip export with ownership transfer for user {Username}", username);
                result.Success = false;
                result.Errors.Add($"Unexpected error: {ex.Message}");
            }

            return result;
        }


        /// <summary>
        /// Automatically share the spreadsheet with all relevant users
        /// </summary>
        [Obsolete("Use ownership transfer instead to avoid service account storage issues")]
        private async Task ShareSpreadsheetWithUsers(string spreadsheetId, string username)
        {
            var emailsToShare = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. Always share with the default owner email from config
                if (!string.IsNullOrEmpty(_config.DefaultShareEmail))
                {
                    emailsToShare.Add(_config.DefaultShareEmail);
                    _logger.LogDebug("Added default share email: {Email}", _config.DefaultShareEmail);
                }

                // 2. Get user's personal email from their settings
                var userEmail = await _configService.GetSettingAsync<string>(username, "BankSlip", "UserShareEmail", "");
                if (!string.IsNullOrEmpty(userEmail))
                {
                    emailsToShare.Add(userEmail);
                    _logger.LogDebug("Added user email: {Email}", userEmail);
                }

                // 3. Share with each unique email
                foreach (var email in emailsToShare)
                {
                    try
                    {
                        _logger.LogInformation("Sharing spreadsheet {SpreadsheetId} with {Email}", spreadsheetId, email);

                        var shareSuccess = await _googleSheetsService.ShareSpreadsheetAsync(spreadsheetId, email, "writer");

                        if (shareSuccess)
                        {
                            _logger.LogInformation("✅ Successfully shared spreadsheet {SpreadsheetId} with {Email}",
                                spreadsheetId, email);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Failed to share spreadsheet {SpreadsheetId} with {Email}",
                                spreadsheetId, email);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error sharing spreadsheet {SpreadsheetId} with {Email}: {Error}",
                            spreadsheetId, email, ex.Message);
                        // Don't throw - sharing failure shouldn't break the export
                    }
                }

                _logger.LogInformation("Completed sharing process for spreadsheet {SpreadsheetId}. Shared with {Count} emails: {Emails}",
                    spreadsheetId, emailsToShare.Count, string.Join(", ", emailsToShare));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in sharing process for spreadsheet {SpreadsheetId}", spreadsheetId);
                // Don't throw - sharing failure shouldn't break the export
            }
        }

        /// <summary>
        /// Get user's export preferences
        /// </summary>
        public async Task<Dictionary<string, object>> GetUserPreferencesAsync(string username)
        {
            try
            {
                var config = await _configService.GetUserConfigAsync(username, "BankSlip");
                return config?.Settings ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user preferences for {Username}", username);
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Update user's export preferences
        /// </summary>
        public async Task<bool> UpdateUserPreferencesAsync(string username, Dictionary<string, object> preferences)
        {
            try
            {
                foreach (var preference in preferences)
                {
                    await _configService.SetUserSettingAsync(username, "BankSlip", preference.Key, preference.Value, username);
                }

                _logger.LogInformation("Updated export preferences for user {Username}", username);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user preferences for {Username}", username);
                return false;
            }
        }

        /// <summary>
        /// Get information about the user's last export
        /// </summary>
        public async Task<(DateTime? lastExportDate, string? lastSheetId, string? lastSheetUrl)> GetLastExportInfoAsync(string username)
        {
            try
            {
                var lastExportDate = await _configService.GetSettingAsync<DateTime?>(username, "BankSlip", "LastExportDate");
                var lastSheetId = await _configService.GetSettingAsync<string>(username, "BankSlip", "LastExportSheetId");

                string? lastSheetUrl = null;
                if (!string.IsNullOrEmpty(lastSheetId))
                {
                    lastSheetUrl = $"https://docs.google.com/spreadsheets/d/{lastSheetId}";
                }

                return (lastExportDate, lastSheetId, lastSheetUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving last export info for {Username}", username);
                return (null, null, null);
            }
        }

        /// <summary>
        /// Test the export functionality with sample data and automatic sharing
        /// </summary>
        public async Task<ExportResult> TestExportAsync(string username)
        {
            try
            {
                _logger.LogInformation("Starting test export for user {Username}", username);

                // Create sample data
                var sampleData = new List<BankSlipData>
                {
                    new BankSlipData
                    {
                        Id = Guid.NewGuid().ToString(),
                        TransactionDate = DateTime.Now.AddYears(543), // Buddhist calendar
                        Amount = 1500.00m,
                        AccountName = "Test User",
                        AccountNumber = "xxx-1234-567",
                        ReceiverName = "Test Receiver",
                        ReceiverAccount = "xxx-9876-543",
                        Note = "Test bank slip export with auto-sharing",
                        SlipCollectionName = "Test Collection",
                        ProcessedBy = username,
                        ProcessedAt = DateTime.UtcNow,
                        OriginalFilePath = "test-slip.jpg",
                        Status = BankSlipProcessingStatus.Completed
                    },
                    new BankSlipData
                    {
                        Id = Guid.NewGuid().ToString(),
                        TransactionDate = DateTime.Now.AddDays(-1).AddYears(543),
                        Amount = 750.50m,
                        AccountName = "Test User",
                        AccountNumber = "xxx-1234-567",
                        ReceiverName = "Another Receiver",
                        ReceiverAccount = "xxx-5555-666",
                        Note = "Second test bank slip with auto-sharing",
                        SlipCollectionName = "Test Collection",
                        ProcessedBy = username,
                        ProcessedAt = DateTime.UtcNow,
                        OriginalFilePath = "test-slip-2.jpg",
                        Status = BankSlipProcessingStatus.Completed
                    }
                };

                var result = await ExportBankSlipsAsync(username, sampleData, DateTime.Now.AddDays(-7), DateTime.Now);

                _logger.LogInformation("Test export completed for user {Username}. Success: {Success}",
                    username, result.Success);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during test export for user {Username}", username);
                return new ExportResult
                {
                    Success = false,
                    Errors = { $"Test export failed: {ex.Message}" }
                };
            }
        }
    }
}