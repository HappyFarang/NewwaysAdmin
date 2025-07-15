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
        private async Task ApplyCheckboxFormattingAsync(SheetsService service, string spreadsheetId, SheetData sheetData, string worksheetName)
        {
            try
            {
                _logger.LogInformation("🔄 Applying checkbox formatting...");

                // Find columns that need checkbox formatting
                var checkboxColumns = new List<int>();
                if (sheetData.Rows.Any())
                {
                    var firstDataRow = sheetData.Rows.FirstOrDefault(r => !r.IsHeader);
                    if (firstDataRow != null)
                    {
                        for (int i = 0; i < firstDataRow.Cells.Count; i++)
                        {
                            if (firstDataRow.Cells[i].IsCheckbox)
                            {
                                checkboxColumns.Add(i);
                            }
                        }
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

                    // Apply to the entire column (excluding header if present)
                    var startRow = sheetData.Rows.Any(r => r.IsHeader) ? 1 : 0; // Skip header row
                    var endRow = totalRows - 1;

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

                // 1. Check OAuth2 credentials
                if (string.IsNullOrEmpty(_config.PersonalAccountOAuthPath) || !File.Exists(_config.PersonalAccountOAuthPath))
                {
                    return (false, null, null, "Personal account OAuth credentials not found. Please set PersonalAccountOAuthPath in config.");
                }

                // 2. Create OAuth2 services
                GoogleCredential oauthCredential;
                using (var stream = new FileStream(_config.PersonalAccountOAuthPath, FileMode.Open, FileAccess.Read))
                {
                    oauthCredential = GoogleCredential.FromStream(stream)
                        .CreateScoped(SheetsService.Scope.Spreadsheets, DriveService.Scope.Drive);
                }

                oauthDriveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = oauthCredential,
                    ApplicationName = _config.ApplicationName,
                });

                oauthSheetsService = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = oauthCredential,
                    ApplicationName = _config.ApplicationName,
                });

                // 3. Create spreadsheet with OAuth2
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

                // 4. Write data using SAME OAuth2 account (no permission issues!)
                _logger.LogInformation("📝 Writing data to spreadsheet...");

                worksheetName ??= "Bank Slips";

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

                // 5. Transfer ownership to final user (if different from OAuth2 account)
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

                // 6. Create spreadsheet
                _logger.LogInformation("📄 Creating spreadsheet '{Title}'...", title);

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

                // 7. Write data to spreadsheet
                _logger.LogInformation("📝 Writing data to spreadsheet...");

                worksheetName ??= "Bank Slips";

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

                // 8. Share with user instead of transferring ownership
                if (!string.IsNullOrEmpty(finalOwnerEmail) &&
                    finalOwnerEmail != about.User?.EmailAddress &&
                    finalOwnerEmail != "superfox75@gmail.com")
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
                        shareRequest.SendNotificationEmail = true;
                        shareRequest.EmailMessage = "Bank slip export from NewwaysAdmin - you now have edit access to this spreadsheet.";

                        await shareRequest.ExecuteAsync();
                        _logger.LogInformation("✅ Successfully shared spreadsheet with {Email}", finalOwnerEmail);
                    }
                    catch (Exception shareEx)
                    {
                        _logger.LogWarning(shareEx, "⚠️ Failed to share with user, but spreadsheet was created successfully");
                        // Don't fail the entire operation if sharing fails
                    }
                }
                else
                {
                    _logger.LogInformation("ℹ️ Keeping ownership with OAuth account: {Email}", about.User?.EmailAddress);
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
        