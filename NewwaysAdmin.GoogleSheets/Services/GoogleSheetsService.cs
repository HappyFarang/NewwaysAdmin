// NewwaysAdmin.GoogleSheets/Services/GoogleSheetsService.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Exceptions;
using NewwaysAdmin.GoogleSheets.Models;
using System.Drawing;
using Color = Google.Apis.Sheets.v4.Data.Color;

namespace NewwaysAdmin.GoogleSheets.Services
{
    public class GoogleSheetsService : IDisposable
    {
        private readonly GoogleSheetsConfig _config;
        private readonly ILogger<GoogleSheetsService> _logger;
        private SheetsService? _sheetsService;
        private DriveService? _driveService;
        private bool _disposed = false;

        public GoogleSheetsService(GoogleSheetsConfig config, ILogger<GoogleSheetsService> logger)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private SheetsService GetSheetsService()
        {
            if (_sheetsService != null)
                return _sheetsService;

            try
            {
                if (!File.Exists(_config.CredentialsPath))
                {
                    throw new GoogleSheetsAuthenticationException($"Credentials file not found: {_config.CredentialsPath}");
                }

                GoogleCredential credential;
                using (var stream = new FileStream(_config.CredentialsPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream)
                        .CreateScoped(SheetsService.Scope.Spreadsheets, DriveService.Scope.Drive);
                }

                _sheetsService = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _config.ApplicationName,
                });

