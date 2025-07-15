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

        private readonly SheetConfigurationService _sheetConfigService;
        private readonly SimpleEmailStorageService _emailStorage;


        public BankSlipExportService(
     GoogleSheetsService googleSheetsService,
     UserSheetConfigService configService,
     ISheetLayout<BankSlipData> bankSlipLayout,
     GoogleSheetsConfig config,
     ILogger<BankSlipExportService> logger,
     SheetConfigurationService sheetConfigService,
     SimpleEmailStorageService emailStorage)
        {
            _googleSheetsService = googleSheetsService ?? throw new ArgumentNullException(nameof(googleSheetsService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _bankSlipLayout = bankSlipLayout ?? throw new ArgumentNullException(nameof(bankSlipLayout));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sheetConfigService = sheetConfigService ?? throw new ArgumentNullException(nameof(sheetConfigService));
            _emailStorage = emailStorage ?? throw new ArgumentNullException(nameof(emailStorage));
        }

        /// <summary>
        /// Export bank slip data to Google Sheets
        /// </summary>
        public async Task<ExportResult> ExportBankSlipsAsync(IEnumerable<BankSlipData> bankSlips, string username, string templateId = "")
        {
            var result = new ExportResult();

            try
            {
                // Get user's email for ownership transfer using the new email service
                _logger.LogInformation("🔄 Getting user email for {Username}...", username);
                var userEmail = await _emailStorage.GetUserEmailAsync(username);
                _logger.LogInformation("📧 Retrieved email: {Email}", string.IsNullOrEmpty(userEmail) ? "NONE" : userEmail);

                if (string.IsNullOrEmpty(userEmail))
                {
                    result.Success = false;
                    result.Errors.Add("User email is required for ownership transfer. Please set your email in the configuration.");
                    return result;
                }

                _logger.LogInformation("Starting bank slip export with ownership transfer for user {Username} to email {Email}", username, userEmail);

                // Get the sheet configuration/template
                // Get the sheet configuration/template
                UserSheetConfiguration? config = null;
                if (!string.IsNullOrEmpty(templateId))
                {
                    _logger.LogInformation("🔄 Loading template configuration: {TemplateId}", templateId);

                    // Extract configuration name from templateId
                    // templateId format is "BankSlips_ConfigurationName"
                    string configName = "Default";
                    if (templateId.StartsWith("BankSlips_"))
                    {
                        configName = templateId.Substring("BankSlips_".Length);
                    }
                    else
                    {
                        // If templateId doesn't match expected format, try using it directly
                        configName = templateId;
                    }

                    _logger.LogInformation("🔄 Extracted config name: {ConfigName} from templateId: {TemplateId}", configName, templateId);

                    // Load the configuration with the correct module name "BankSlips" (not "BankSlip")
                    config = await _sheetConfigService.LoadConfigurationAsync(username, "BankSlips", configName);

                    if (config != null)
                    {
                        _logger.LogInformation("✅ Successfully loaded template configuration: {ConfigName}", configName);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Template configuration not found: {ConfigName}, will use default", configName);
                    }
                }

                // If no config found, create a default one
                if (config == null)
                {
                    _logger.LogInformation("🔄 Creating default configuration...");
                    config = _sheetConfigService.CreateDefaultConfiguration("BankSlips");
                    config.ConfigurationName = "Default";
                }

                // Create sheet data using the configuration service
                _logger.LogInformation("🔄 Generating sheet data...");
                var bankSlipsList = bankSlips.ToList();
                var sheetData = _sheetConfigService.GenerateSheetData(
                    bankSlipsList,
                    config,
                    GetBankSlipPropertyValue
                );

                if (sheetData?.Rows?.Any() != true)
                {
                    result.Success = false;
                    result.Errors.Add("No data to export");
                    return result;
                }

                _logger.LogInformation("✅ Generated sheet data with {RowCount} rows", sheetData.Rows.Count);

                // Generate spreadsheet title
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm");
                var templateName = config?.ConfigurationName ?? "Default";
                var title = $"BankSlips_{username}_{templateName}_{timestamp}";

                _logger.LogInformation("🔄 Creating spreadsheet '{Title}' for user {Username}", title, username);

                // Use the new create and transfer method with enhanced error handling
                string? spreadsheetId = null;
                string? url = null;
                string? error = null;
                bool success = false;
                
                try
                {
                    _logger.LogInformation("🔄 About to call CreateAndTransferSpreadsheetAsync...");
                    _logger.LogInformation("🔄 Parameters: Title={Title}, OwnerEmail={Email}, WorksheetName=Bank Slips", title, userEmail);

                    (success, spreadsheetId, url, error) = await _googleSheetsService.CreateWithProperOAuth2Async(
                        title,
                        sheetData,
                        userEmail
                    );

                    _logger.LogInformation("✅ CreateAndTransferSpreadsheetAsync completed: Success={Success}, SpreadsheetId={SpreadsheetId}, Error={Error}",
                        success, spreadsheetId ?? "NULL", error ?? "NONE");
                }
                catch (Exception createEx)
                {
                    _logger.LogError(createEx, "❌ Exception during CreateAndTransferSpreadsheetAsync for title '{Title}'", title);
                    result.Success = false;
                    result.Errors.Add($"Spreadsheet creation exception: {createEx.Message}");

                    // Add more details about the exception
                    if (createEx.InnerException != null)
                    {
                        _logger.LogError("❌ Inner exception: {InnerException}", createEx.InnerException.Message);
                        result.Errors.Add($"Inner exception: {createEx.InnerException.Message}");
                    }

                    return result;
                }

                if (success && !string.IsNullOrEmpty(spreadsheetId))
                {
                    result.Success = true;
                    result.SheetId = spreadsheetId;
                    result.SheetUrl = url ?? $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";
                    result.RowsExported = sheetData.Rows.Count;

                    _logger.LogInformation("✅ Successfully exported {RowCount} bank slip records with ownership transferred to {Email}. " +
                                         "Spreadsheet: {SheetUrl}", result.RowsExported, userEmail, result.SheetUrl);

                    // Update user's last export info (keeping the old config service for this)
                    try
                    {
                        await _configService.SetUserSettingAsync(username, "BankSlip", "LastExportDate", DateTime.UtcNow, username);
                        await _configService.SetUserSettingAsync(username, "BankSlip", "LastExportSheetId", spreadsheetId, username);
                        await _configService.SetUserSettingAsync(username, "BankSlip", "LastExportOwner", userEmail, username);
                        _logger.LogInformation("✅ Updated user export history");
                    }
                    catch (Exception historyEx)
                    {
                        _logger.LogWarning(historyEx, "⚠️ Failed to update export history, but export was successful");
                    }

                    // Optional: Still share with admin for visibility (as editor, not owner)
                    if (!string.IsNullOrEmpty(_config.DefaultShareEmail) && _config.DefaultShareEmail != userEmail)
                    {
                        try
                        {
                            _logger.LogInformation("🔄 Sharing with admin email {AdminEmail}...", _config.DefaultShareEmail);
                            await _googleSheetsService.ShareSpreadsheetAsync(spreadsheetId, _config.DefaultShareEmail, "writer");
                            _logger.LogInformation("✅ Also shared spreadsheet with admin email {AdminEmail} as editor", _config.DefaultShareEmail);
                        }
                        catch (Exception shareEx)
                        {
                            _logger.LogWarning(shareEx, "⚠️ Failed to share with admin email, but export was successful");
                        }
                    }
                }
                else
                {
                    result.Success = false;
                    var errorMessage = error ?? "Unknown error during spreadsheet creation and transfer";
                    result.Errors.Add(errorMessage);
                    _logger.LogError("❌ Failed to create and transfer spreadsheet for user {Username}: {Error}", username, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error during bank slip export with ownership transfer for user {Username}", username);
                result.Success = false;
                result.Errors.Add($"Unexpected error: {ex.Message}");

                // Add stack trace for debugging
                _logger.LogError("❌ Stack trace: {StackTrace}", ex.StackTrace);
            }

            return result;
        }

        /// <summary>
        /// Get property value from BankSlipData for sheet generation
        /// </summary>
        private object? GetBankSlipPropertyValue(BankSlipData bankSlip, string propertyName)
        {
            return propertyName switch
            {
                "TransactionDate" => bankSlip.TransactionDate,
                "Amount" => bankSlip.Amount,
                "AccountName" => bankSlip.AccountName,
                "AccountNumber" => bankSlip.AccountNumber,
                "ReceiverName" => bankSlip.ReceiverName,
                "ReceiverAccount" => bankSlip.ReceiverAccount,
                "Note" => bankSlip.Note,
                "SlipCollectionName" => bankSlip.SlipCollectionName,
                "ProcessedBy" => bankSlip.ProcessedBy,
                "ProcessedAt" => bankSlip.ProcessedAt,
                "OriginalFilePath" => bankSlip.OriginalFilePath,
                "Status" => bankSlip.Status.ToString(),
                "ErrorReason" => bankSlip.ErrorReason,
                "Id" => bankSlip.Id,
                _ => null
            };
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
                var userEmail = await _emailStorage.GetUserEmailAsync(username);
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
    }
}