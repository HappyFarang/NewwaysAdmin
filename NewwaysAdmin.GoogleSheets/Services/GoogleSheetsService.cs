// NewwaysAdmin.GoogleSheets/Services/GoogleSheetsService.cs
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
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

        private async Task<SheetsService> GetSheetsServiceAsync()
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
            catch (Exception ex) when (!(ex is GoogleSheetsException))
            {
                _logger.LogError(ex, "Failed to initialize Google Sheets service");
                throw new GoogleSheetsAuthenticationException("Failed to initialize Google Sheets service", ex);
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
            try
            {
                var service = await GetSheetsServiceAsync();

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
            }
            catch (Exception ex) when (!(ex is GoogleSheetsException))
            {
                _logger.LogError(ex, "Failed to create spreadsheet: {Title}", title);
                throw new GoogleSheetsException($"Failed to create spreadsheet: {title}", ex);
            }
        }

        public async Task<ExportResult> WriteDataToSheetAsync(string spreadsheetId, SheetData sheetData, string? worksheetName = null)
        {
            var result = new ExportResult();

            try
            {
                var service = await GetSheetsServiceAsync();
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
                await ApplyCheckboxFormattingAsync(service, spreadsheetId, sheetData, worksheetName);

                var updateRequest = service.Spreadsheets.Values.Update(valueRange, spreadsheetId, range);
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;

                var updateResponse = await updateRequest.ExecuteAsync();

                // Apply formatting if needed
                await ApplyFormattingAsync(service, spreadsheetId, sheetData, worksheetName);

                result.Success = true;
                result.SheetId = spreadsheetId;
                result.SheetUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}";
                result.RowsExported = values.Count;

                _logger.LogInformation("Successfully wrote {RowCount} rows to sheet {SpreadsheetId}", values.Count, spreadsheetId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write data to sheet {SpreadsheetId}", spreadsheetId);
                result.Success = false;
                result.Errors.Add($"Failed to write data: {ex.Message}");
            }

            return result;
        }
        private async Task ApplyCheckboxFormattingAsync(SheetsService service, string spreadsheetId, SheetData sheetData, string worksheetName)
        {
            var requests = new List<Request>();

            for (int rowIndex = 0; rowIndex < sheetData.Rows.Count; rowIndex++)
            {
                var row = sheetData.Rows[rowIndex];
                for (int colIndex = 0; colIndex < row.Cells.Count; colIndex++)
                {
                    var cell = row.Cells[colIndex];
                    if (cell.IsCheckbox)
                    {
                        // Add data validation for checkbox
                        var request = new Request
                        {
                            SetDataValidation = new SetDataValidationRequest
                            {
                                Range = new GridRange
                                {
                                    SheetId = 0, // Assuming first sheet
                                    StartRowIndex = rowIndex,
                                    EndRowIndex = rowIndex + 1,
                                    StartColumnIndex = colIndex,
                                    EndColumnIndex = colIndex + 1
                                },
                                Rule = new DataValidationRule
                                {
                                    Condition = new BooleanCondition
                                    {
                                        Type = "BOOLEAN"
                                    },
                                    ShowCustomUi = true
                                }
                            }
                        };
                        requests.Add(request);
                    }
                }
            }

            if (requests.Any())
            {
                var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
                {
                    Requests = requests
                };

                await service.Spreadsheets.BatchUpdate(batchUpdateRequest, spreadsheetId).ExecuteAsync();
            }
        }
        private async Task ApplyFormattingAsync(SheetsService service, string spreadsheetId, SheetData sheetData, string worksheetName)
        {
            try
            {
                var requests = new List<Request>();

                // Get sheet ID for the worksheet
                var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
                var sheet = spreadsheet.Sheets?.FirstOrDefault(s => s.Properties?.Title == worksheetName);
                if (sheet?.Properties?.SheetId == null)
                {
                    _logger.LogWarning("Could not find sheet {WorksheetName} for formatting", worksheetName);
                    return;
                }

                var sheetId = sheet.Properties.SheetId.Value;

                // Format header row if it exists
                var headerRow = sheetData.Rows.FirstOrDefault(r => r.IsHeader);
                if (headerRow != null)
                {
                    var headerIndex = sheetData.Rows.IndexOf(headerRow);
                    requests.Add(new Request
                    {
                        RepeatCell = new RepeatCellRequest
                        {
                            Range = new GridRange
                            {
                                SheetId = sheetId,
                                StartRowIndex = headerIndex,
                                EndRowIndex = headerIndex + 1,
                                StartColumnIndex = 0,
                                EndColumnIndex = headerRow.Cells.Count
                            },
                            Cell = new CellData
                            {
                                UserEnteredFormat = new CellFormat
                                {
                                    BackgroundColor = new Color { Red = 0.9f, Green = 0.9f, Blue = 0.9f, Alpha = 1.0f },
                                    TextFormat = new TextFormat { Bold = true }
                                }
                            },
                            Fields = "userEnteredFormat(backgroundColor,textFormat)"
                        }
                    });
                }

                // Auto-resize columns
                requests.Add(new Request
                {
                    AutoResizeDimensions = new AutoResizeDimensionsRequest
                    {
                        Dimensions = new DimensionRange
                        {
                            SheetId = sheetId,
                            Dimension = "COLUMNS",
                            StartIndex = 0,
                            EndIndex = sheetData.Rows.FirstOrDefault()?.Cells.Count ?? 10
                        }
                    }
                });

                if (requests.Any())
                {
                    var batchUpdateRequest = new BatchUpdateSpreadsheetRequest
                    {
                        Requests = requests
                    };

                    await service.Spreadsheets.BatchUpdate(batchUpdateRequest, spreadsheetId).ExecuteAsync();
                    _logger.LogDebug("Applied formatting to sheet {SpreadsheetId}", spreadsheetId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply formatting to sheet {SpreadsheetId}", spreadsheetId);
                // Don't throw - formatting is not critical
            }
        }

        public async Task<bool> ShareSpreadsheetAsync(string spreadsheetId, string email, string role = "writer")
        {
            try
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

                // IMPORTANT: Transfer ownership to the user if they're the main user
                request.TransferOwnership = false; // Keep service account as owner for control

                var response = await request.ExecuteAsync();

                _logger.LogInformation("✅ Successfully shared spreadsheet {SpreadsheetId} with {Email}. Permission ID: {PermissionId}",
                    spreadsheetId, email, response.Id);

                return true;
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "❌ Google API error sharing spreadsheet {SpreadsheetId} with {Email}: {Error} (Status: {StatusCode})",
                    spreadsheetId, email, ex.Message, ex.HttpStatusCode);

                // Log more details for debugging
                if (ex.Error?.Errors != null)
                {
                    foreach (var error in ex.Error.Errors)
                    {
                        _logger.LogError("API Error Detail - Domain: {Domain}, Reason: {Reason}, Message: {Message}",
                            error.Domain, error.Reason, error.Message);
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Unexpected error sharing spreadsheet {SpreadsheetId} with {Email}", spreadsheetId, email);
                return false;
            }
        }

        /// <summary>
        /// Share spreadsheet with multiple users at once
        /// </summary>
        public async Task<Dictionary<string, bool>> ShareSpreadsheetWithMultipleUsersAsync(
            string spreadsheetId,
            IEnumerable<string> emails,
            string role = "writer")
        {
            var results = new Dictionary<string, bool>();

            foreach (var email in emails.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                try
                {
                    var success = await ShareSpreadsheetAsync(spreadsheetId, email.Trim(), role);
                    results[email] = success;

                    // Small delay to avoid rate limiting
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sharing with {Email}", email);
                    results[email] = false;
                }
            }

            return results;
        }

        public async Task<SheetInfo?> GetSheetInfoAsync(string spreadsheetId)
        {
            try
            {
                var service = await GetSheetsServiceAsync();
                var spreadsheet = await service.Spreadsheets.Get(spreadsheetId).ExecuteAsync();

                if (spreadsheet?.Properties == null)
                    return null;

                return new SheetInfo
                {
                    SheetId = spreadsheetId,
                    Title = spreadsheet.Properties.Title ?? "Untitled",
                    Url = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}",
                    CreatedAt = DateTime.UtcNow, // Google Sheets API doesn't provide creation time easily
                    RowCount = spreadsheet.Sheets?.FirstOrDefault()?.Properties?.GridProperties?.RowCount ?? 0,
                    ColumnCount = spreadsheet.Sheets?.FirstOrDefault()?.Properties?.GridProperties?.ColumnCount ?? 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sheet info for {SpreadsheetId}", spreadsheetId);
                return null;
            }
        }

        /// <summary>
        /// Test the complete workflow: create, write, and share
        /// </summary>
        public async Task<(bool success, string? spreadsheetId, List<string> errors)> TestCompleteWorkflowAsync(
            string testTitle,
            IEnumerable<string> emailsToShare)
        {
            var errors = new List<string>();
            string? spreadsheetId = null;

            try
            {
                // 1. Create spreadsheet
                spreadsheetId = await CreateSpreadsheetAsync(testTitle);

                // 2. Add some test data
                var testData = new SheetData
                {
                    Title = testTitle
                };
                testData.AddHeaderRow("Test Column 1", "Test Column 2", "Test Column 3");
                testData.AddDataRow("Test Value 1", "Test Value 2", "Test Value 3");

                var writeResult = await WriteDataToSheetAsync(spreadsheetId, testData);
                if (!writeResult.Success)
                {
                    errors.AddRange(writeResult.Errors);
                    return (false, spreadsheetId, errors);
                }

                // 3. Share with all emails
                var shareResults = await ShareSpreadsheetWithMultipleUsersAsync(spreadsheetId, emailsToShare);
                foreach (var shareResult in shareResults)
                {
                    if (!shareResult.Value)
                    {
                        errors.Add($"Failed to share with {shareResult.Key}");
                    }
                }

                return (true, spreadsheetId, errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test workflow");
                errors.Add($"Test workflow failed: {ex.Message}");
                return (false, spreadsheetId, errors);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _sheetsService?.Dispose();
                _driveService?.Dispose();
                _disposed = true;
            }
        }
    }
}