                _logger.LogInformation("Google Sheets service initialized successfully");
                return _sheetsService;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Google Sheets service");
                throw new GoogleSheetsAuthenticationException("Failed to initialize Google Sheets service", ex);
            }
        }
        /// <summary>
        /// Create spreadsheet using proper OAuth2 UserCredential flow
        /// This will prompt for browser login the first time, then save tokens
        /// Uses your personal Google account storage (no quota issues!)
        /// </summary>


        /// <summary>
        /// Apply checkbox validation to columns that have IsCheckbox = true
        /// </summary>
        // Replace the ApplyCheckboxFormattingAsync method in GoogleSheetsService.cs with this fixed version:

        private async Task ApplyCheckboxFormattingAsync(SheetsService service, string spreadsheetId, SheetData sheetData, string worksheetName)
        {
            try
            {
                _logger.LogInformation("🔄 Applying checkbox formatting...");

                // Find columns that need checkbox formatting
                var checkboxColumns = new List<int>();
                if (sheetData.Rows.Any())
                {
                    // FIXED: Find a row that actually has checkboxes, not just "not header"
                    var rowWithCheckboxes = sheetData.Rows.FirstOrDefault(r => r.Cells.Any(c => c.IsCheckbox));
                    if (rowWithCheckboxes != null)
                    {
                        _logger.LogInformation("🔍 Found row with checkboxes, scanning for checkbox columns...");
                        for (int i = 0; i < rowWithCheckboxes.Cells.Count; i++)
                        {
                            if (rowWithCheckboxes.Cells[i].IsCheckbox)
                            {
                                checkboxColumns.Add(i);
                                _logger.LogInformation("🔲 Found checkbox at column {ColumnIndex}", i);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("🔍 No rows with checkboxes found");
                    }
                }

                if (!checkboxColumns.Any())
                {
                    _logger.LogInformation("ℹ️ No checkbox columns found, skipping checkbox formatting");
                    return;
                }

                _logger.LogInformation("📋 Found {Count} checkbox columns: {Columns}",
                    checkboxColumns.Count, string.Join(", ", checkboxColumns));

                // Get the sheet ID for the worksheet
                var spreadsheetResponse = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
                var sheet = spreadsheetResponse.Sheets.FirstOrDefault(s => s.Properties.Title == worksheetName);
                if (sheet == null)
                {
                    _logger.LogWarning("⚠️ Could not find worksheet {WorksheetName} for checkbox formatting", worksheetName);
                    return;
                }

                var sheetId = sheet.Properties.SheetId.Value;
                var totalRows = sheetData.Rows.Count;

                // Create batch request for checkbox validation
                var requests = new List<Request>();

                foreach (var columnIndex in checkboxColumns)
                {
                    var columnLetter = GetColumnLetter(columnIndex);
                    _logger.LogInformation("🔲 Adding checkbox validation to column {Column} (index {Index})",
                        columnLetter, columnIndex);

                    // Create data validation rule for checkbox
                    var dataValidationRule = new DataValidationRule
                    {
                        Condition = new BooleanCondition
                        {
                            Type = "BOOLEAN"
                        },
                        ShowCustomUi = true,
                        Strict = false
                    };

                    // Apply checkbox validation ONLY to data rows, NOT to header or formula rows
                    var startRow = 0;

                    // Find where the actual data rows start (skip header and formula rows)
                    for (int rowIndex = 0; rowIndex < sheetData.Rows.Count; rowIndex++)
                    {
                        var row = sheetData.Rows[rowIndex];

                        // Skip header rows
                        if (row.IsHeader)
                        {
                            startRow = rowIndex + 1;
                            continue;
                        }

                        // Skip formula rows (rows where this column contains a formula)
                        if (columnIndex < row.Cells.Count &&
                            row.Cells[columnIndex].Value?.ToString()?.StartsWith("=") == true)
                        {
                            startRow = rowIndex + 1;
                            _logger.LogInformation("🔲 Skipping formula row {RowIndex} for column {Column}", rowIndex, columnIndex);
                            continue;
                        }

                        // If this row has a checkbox in this column, this is where data starts
                        if (columnIndex < row.Cells.Count && row.Cells[columnIndex].IsCheckbox)
                        {
                            startRow = rowIndex;
                            _logger.LogInformation("🔲 Found data start at row {RowIndex} for column {Column}", rowIndex, columnIndex);
                            break;
                        }
                    }

                    // Find where data rows end (count rows with checkboxes in this column)
                    var dataRowCount = 0;
                    for (int rowIndex = startRow; rowIndex < sheetData.Rows.Count; rowIndex++)
                    {
                        var row = sheetData.Rows[rowIndex];
                        if (columnIndex < row.Cells.Count && row.Cells[columnIndex].IsCheckbox)
                        {
                            dataRowCount++;
                        }
                        else
                        {
                            // Stop when we hit non-checkbox rows (summary rows, etc.)
                            break;
                        }
                    }

                    if (dataRowCount == 0)
                    {
                        _logger.LogInformation("🔲 No data rows with checkboxes found for column {Column}", columnIndex);
                        continue;
                    }

                    var endRow = startRow + dataRowCount - 1;

                    _logger.LogInformation("🔲 Applying checkbox validation to column {Column} rows {StartRow} to {EndRow} ({DataRowCount} data rows)",
                        columnIndex, startRow, endRow, dataRowCount);

                    var request = new Request
                    {
                        SetDataValidation = new SetDataValidationRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = startRow,
                                EndRowIndex = endRow + 1, // +1 because EndRowIndex is exclusive
                                StartColumnIndex = columnIndex,
                                EndColumnIndex = columnIndex + 1
                            },
                            Rule = dataValidationRule
                        }
                    };

                    requests.Add(request);
                }

                if (requests.Any())
                {
                    var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = requests
                    };

                    await service.Spreadsheets.BatchUpdate(batchUpdateRequest, spreadsheetId).ExecuteAsync();
                    _logger.LogInformation("✅ Successfully applied checkbox formatting to {Count} columns", checkboxColumns.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error applying checkbox formatting");
                // Don't fail the entire operation, just log the error
            }
        }
        /// <summary>
        /// Create spreadsheet using personal OAuth2 account for EVERYTHING
        /// No service account, no sharing complications!
        /// </summary>
        public async Task<(bool success, string? spreadsheetId, string? url, string? error)> CreateWithOAuth2OnlyAsync(
    string title,
    SheetData sheetData,
    string finalOwnerEmail,
    string? worksheetName = null)
        {
            DriveService? oauthDriveService = null;
            SheetsService? oauthSheetsService = null;
            string? spreadsheetId = null;

            try
            {
                _logger.LogInformation("🚀 Creating spreadsheet '{Title}' using OAuth2-only approach (SIMPLE!)", title);

                // 1. Check OAuth2 credentials file exists
                if (string.IsNullOrEmpty(_config.PersonalAccountOAuthPath) || !File.Exists(_config.PersonalAccountOAuthPath))
                {
                    return (false, null, null, "Personal account OAuth credentials not found. Please set PersonalAccountOAuthPath in config.");
                }

                // 2. Load OAuth2 client secrets (CORRECTED: Use GoogleClientSecrets, not GoogleCredential)
                GoogleClientSecrets clientSecrets;
                using (var stream = new FileStream(_config.PersonalAccountOAuthPath, FileMode.Open, FileAccess.Read))
                {
                    clientSecrets = GoogleClientSecrets.FromStream(stream);
                }

                _logger.LogInformation("✅ Loaded OAuth2 client secrets");

                // 3. Create UserCredential (handles OAuth2 flow - CORRECTED METHOD)
                var scopes = new[] { SheetsService.Scope.Spreadsheets, DriveService.Scope.Drive };

                UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    clientSecrets.Secrets,
                    scopes,
                    "NewwaysAdmin_User", // User identifier for token storage
                    CancellationToken.None,
                    new FileDataStore("NewwaysAdmin_OAuth2_Tokens", true)); // Stores refresh tokens locally

                _logger.LogInformation("✅ OAuth2 authentication successful!");

                // 4. Create Google API services with UserCredential
                oauthDriveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _config.ApplicationName,
                });

                oauthSheetsService = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _config.ApplicationName,
                });

                // 5. Create spreadsheet with OAuth2
                _logger.LogInformation("📄 Creating spreadsheet...");

                var spreadsheet = new Spreadsheet
                {
                    Properties = new SpreadsheetProperties
                    {
                        Title = title
                    }
                };

                var createRequest = oauthSheetsService.Spreadsheets.Create(spreadsheet);
                var response = await createRequest.ExecuteAsync();
                spreadsheetId = response.SpreadsheetId!;

                _logger.LogInformation("✅ Created spreadsheet: {SpreadsheetId}", spreadsheetId);

                // 6. Write data using SAME OAuth2 account (no permission issues!)
                _logger.LogInformation("📝 Writing data to spreadsheet...");

                worksheetName ??= "Sheet1";

                // Prepare the data
                var values = new List<IList<object>>();
                foreach (var row in sheetData.Rows)
                {
                    var rowValues = new List<object>();
                    foreach (var cell in row.Cells)
                    {
                        rowValues.Add(cell.Value ?? string.Empty);
                    }
                    values.Add(rowValues);
                }

                var range = $"{worksheetName}!A1";
                var valueRange = new ValueRange
                {
                    Values = values
                };

                var updateRequest = oauthSheetsService.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                await updateRequest.ExecuteAsync();

                _logger.LogInformation("✅ Successfully wrote {RowCount} rows to spreadsheet", values.Count);

                // 7. Transfer ownership to final user (if different from OAuth2 account)
                if (!string.IsNullOrEmpty(finalOwnerEmail) && finalOwnerEmail != "superfox75@gmail.com")
                {
                    _logger.LogInformation("🔄 Transferring ownership to {Email}...", finalOwnerEmail);

                    var permission = new Google.Apis.Drive.v3.Data.Permission()
                    {
                        Type = "user",
                        Role = "owner",
                        EmailAddress = finalOwnerEmail
                    };

                    var transferRequest = oauthDriveService.Permissions.Create(permission, spreadsheetId);
                    transferRequest.TransferOwnership = true;
                    transferRequest.SendNotificationEmail = true;
                    transferRequest.EmailMessage = "You are now the owner of this spreadsheet exported from NewwaysAdmin.";

                    try
                    {
                        await transferRequest.ExecuteAsync();
                        _logger.LogInformation("✅ Successfully transferred ownership to {Email}", finalOwnerEmail);
                    }
                    catch (Exception transferEx)
                    {
                        _logger.LogWarning(transferEx, "⚠️ Ownership transfer failed, but spreadsheet was created successfully");
                        // Don't fail the whole operation
                    }
                }
                else
                {
                    _logger.LogInformation("ℹ️ Keeping ownership with OAuth2 account (superfox75@gmail.com)");
                }

                var url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";
                _logger.LogInformation("🎉 SUCCESS! Spreadsheet ready: {Url}", url);

                return (true, spreadsheetId, url, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in OAuth2-only create operation");

                // Cleanup on failure
                if (!string.IsNullOrEmpty(spreadsheetId) && oauthDriveService != null)
                {
                    try
                    {
                        await oauthDriveService.Files.Delete(spreadsheetId).ExecuteAsync();
                        _logger.LogInformation("🧹 Cleaned up failed spreadsheet {SpreadsheetId}", spreadsheetId);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to cleanup spreadsheet {SpreadsheetId}", spreadsheetId);
                    }
                }

                return (false, null, null, ex.Message);
            }
            finally
            {
                oauthDriveService?.Dispose();
                oauthSheetsService?.Dispose();
            }
        }
        private string GetServiceAccountEmail()
        {
            try
            {
                // This should read from newwaysadmin-sheets-v2.json
                var credentialsJson = File.ReadAllText(_config.CredentialsPath);
                var credentialsData = System.Text.Json.JsonDocument.Parse(credentialsJson);
                var email = credentialsData.RootElement.GetProperty("client_email").GetString()!;

                _logger.LogInformation("🔍 NewwaysAdmin service account email: {Email}", email);
                // This should log: google-sheets-service-account2@newwaysadmin-intergration.iam.gserviceaccount.com
                return email;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to read service account email");
                throw;
            }
        }
        /// <summary>
        /// Transfer ownership of a spreadsheet to another user
        /// This removes the file from the service account's storage quota
        /// </summary>
        public async Task<bool> TransferOwnershipAsync(string spreadsheetId, string newOwnerEmail)
        {
            try
            {
                _logger.LogInformation("🔄 Transferring ownership of spreadsheet {SpreadsheetId} to {Email}",
                    spreadsheetId, newOwnerEmail);

                var driveService = await GetDriveServiceAsync();

                // Create ownership transfer permission
                var permission = new Google.Apis.Drive.v3.Data.Permission()
                {
                    Type = "user",
                    Role = "owner",  // MUST be "owner" for ownership transfer
                    EmailAddress = newOwnerEmail
                };

                var request = driveService.Permissions.Create(permission, spreadsheetId);

                // CRITICAL: Enable ownership transfer
                request.TransferOwnership = true;

                // Optional: Send notification email to new owner
                request.SendNotificationEmail = true;
                request.EmailMessage = "You are now the owner of this spreadsheet exported from NewwaysAdmin.";

                var response = await request.ExecuteAsync();

                _logger.LogInformation("✅ Successfully transferred ownership of spreadsheet {SpreadsheetId} to {Email}. " +
                                      "File is no longer using service account storage.",
                                      spreadsheetId, newOwnerEmail);

                return true;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "❌ Google API error transferring ownership of {SpreadsheetId} to {Email}: {Error}",
                    spreadsheetId, newOwnerEmail, ex.Message);

                // Special handling for common ownership transfer errors
                if (ex.Message.Contains("insufficientPermissions"))
                {
                    _logger.LogError("Service account lacks permission to transfer ownership. " +
                                   "The new owner email might need to be a Google account.");
                }
                else if (ex.Message.Contains("domainPolicy"))
                {
                    _logger.LogError("Domain policy prevents ownership transfer. " +
                                   "The receiving email domain might not allow external file ownership.");
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error transferring ownership of {SpreadsheetId} to {Email}",
                                spreadsheetId, newOwnerEmail);
                return false;
            }
        }

        /// <summary>
        /// Create spreadsheet, export data, and immediately transfer ownership
        /// This is the recommended approach for avoiding service account storage issues
        /// </summary>
        public async Task<(bool success, string? spreadsheetId, string? url, string? error)> CreateAndTransferSpreadsheetAsync(
            string title,
            SheetData sheetData,
            string newOwnerEmail,
            string? worksheetName = null)
        {
            string? spreadsheetId = null;

            try
            {
                _logger.LogInformation("Creating spreadsheet '{Title}' for transfer to {Email}", title, newOwnerEmail);

                // 1. Create the spreadsheet
                spreadsheetId = await CreateSpreadsheetAsync(title);

                // 2. Write the data
                var writeResult = await WriteDataToSheetAsync(spreadsheetId, sheetData, worksheetName);
                if (!writeResult.Success)
                {
                    return (false, null, null, $"Failed to write data: {string.Join(", ", writeResult.Errors)}");
                }

                // 3. Transfer ownership immediately
                var transferSuccess = await TransferOwnershipAsync(spreadsheetId, newOwnerEmail);
                if (!transferSuccess)
                {
                    _logger.LogWarning("Ownership transfer failed, but spreadsheet was created successfully. " +
                                     "File will remain on service account storage.");
                    // Don't fail the whole operation - user still gets a working spreadsheet
                }

                var url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";

                _logger.LogInformation("✅ Successfully created and {TransferStatus} spreadsheet {SpreadsheetId}",
                    transferSuccess ? "transferred ownership of" : "shared", spreadsheetId);

                return (true, spreadsheetId, url, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in create and transfer operation for {Email}", newOwnerEmail);

                // Clean up on failure
                if (!string.IsNullOrEmpty(spreadsheetId))
                {
                    try
                    {
                        var driveService = await GetDriveServiceAsync();
                        await driveService.Files.Delete(spreadsheetId).ExecuteAsync();
                        _logger.LogInformation("Cleaned up failed spreadsheet {SpreadsheetId}", spreadsheetId);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to cleanup spreadsheet {SpreadsheetId} after error", spreadsheetId);
                    }
                }

                return (false, null, null, ex.Message);
            }
        }

        public async Task<bool> ShareSpreadsheetAsync(string spreadsheetId, string email, string role = "writer")
        {
            try
            {
                return await ExecuteWithRetryAsync(async () =>
                {
                    _logger.LogInformation("🔄 Attempting to share spreadsheet {SpreadsheetId} with {Email} as {Role}",
                        spreadsheetId, email, role);

                    var driveService = await GetDriveServiceAsync();

                    // Create permission object
                    var permission = new Google.Apis.Drive.v3.Data.Permission()
                    {
                        Type = "user",
                        Role = role, // "reader", "writer", or "owner"
                        EmailAddress = email
                    };

                    var request = driveService.Permissions.Create(permission, spreadsheetId);

                    // IMPORTANT: Disable email notifications to avoid spam
                    request.SendNotificationEmail = false;

                    var response = await request.ExecuteAsync();

                    _logger.LogInformation("✅ Successfully shared spreadsheet {SpreadsheetId} with {Email}. Permission ID: {PermissionId}",
                        spreadsheetId, email, response.Id);

                    return true;

                }, $"Share spreadsheet {spreadsheetId} with {email}");
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "❌ Google API error sharing spreadsheet {SpreadsheetId} with {Email}: {Error} (Status: {StatusCode})",
                    spreadsheetId, email, ex.Message, ex.HttpStatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error sharing spreadsheet {SpreadsheetId} with {Email}", spreadsheetId, email);
                return false;
            }
        }

        private async Task<DriveService> GetDriveServiceAsync()
        {
            if (_driveService != null)
                return _driveService;

            try
            {
                if (!File.Exists(_config.CredentialsPath))
                {
                    throw new GoogleSheetsAuthenticationException($"Credentials file not found: {_config.CredentialsPath}");
                }

                GoogleCredential credential;
                using (var stream = new FileStream(_config.CredentialsPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream)
                        .CreateScoped(SheetsService.Scope.Spreadsheets, DriveService.Scope.Drive);
                }

                _driveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _config.ApplicationName,
                });

                _logger.LogInformation("Google Drive service initialized successfully");
                return _driveService;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Google Drive service");
                throw new GoogleSheetsAuthenticationException("Failed to initialize Google Drive service", ex);
            }
        }

        public async Task<string> CreateSpreadsheetAsync(string title)
        {
            return await ExecuteWithRetryAsync(async () =>
            {
                var service = GetSheetsService();

                var spreadsheet = new Spreadsheet
                {
                    Properties = new SpreadsheetProperties
                    {
                        Title = title
                    }
                };

                var request = service.Spreadsheets.Create(spreadsheet);
                var response = await request.ExecuteAsync();

                _logger.LogInformation("Created spreadsheet: {Title} with ID: {SpreadsheetId}", title, response.SpreadsheetId);
                return response.SpreadsheetId!;

            }, $"Create spreadsheet '{title}'");
        }

        public async Task<ExportResult> WriteDataToSheetAsync(string spreadsheetId, SheetData sheetData, string? worksheetName = null)
        {
            var result = new ExportResult();

            try
            {
                await ExecuteWithRetryAsync(async () =>
                {
                    var service = GetSheetsService();
                    worksheetName ??= "Sheet1";

                    // Prepare the data
                    var values = new List<IList<object>>();
                    foreach (var row in sheetData.Rows)
                    {
                        var rowValues = new List<object>();
                        foreach (var cell in row.Cells)
                        {
                            rowValues.Add(cell.Value ?? string.Empty);
                        }
                        values.Add(rowValues);
                    }

                    var range = $"{worksheetName}!A1";
                    var valueRange = new ValueRange
                    {
                        Values = values
                    };

                    var updateRequest = service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                    updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                    await updateRequest.ExecuteAsync();

                    // Apply formatting if needed (also with retry)
                   // await ApplyFormattingAsync(service, spreadsheetId, sheetData, worksheetName);

                    _logger.LogInformation("Successfully wrote {RowCount} rows to sheet {SpreadsheetId}", values.Count, spreadsheetId);

                }, $"Write data to spreadsheet {spreadsheetId}");

                result.Success = true;
                result.SheetId = spreadsheetId;
                result.SheetUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";
                result.RowsExported = sheetData.Rows.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write data to sheet {SpreadsheetId}", spreadsheetId);
                result.Success = false;
                result.Errors.Add($"Failed to write data: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Execute a Google API operation with retry logic for temporary failures
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName, int maxRetries = 3)
        {
            var retryDelays = new[] { 1000, 2000, 5000 }; // 1s, 2s, 5s

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Google.GoogleApiException ex) when (ShouldRetry(ex, attempt, maxRetries))
                {
                    var delay = attempt < retryDelays.Length ? retryDelays[attempt] : 5000;

                    _logger.LogWarning("🔄 Retry {Attempt}/{MaxRetries} for {Operation} after {StatusCode} error. Waiting {Delay}ms...",
                        attempt + 1, maxRetries + 1, operationName, ex.HttpStatusCode, delay);

                    await Task.Delay(delay);
                    continue;
                }
                catch (Google.GoogleApiException ex)
                {
                    _logger.LogError(ex, "❌ {Operation} failed with non-retryable error: {StatusCode} - {Message}",
                        operationName, ex.HttpStatusCode, ex.Message);
                    throw;
                }
            }

            throw new GoogleSheetsException($"Operation {operationName} failed after {maxRetries + 1} attempts");
        }

        /// <summary>
        /// Helper for void operations - use this pattern: await ExecuteRetryVoidAsync(() => SomeVoidMethod(), "description");
        /// </summary>
        private async Task ExecuteRetryVoidAsync(Func<Task> operation, string operationName, int maxRetries = 3)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true; // Dummy return value
            }, operationName, maxRetries);
        }

        /// <summary>
        /// Determine if an error should be retried
        /// </summary>
        private static bool ShouldRetry(Google.GoogleApiException ex, int currentAttempt, int maxRetries)
        {
            if (currentAttempt >= maxRetries) return false;

            // Retry on these HTTP status codes
            return ex.HttpStatusCode switch
            {
                System.Net.HttpStatusCode.BadGateway => true,          // 502
                System.Net.HttpStatusCode.ServiceUnavailable => true,  // 503
                System.Net.HttpStatusCode.GatewayTimeout => true,      // 504
                System.Net.HttpStatusCode.InternalServerError => true, // 500
                System.Net.HttpStatusCode.TooManyRequests => true,     // 429
                _ => false
            };
        }



        /// <summary>
        /// Execute a Google API operation with retry logic (void return)
        /// </summary>
        private async Task ExecuteWithRetryAsync(Func<Task> operation, string operationName, int maxRetries = 3)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true; // Dummy return for void operations
            }, operationName, maxRetries);
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    _sheetsService?.Dispose();
                    _driveService?.Dispose();
                }

                _disposed = true;
            }
        }
        // Replace your entire CreateWithProperOAuth2Async method in GoogleSheetsService.cs with this:

        public async Task<(bool success, string? spreadsheetId, string? url, string? error)> CreateWithProperOAuth2Async(
    string title,
    SheetData sheetData,
    string finalOwnerEmail,
    string? worksheetName = null)
        {
            DriveService? oauthDriveService = null;
            SheetsService? oauthSheetsService = null;
            string? spreadsheetId = null;

            try
            {
                _logger.LogInformation("🚀 Creating spreadsheet using OAuth2 and sharing with user");

                // 1. Check OAuth2 file exists
                if (string.IsNullOrEmpty(_config.PersonalAccountOAuthPath) || !File.Exists(_config.PersonalAccountOAuthPath))
                {
                    return (false, null, null, "OAuth2 credentials file not found");
                }

                // 2. Load OAuth2 client secrets
                GoogleClientSecrets clientSecrets;
                using (var stream = new FileStream(_config.PersonalAccountOAuthPath, FileMode.Open, FileAccess.Read))
                {
                    clientSecrets = GoogleClientSecrets.FromStream(stream);
                }

                _logger.LogInformation("✅ Loaded OAuth2 client secrets");

                // 3. Create UserCredential (handles OAuth2 flow)
                var scopes = new[] { SheetsService.Scope.Spreadsheets, DriveService.Scope.Drive };

                UserCredential credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    clientSecrets.Secrets,
                    scopes,
                    "NewwaysAdmin_User", // User identifier for token storage
                    CancellationToken.None,
                    new FileDataStore("NewwaysAdmin_OAuth2_Tokens", true)); // Stores refresh tokens locally

                _logger.LogInformation("✅ OAuth2 authentication successful!");

                // 4. Create Google API services
                oauthDriveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _config.ApplicationName,
                });

                oauthSheetsService = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = _config.ApplicationName,
                });

                // 5. Test the connection and show account info
                _logger.LogInformation("🔍 Testing connection...");
                var aboutRequest = oauthDriveService.About.Get();
                aboutRequest.Fields = "user,storageQuota";
                var about = await aboutRequest.ExecuteAsync();

                var storageGB = Math.Round((about.StorageQuota?.Limit ?? 0) / 1024.0 / 1024.0 / 1024.0, 2);
                var usedGB = Math.Round((about.StorageQuota?.Usage ?? 0) / 1024.0 / 1024.0 / 1024.0, 2);

                _logger.LogInformation("🔍 Connected as: {UserEmail}", about.User?.EmailAddress);
                _logger.LogInformation("💾 Storage: {UsedGB}GB / {TotalGB}GB", usedGB, storageGB);

                // 6. Create or get the NewwaysAdminSheets folder
                _logger.LogInformation("📁 Setting up NewwaysAdminSheets folder...");
                string? folderId = await GetOrCreateFolderAsync(oauthDriveService, "NewwaysAdminSheets");
                _logger.LogInformation("✅ Folder ready: {FolderId}", folderId);

                // 7. Create spreadsheet in the folder
                _logger.LogInformation("📄 Creating spreadsheet '{Title}' in folder...", title);

                var spreadsheet = new Spreadsheet
                {
                    Properties = new SpreadsheetProperties
                    {
                        Title = title
                    }
                };

                var createRequest = oauthSheetsService.Spreadsheets.Create(spreadsheet);
                var response = await createRequest.ExecuteAsync();
                spreadsheetId = response.SpreadsheetId!;

                _logger.LogInformation("✅ Created spreadsheet: {SpreadsheetId}", spreadsheetId);

                // 8. Move spreadsheet to the folder
                if (!string.IsNullOrEmpty(folderId))
                {
                    _logger.LogInformation("📁 Moving spreadsheet to NewwaysAdminSheets folder...");
                    try
                    {
                        var moveRequest = oauthDriveService.Files.Update(new Google.Apis.Drive.v3.Data.File(), spreadsheetId);
                        moveRequest.AddParents = folderId;
                        moveRequest.RemoveParents = "root"; // Remove from root folder
                        await moveRequest.ExecuteAsync();
                        _logger.LogInformation("✅ Moved spreadsheet to folder successfully");
                    }
                    catch (Exception moveEx)
                    {
                        _logger.LogWarning(moveEx, "⚠️ Failed to move to folder, but spreadsheet was created successfully");
                    }
                }

                // 9. Write data to spreadsheet
                _logger.LogInformation("📝 Writing data to spreadsheet...");

                worksheetName ??= "BankSlips";

                // First, rename the default "Sheet1" to our desired name
                _logger.LogInformation("📋 Renaming default sheet to '{WorksheetName}'...", worksheetName);

                var renameRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = new List<Request>
            {
                new Request
                {
                    UpdateSheetProperties = new UpdateSheetPropertiesRequest
                    {
                        Properties = new SheetProperties
                        {
                            SheetId = 0, // Default sheet ID is always 0
                            Title = worksheetName
                        },
                        Fields = "title"
                    }
                }
            }
                };

                await oauthSheetsService.Spreadsheets.BatchUpdate(renameRequest, spreadsheetId).ExecuteAsync();
                _logger.LogInformation("✅ Renamed sheet to '{WorksheetName}'", worksheetName);

                // 10. Now write the data first
                _logger.LogInformation("📝 Writing data to spreadsheet...");

                // Prepare the data - use actual boolean false for checkbox cells
                var values = new List<IList<object>>();
                foreach (var row in sheetData.Rows)
                {
                    var rowValues = new List<object>();
                    foreach (var cell in row.Cells)
                    {
                        // For checkbox cells, use actual boolean false (not string)
                        if (cell.IsCheckbox)
                        {
                            rowValues.Add(false); // Boolean false - should become unchecked checkbox with validation
                        }
                        else
                        {
                            rowValues.Add(cell.Value ?? string.Empty);
                        }
                    }
                    values.Add(rowValues);
                }

                // Handle worksheet names with spaces by wrapping in single quotes
                var range = worksheetName.Contains(' ') ? $"'{worksheetName}'!A1" : $"{worksheetName}!A1";
                var valueRange = new ValueRange
                {
                    Values = values
                };

                var updateRequest = oauthSheetsService.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                // Use USER_ENTERED to preserve formulas but also handle boolean values correctly
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                await updateRequest.ExecuteAsync();

                _logger.LogInformation("✅ Successfully wrote {RowCount} rows to spreadsheet", values.Count);

                // 11. THEN apply checkbox formatting to columns that need it
                _logger.LogInformation("🔲 Applying checkbox formatting to columns with data...");

                // Debug: Count how many checkbox cells we expect
                var totalCheckboxCells = 0;
                foreach (var row in sheetData.Rows)
                {
                    var checkboxCount = row.Cells.Count(c => c.IsCheckbox);
                    if (checkboxCount > 0)
                    {
                        _logger.LogInformation("🔲 Row has {Count} checkbox cells", checkboxCount);
                        totalCheckboxCells += checkboxCount;
                    }
                }
                _logger.LogInformation("🔲 Total checkbox cells expected: {Total}", totalCheckboxCells);

                await ApplyCheckboxFormattingAsync(oauthSheetsService, spreadsheetId, sheetData, worksheetName);

                // 12. Share with user instead of transferring ownership
                _logger.LogInformation("🔍 Checking sharing logic: finalOwnerEmail='{FinalOwner}', oauthEmail='{OAuthEmail}'",
                    finalOwnerEmail ?? "NULL", about.User?.EmailAddress ?? "NULL");

                if (!string.IsNullOrEmpty(finalOwnerEmail) &&
                    finalOwnerEmail != about.User?.EmailAddress)
                {
                    _logger.LogInformation("📤 Sharing spreadsheet with {Email} as editor...", finalOwnerEmail);

                    try
                    {
                        var permission = new Google.Apis.Drive.v3.Data.Permission()
                        {
                            Type = "user",
                            Role = "writer", // Give them edit access instead of ownership
                            EmailAddress = finalOwnerEmail
                        };

                        var shareRequest = oauthDriveService.Permissions.Create(permission, spreadsheetId);
                        shareRequest.SendNotificationEmail = true; // MUST be true to send email message
                        shareRequest.EmailMessage = "You now have edit access to this bank slip spreadsheet from NewwaysAdmin.";

                        var shareResponse = await shareRequest.ExecuteAsync();
                        _logger.LogInformation("✅ Successfully shared spreadsheet with {Email} as editor. Permission ID: {PermissionId}",
                            finalOwnerEmail, shareResponse.Id);

                        // Verify the share worked by listing permissions
                        try
                        {
                            var permissionsRequest = oauthDriveService.Permissions.List(spreadsheetId);
                            var permissions = await permissionsRequest.ExecuteAsync();
                            _logger.LogInformation("📋 Current permissions on spreadsheet:");
                            foreach (var perm in permissions.Permissions)
                            {
                                _logger.LogInformation("  - {Email} ({Role})", perm.EmailAddress ?? perm.Id, perm.Role);
                            }
                        }
                        catch (Exception listEx)
                        {
                            _logger.LogWarning(listEx, "Could not list permissions for verification");
                        }
                    }
                    catch (Exception shareEx)
                    {
                        _logger.LogError(shareEx, "❌ Failed to share with user {Email}: {Error}", finalOwnerEmail, shareEx.Message);
                        // Don't fail the entire operation if sharing fails
                    }
                }
                else
                {
                    _logger.LogInformation("ℹ️ No sharing needed. finalOwnerEmail is null, empty, or same as OAuth account");
                }

                var url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";
                _logger.LogInformation("🎉 SUCCESS! Spreadsheet ready: {Url}", url);

                return (true, spreadsheetId, url, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in OAuth2 create and share operation");

                // Cleanup on failure
                if (!string.IsNullOrEmpty(spreadsheetId) && oauthDriveService != null)
                {
                    try
                    {
                        await oauthDriveService.Files.Delete(spreadsheetId).ExecuteAsync();
                        _logger.LogInformation("🧹 Cleaned up failed spreadsheet {SpreadsheetId}", spreadsheetId);
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to cleanup spreadsheet {SpreadsheetId}", spreadsheetId);
                    }
                }

                return (false, null, null, ex.Message);
            }
            finally
            {
                oauthDriveService?.Dispose();
                oauthSheetsService?.Dispose();
            }
        }

        /// <summary>
        /// Get or create a folder in Google Drive
        /// </summary>
        private async Task<string?> GetOrCreateFolderAsync(DriveService driveService, string folderName)
        {
            try
            {
                // First, check if folder already exists
                var listRequest = driveService.Files.List();
                listRequest.Q = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
                listRequest.Fields = "files(id, name)";

                var listResponse = await listRequest.ExecuteAsync();

                if (listResponse.Files?.Any() == true)
                {
                    var existingFolder = listResponse.Files.First();
                    _logger.LogInformation("📁 Found existing folder '{FolderName}': {FolderId}", folderName, existingFolder.Id);
                    return existingFolder.Id;
                }

                // Create new folder
                _logger.LogInformation("📁 Creating new folder '{FolderName}'...", folderName);

                var folderMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = folderName,
                    MimeType = "application/vnd.google-apps.folder"
                };

                var createRequest = driveService.Files.Create(folderMetadata);
                createRequest.Fields = "id, name";

                var folder = await createRequest.ExecuteAsync();

                _logger.LogInformation("✅ Created folder '{FolderName}': {FolderId}", folderName, folder.Id);
                return folder.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating/finding folder '{FolderName}'", folderName);
                return null; // Return null so spreadsheet still gets created in root if folder creation fails
            }
        }

        /// <summary>
        /// Convert column index to Excel-style letter (0=A, 1=B, 25=Z, 26=AA, etc.)
        /// </summary>
        private string GetColumnLetter(int columnIndex)
        {
            string columnLetter = "";
            while (columnIndex >= 0)
            {
                columnLetter = (char)('A' + (columnIndex % 26)) + columnLetter;
                columnIndex = (columnIndex / 26) - 1;
            }
            return columnLetter;
        }
    }
}
        