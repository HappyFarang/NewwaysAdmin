﻿// Updated BankSlipOcrService.cs with enhanced note detection and image processing
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.Auth;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class BankSlipOcrService : IBankSlipOcrService
    {
        private readonly ILogger<BankSlipOcrService> _logger;
        private readonly IOManager _ioManager;
        private readonly IAuthenticationService _authService;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<List<SlipCollection>>? _collectionsStorage;
        private IDataStorage<List<BankSlipData>>? _slipsStorage;
        private ImageAnnotatorClient? _visionClient;

        private static readonly string[] ThaiMonths = {
            "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
            "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."
        };

        public BankSlipOcrService(ILogger<BankSlipOcrService> logger, IOManager ioManager, IAuthenticationService authService)
        {
            _logger = logger;
            _ioManager = ioManager;
            _authService = authService;
        }

        private async Task EnsureStorageInitializedAsync()
        {
            if (_collectionsStorage != null && _slipsStorage != null) return;

            await _initLock.WaitAsync();
            try
            {
                _collectionsStorage ??= await _ioManager.GetStorageAsync<List<SlipCollection>>("BankSlip_Collections");
                _slipsStorage ??= await _ioManager.GetStorageAsync<List<BankSlipData>>("BankSlip_Data");
                _logger.LogInformation("Storage initialized successfully");
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<List<SlipCollection>> GetUserCollectionsAsync(string username)
        {
            await EnsureStorageInitializedAsync();

            // Load all collections from admin storage
            var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();

            // Check if user is admin - if so, return all active collections
            var user = await _authService.GetUserByNameAsync(username);

            if (user?.IsAdmin == true)
            {
                _logger.LogInformation("Admin user {Username} accessing all {Count} collections", username, allCollections.Count);
                return allCollections.Where(c => c.IsActive).ToList();
            }

            // For non-admin users, filter by accessible collections
            var accessibleCollectionIds = new List<string>();

            if (user?.ModuleConfigs.TryGetValue("accounting.bankslips", out var moduleConfig) == true)
            {
                accessibleCollectionIds = moduleConfig.AccessibleCollectionIds ?? new List<string>();
            }

            var accessibleCollections = allCollections
                .Where(c => c.IsActive && accessibleCollectionIds.Contains(c.Id))
                .ToList();

            _logger.LogInformation("User {Username} has access to {Count} of {Total} collections",
                username, accessibleCollections.Count, allCollections.Count);

            return accessibleCollections;
        }



        public async Task<SlipCollection?> GetCollectionAsync(string collectionId, string username)
        {
            var userCollections = await GetUserCollectionsAsync(username);
            return userCollections.FirstOrDefault(c => c.Id == collectionId);
        }



        public async Task SaveCollectionAsync(SlipCollection collection, string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();
                _logger.LogInformation("Saving collection {CollectionName} by user {Username}",
                    collection.Name, username);

                // All collections are saved under "admin" storage
                var collections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
                var existingIndex = collections.FindIndex(c => c.Id == collection.Id);

                if (existingIndex >= 0)
                {
                    collections[existingIndex] = collection;
                    _logger.LogInformation("Updated existing collection");
                }
                else
                {
                    if (string.IsNullOrEmpty(collection.CreatedBy))
                        collection.CreatedBy = username;
                    if (collection.CreatedAt == default)
                        collection.CreatedAt = DateTime.UtcNow;
                    collections.Add(collection);
                    _logger.LogInformation("Added new collection");
                }

                await _collectionsStorage.SaveAsync("admin", collections);
                _logger.LogInformation("Collection saved successfully to admin storage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving collection");
                throw;
            }
        }


        public async Task DeleteCollectionAsync(string collectionId, string username)
        {
            await EnsureStorageInitializedAsync();

            // Load from admin storage
            var collections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
            var collection = collections.FirstOrDefault(c => c.Id == collectionId);

            if (collection != null)
            {
                collection.IsActive = false;
                await _collectionsStorage.SaveAsync("admin", collections);
                _logger.LogInformation("Collection {CollectionId} marked as inactive by {Username}", collectionId, username);
            }
        }
        public async Task<List<SlipCollection>> GetAllCollectionsAsync()
        {
            await EnsureStorageInitializedAsync();
            var collections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
            return collections.Where(c => c.IsActive).ToList();
        }

        public async Task<BankSlipProcessingResult> ProcessSlipCollectionAsync(
            string collectionId,
            DateTime startDate,
            DateTime endDate,
            string username,
            IProgressReporter? progressReporter = null)
        {
            var collection = await GetCollectionAsync(collectionId, username);
            if (collection == null)
            {
                throw new ArgumentException($"Collection not found: {collectionId}");
            }

            _logger.LogInformation("Starting enhanced bank slip processing for collection {CollectionName} from {StartDate} to {EndDate}",
                collection.Name, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            var result = new BankSlipProcessingResult
            {
                ProcessingStarted = DateTime.UtcNow
            };

            try
            {
                // Validate directories first
                if (!Directory.Exists(collection.SourceDirectory))
                {
                    throw new DirectoryNotFoundException($"Source directory not found: {collection.SourceDirectory}");
                }

                // Ensure output directory exists
                Directory.CreateDirectory(collection.OutputDirectory);

                // Clean up any old temp files first
                EnhancedImageProcessor.CleanupTempFiles(collection.OutputDirectory, _logger);

                // Initialize Vision API client
                _visionClient = CreateVisionClient(collection.CredentialsPath);
                _logger.LogInformation("Vision API client initialized");

                // Get all image files
                var allFiles = GetImageFiles(collection.SourceDirectory);
                _logger.LogInformation("Found {TotalFileCount} total image files in source directory", allFiles.Count);

                if (allFiles.Count == 0)
                {
                    _logger.LogWarning("No image files found in {Directory}", collection.SourceDirectory);
                    result.ProcessingCompleted = DateTime.UtcNow;
                    result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;
                    return result;
                }

                // Pre-filter files by file timestamp
                var endDateInclusive = endDate.AddDays(1);
                var candidateFiles = allFiles.Where(file =>
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var fileDate = fileInfo.LastWriteTime.Date;
                        return fileDate >= startDate.Date && fileDate < endDateInclusive.Date;
                    }
                    catch
                    {
                        return true;
                    }
                }).ToList();

                _logger.LogInformation("Pre-filtered to {CandidateCount} files based on file timestamps",
                    candidateFiles.Count);

                result.Summary.TotalFiles = candidateFiles.Count;
                progressReporter?.ReportProgress(0, candidateFiles.Count);

                // Process each candidate file
                for (int i = 0; i < candidateFiles.Count; i++)
                {
                    var file = candidateFiles[i];
                    var fileName = Path.GetFileName(file);

                    try
                    {
                        _logger.LogDebug("Processing file {FileIndex}/{TotalFiles}: {FileName}",
                            i + 1, candidateFiles.Count, fileName);

                        progressReporter?.ReportProgress(i, candidateFiles.Count, fileName);

                        var slipData = await ProcessSingleFileAsync(file, collection, startDate, endDate);
                        if (slipData != null)
                        {
                            slipData.ProcessedBy = username;
                            slipData.ProcessedAt = DateTime.UtcNow;
                            slipData.SlipCollectionName = collection.Name;
                            result.ProcessedSlips.Add(slipData);
                            result.Summary.ProcessedFiles++;

                            var ceDate = slipData.TransactionDate.AddYears(-543);
                            _logger.LogDebug("Successfully processed {FileName}, Amount: {Amount}, Date: {Date}, Note: '{Note}'",
                                fileName, slipData.Amount, ceDate.ToString("yyyy-MM-dd"), slipData.Note ?? "No note");
                        }
                        else
                        {
                            result.Summary.FailedFiles++;
                            _logger.LogDebug("Failed to process or date out of range: {FileName}", fileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing file: {FilePath}", file);
                        result.Errors.Add(new ProcessingError
                        {
                            FilePath = file,
                            Reason = ex.Message,
                            ErrorTime = DateTime.UtcNow
                        });
                        result.Summary.FailedFiles++;
                    }

                    await Task.Delay(50);
                }

                result.Summary.DateOutOfRangeFiles = candidateFiles.Count - result.Summary.ProcessedFiles - result.Summary.FailedFiles;
                result.Summary.TotalFiles = allFiles.Count;

                progressReporter?.ReportProgress(candidateFiles.Count, candidateFiles.Count, "Processing complete");

                if (result.ProcessedSlips.Any())
                {
                    try
                    {
                        await SaveProcessedSlipsAsync(result.ProcessedSlips, username);
                        _logger.LogInformation("Saved {Count} processed slips", result.ProcessedSlips.Count);
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogError(saveEx, "Failed to save processed slips, but processing completed successfully");
                    }
                }

                // Clean up temp files after processing
                EnhancedImageProcessor.CleanupTempFiles(collection.OutputDirectory, _logger);

                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;

                _logger.LogInformation("Enhanced processing completed. Success: {Success}, Failed: {Failed}, Out of range: {OutOfRange}",
                    result.Summary.ProcessedFiles, result.Summary.FailedFiles, result.Summary.DateOutOfRangeFiles);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during enhanced batch processing");
                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;
                throw;
            }
        }

        public async Task<BankSlipData?> TestProcessSingleFileAsync(string filePath, SlipCollection collection)
        {
            try
            {
                // Clean up any old temp files first
                EnhancedImageProcessor.CleanupTempFiles(collection.OutputDirectory, _logger);

                _visionClient = CreateVisionClient(collection.CredentialsPath);
                var result = await ProcessSingleFileAsync(filePath, collection, DateTime.MinValue, DateTime.MaxValue);

                // Clean up after test
                EnhancedImageProcessor.CleanupTempFiles(collection.OutputDirectory, _logger);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in enhanced test processing: {FilePath}", filePath);
                throw;
            }
        }

        private async Task<BankSlipData?> ProcessSingleFileAsync(
            string filePath,
            SlipCollection collection,
            DateTime startDate,
            DateTime endDate)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found: {FilePath}", filePath);
                return null;
            }

            var tempProcessedPath = Path.Combine(collection.OutputDirectory, $"enhanced_processed_{Guid.NewGuid():N}.png");

            try
            {
                // Try different processing passes with enhanced image processing
                foreach (var pass in collection.ProcessingSettings.ProcessingPasses)
                {
                    try
                    {
                        _logger.LogDebug("Trying enhanced processing pass {Pass} for {FilePath}", pass, Path.GetFileName(filePath));

                        // Get processing settings for this pass
                        var settings = EnhancedImageProcessor.GetSettingsForPass(pass);

                        // Apply enhanced image processing
                        EnhancedImageProcessor.ProcessImage(filePath, tempProcessedPath, settings, _logger);

                        var slipData = await ExtractSlipDataAsync(tempProcessedPath);
                        if (slipData != null)
                        {
                            slipData.OriginalFilePath = filePath;

                            if (!ValidateAndFixDate(slipData, filePath))
                            {
                                _logger.LogDebug("Date validation failed for {FilePath}", filePath);
                                continue;
                            }

                            var ceDate = slipData.TransactionDate.AddYears(-543);

                            if (startDate != DateTime.MinValue && endDate != DateTime.MaxValue)
                            {
                                if (ceDate.Date >= startDate.Date && ceDate.Date <= endDate.Date)
                                {
                                    slipData.Status = BankSlipProcessingStatus.Completed;
                                    _logger.LogDebug("Successfully processed {FilePath} with enhanced pass {Pass}, Date: {Date}, Note: '{Note}'",
                                        Path.GetFileName(filePath), pass, ceDate.ToString("yyyy-MM-dd"), slipData.Note ?? "No note");
                                    return slipData;
                                }
                                else
                                {
                                    _logger.LogDebug("Date {Date} outside range for {FilePath}",
                                        ceDate.ToString("yyyy-MM-dd"), Path.GetFileName(filePath));
                                    return null;
                                }
                            }
                            else
                            {
                                slipData.Status = BankSlipProcessingStatus.Completed;
                                return slipData;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Enhanced processing pass {Pass} failed for {FilePath}: {Error}",
                            pass, Path.GetFileName(filePath), ex.Message);
                        continue;
                    }
                }

                _logger.LogDebug("All enhanced processing passes failed for {FilePath}", Path.GetFileName(filePath));
                return null;
            }
            finally
            {
                // Clean up temp file
                try
                {
                    if (File.Exists(tempProcessedPath))
                    {
                        File.Delete(tempProcessedPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Failed to delete temp file {TempPath}: {Error}", tempProcessedPath, ex.Message);
                }
            }
        }

        private bool ValidateAndFixDate(BankSlipData slipData, string filePath)
        {
            try
            {
                if (slipData.TransactionDate == default ||
                    slipData.TransactionDate.Year < 2560 ||
                    slipData.TransactionDate.Year > 2570)
                {
                    var fileInfo = new FileInfo(filePath);
                    slipData.TransactionDate = fileInfo.LastWriteTime.AddYears(543);
                    _logger.LogDebug("Using file timestamp for date: {Date}", slipData.TransactionDate);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating date for {FilePath}", filePath);
                return false;
            }
        }

        private async Task<BankSlipData?> ExtractSlipDataAsync(string imagePath)
        {
            try
            {
                var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);
                var response = await _visionClient!.DetectTextAsync(image);

                if (!response.Any())
                {
                    _logger.LogDebug("OCR returned no text for {ImagePath}", Path.GetFileName(imagePath));
                    return null;
                }

                var text = string.Join("\n", response.Select(r => r.Description));
                _logger.LogDebug("Enhanced OCR extracted {CharCount} characters from {ImagePath}",
                    text.Length, Path.GetFileName(imagePath));

                return ParseSlipText(text, imagePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during enhanced OCR extraction for {ImagePath}", imagePath);
                return null;
            }
        }

        private BankSlipData? ParseSlipText(string text, string imagePath)
        {
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            _logger.LogDebug("Enhanced OCR Output for {FileName} - Total lines: {LineCount}",
                Path.GetFileName(imagePath), lines.Count);

            // Log lines for debugging
            foreach (var line in lines.Take(Math.Min(lines.Count, 25)))
            {
                _logger.LogDebug("Enhanced OCR Line: '{Line}'", line);
            }

            var slip = new BankSlipData
            {
                OriginalFilePath = imagePath
            };

            var accountNumberPattern = new Regex(@"[xX]{3,}[-\d]+|[\d]+-[xX]{3,}|\d{3}-\d-\d{5}-\d");
            var amountPattern = new Regex(@"[\d,]+\.?\d*\s*บาท");

            bool foundSenderAccount = false;
            bool foundAmount = false;
            bool foundPromptPaySection = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var nextLine = i + 1 < lines.Count ? lines[i + 1] : "";

                // Skip lines that are clearly system/UI elements
                if (ShouldSkipLine(line))
                {
                    _logger.LogDebug("Skipping system line: '{Line}'", line);
                    continue;
                }

                // Date parsing
                if (IsDateLine(line) && slip.TransactionDate == default)
                {
                    try
                    {
                        slip.TransactionDate = ParseThaiDate(line);
                        _logger.LogDebug("Found date: {Date}", slip.TransactionDate);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Failed to parse date from '{Line}': {Error}", line, ex.Message);
                    }
                }

                // Check for PromptPay section indicator (keep for logging purposes)
                else if (line.Contains("พร้อมเพย์") || line.Contains("Prompt") || line.Contains("PromptPay"))
                {
                    foundPromptPaySection = true;
                    _logger.LogDebug("Found PromptPay section at line {Index}", i);
                }

                // Account number detection - sender account first
                else if (!foundSenderAccount && accountNumberPattern.IsMatch(line))
                {
                    var cleanLine = CleanAccountNumber(line);
                    slip.AccountNumber = cleanLine;
                    foundSenderAccount = true;
                    _logger.LogDebug("Found sender account: {Account}", slip.AccountNumber);

                    // Look for sender name in nearby lines
                    if (string.IsNullOrEmpty(slip.AccountName))
                    {
                        slip.AccountName = FindSenderNameNearAccount(lines, i);
                        if (!string.IsNullOrEmpty(slip.AccountName))
                        {
                            _logger.LogDebug("Found sender name: {Name}", slip.AccountName);
                        }
                    }
                }

                // Second account number (receiver account) - accept both traditional and e-wallet formats
                else if (foundSenderAccount && (accountNumberPattern.IsMatch(line) || CouldBeEWalletId(line)))
                {
                    var cleanLine = CleanAccountNumber(line);
                    if (cleanLine != slip.AccountNumber) // Make sure it's different from sender
                    {
                        slip.ReceiverAccount = cleanLine;
                        _logger.LogDebug("Found receiver account (traditional or e-wallet): {Account}", slip.ReceiverAccount);

                        // Look for receiver name near this account
                        if (string.IsNullOrEmpty(slip.ReceiverName))
                        {
                            slip.ReceiverName = FindReceiverNameNearAccount(lines, i, slip.AccountName);
                            if (!string.IsNullOrEmpty(slip.ReceiverName))
                            {
                                _logger.LogDebug("Found receiver name near account: {Name}", slip.ReceiverName);
                            }
                        }
                    }
                }

                // Amount detection
                else if (!foundAmount && amountPattern.IsMatch(line))
                {
                    var amountStr = ExtractAmount(line);
                    if (decimal.TryParse(amountStr, out decimal amount) && amount > 0)
                    {
                        slip.Amount = amount;
                        foundAmount = true;
                        _logger.LogDebug("Found amount: {Amount}", slip.Amount);
                    }
                }

                // Sender name detection (if not found yet)
                else if (string.IsNullOrEmpty(slip.AccountName) && IsValidPersonOrCompanyName(line, ""))
                {
                    slip.AccountName = line;
                    _logger.LogDebug("Found sender name: {Name}", slip.AccountName);
                }

                // FIXED: Enhanced receiver name detection - TRY EVERYWHERE, not just in PromptPay section
                else if (string.IsNullOrEmpty(slip.ReceiverName))
                {
                    if (IsValidReceiverName(line, slip.AccountName))
                    {
                        var receiverName = line;

                        // Try to combine with next line if it looks like continuation
                        if (ShouldCombineWithNextLine(line, nextLine))
                        {
                            receiverName += " " + nextLine;
                            i++; // Skip next line since we consumed it
                        }

                        slip.ReceiverName = receiverName.Trim();
                        _logger.LogDebug("Found receiver name: {Name} (PromptPay section: {InPromptPay})",
                            slip.ReceiverName, foundPromptPaySection);
                    }
                }

                // Enhanced note/memo detection - only process if we haven't found a good note yet
                else if (string.IsNullOrEmpty(slip.Note) && IsNoteLine(line))
                {
                    var note = ExtractNote(line, lines, i);
                    if (!string.IsNullOrEmpty(note) && note.Length > 2)
                    {
                        slip.Note = note;
                        _logger.LogDebug("Found note: '{Note}'", slip.Note);
                    }
                    else
                    {
                        _logger.LogDebug("Note line detected but extraction failed or too short: '{Line}'", line);
                    }
                }
            }

            // Final validation and cleanup
            CleanupAndValidate(slip);

            // Enhanced logging for debugging
            LogParsingResults(slip, imagePath);

            return slip;
        }

        // Add this helper method to detect e-wallet IDs
        private bool CouldBeEWalletId(string line)
        {
            // E-wallet IDs are usually long alphanumeric strings with letters and numbers
            // Examples: 46095486RK4C53000000, 000002200860880
            if (line.Length < 10) return false;

            // Must contain at least some digits
            if (!line.Any(char.IsDigit)) return false;

            // Check for common e-wallet patterns:
            // 1. Long numeric strings (15+ digits)
            // 2. Alphanumeric with both letters and numbers
            // 3. Should not contain spaces or special chars (except maybe dashes)

            if (Regex.IsMatch(line, @"^\d{15,}$")) // Long numeric string
                return true;

            if (Regex.IsMatch(line, @"^[A-Z0-9]{10,}$") && line.Any(char.IsLetter)) // Alphanumeric
                return true;

            return false;
        }

        #region Enhanced Note Detection Methods

        private bool IsNoteLine(string line)
        {
            var notePatterns = new[]
            {
                "บันทึกช่วยจำ", "บันทึกช่วยจํา", "บันทึก", "หมายเหตุ", "Memo", "Note",
                "รายละเอียด", "วัตถุประสงค์", "หมายเหตุ:", "บันทึก:", "ข้อความ"
            };

            return notePatterns.Any(pattern => line.Contains(pattern));
        }

        private string ExtractNote(string line, List<string> lines, int currentIndex)
        {
            var note = "";

            // Try to extract note from current line first
            var colonIndex = line.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < line.Length - 1)
            {
                note = line.Substring(colonIndex + 1).Trim();
            }
            else
            {
                // If no colon, check if the line itself contains the note after keywords
                var noteKeywords = new[] { "บันทึกช่วยจำ", "บันทึกช่วยจํา", "หมายเหตุ", "บันทึก" };
                var keyword = noteKeywords.FirstOrDefault(k => line.Contains(k));
                if (keyword != null)
                {
                    var keywordIndex = line.IndexOf(keyword);
                    if (keywordIndex >= 0)
                    {
                        var afterKeyword = line.Substring(keywordIndex + keyword.Length).Trim();
                        // Remove any leading colons or special characters
                        note = afterKeyword.TrimStart(':', '-', ' ');
                    }
                }
            }

            // Enhanced validation: if note is too short, incomplete, or looks like OCR noise, check next lines
            if (IsNoteIncompleteOrInvalid(note))
            {
                _logger.LogDebug("Note '{Note}' appears incomplete or invalid, checking subsequent lines", note);

                // Look at the next few lines for actual note content
                for (int i = currentIndex + 1; i < Math.Min(lines.Count, currentIndex + 4); i++)
                {
                    var nextLine = lines[i].Trim();

                    // Skip system lines and known non-note content
                    if (ShouldSkipLineForNote(nextLine))
                    {
                        continue;
                    }

                    // If this looks like valid note content
                    if (IsValidNoteContent(nextLine))
                    {
                        if (string.IsNullOrEmpty(note) || note.Length < 5)
                        {
                            note = nextLine;
                            _logger.LogDebug("Found note content in next line: '{Note}'", note);
                        }
                        else
                        {
                            note += " " + nextLine;
                        }

                        // Don't continue if we found a substantial note
                        if (note.Length > 15)
                        {
                            break;
                        }
                    }
                }
            }

            // Final cleanup and validation
            note = CleanupNoteText(note);

            // Final validation - reject if still looks like OCR noise or system text
            if (IsNoteTextInvalid(note))
            {
                _logger.LogDebug("Rejecting invalid note text: '{Note}'", note);
                return "";
            }

            return note;
        }

        private bool IsNoteIncompleteOrInvalid(string note)
        {
            if (string.IsNullOrWhiteSpace(note)) return true;
            if (note.Length < 3) return true;

            // Check for common OCR fragments that indicate incomplete extraction
            var ocrNoise = new[] { "ช่วย", "จ่า", "จํา", "ช่วยจำ", "ช่วยจํา" };
            if (ocrNoise.Any(noise => note.Equals(noise, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Check if it's just part of the keyword itself
            var noteKeywords = new[] { "บันทึก", "หมายเหตุ" };
            if (noteKeywords.Any(keyword => note.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private bool ShouldSkipLineForNote(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 2) return true;

            var skipPatterns = new[]
            {
                "บาท", "ยอดคงเหลือ", "ค่าธรรมเนียม", "เลขที่รายการ",
                "สแกน", "ตรวจสอบ", "Mobile Banking", "ATM", "Online",
                "สำเร็จ", "เสร็จสิ้น", "completed", "จํานวน:", "จำนวน:",
                "รหัสพร้อมเพย์", "พร้อมเพย์", "PromptPay"
            };

            // Skip if contains skip patterns
            if (skipPatterns.Any(pattern => line.Contains(pattern))) return true;

            // Skip if it looks like a date
            if (IsDateLine(line)) return true;

            // Skip if it looks like an account number
            if (IsAccountNumber(line)) return true;

            // Skip if it's mostly numbers
            if (line.Count(char.IsDigit) > line.Length / 2) return true;

            return false;
        }

        private bool IsValidNoteContent(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;

            // Should not be mostly numbers
            if (line.Count(char.IsDigit) > line.Length / 2) return false;

            // Should not contain system keywords
            var systemKeywords = new[]
            {
                "บาท", "ยอดคงเหลือ", "ค่าธรรมเนียม", "สแกน", "ตรวจสอบ",
                "เลขที่", "รายการ", "ATM", "Mobile", "Online"
            };

            if (systemKeywords.Any(keyword => line.Contains(keyword))) return false;

            // Should not be account numbers or dates
            if (IsAccountNumber(line) || IsDateLine(line)) return false;

            // Should contain meaningful Thai text or common note content
            // Look for Thai characters or common words that indicate real note content
            var validNoteIndicators = new[]
            {
                "ค่า", "ซื้อ", "จ่าย", "ชื่อ", "สำหรับ", "เพื่อ", "ให้", "แล้ว",
                "งาน", "บริการ", "สินค้า", "อาหาร", "เงิน", "ผ่อน", "ดอกเบี้ย"
            };

            // If it contains valid indicators, it's likely a real note
            if (validNoteIndicators.Any(indicator => line.Contains(indicator)))
            {
                return true;
            }

            // If it's mostly Thai characters and reasonable length, consider it valid
            var thaiCharCount = line.Count(c => c >= '\u0E00' && c <= '\u0E7F');
            if (thaiCharCount > line.Length / 2 && line.Length >= 5)
            {
                return true;
            }

            return false;
        }

        private string CleanupNoteText(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";

            // Remove common prefixes that might have been included
            var prefixesToRemove = new[]
            {
                "บันทึกช่วยจำ", "บันทึกช่วยจํา", "หมายเหตุ", "บันทึก",
                ":", "-", "ช่วยจำ", "ช่วยจํา", "ช่วย", "จ่า", "จํา"
            };

            foreach (var prefix in prefixesToRemove)
            {
                if (note.StartsWith(prefix))
                {
                    note = note.Substring(prefix.Length).Trim();
                }
            }

            // Remove trailing punctuation
            note = note.TrimEnd(':', '-', ' ', '.', ',');

            // Remove leading punctuation
            note = note.TrimStart(':', '-', ' ', '.', ',');

            return note.Trim();
        }

        private bool IsNoteTextInvalid(string note)
        {
            if (string.IsNullOrWhiteSpace(note) || note.Length < 3) return true;

            // Reject if it's just OCR noise or fragments
            var invalidTexts = new[]
            {
                "ช่วย", "จ่า", "จํา", "ช่วยจำ", "ช่วยจํา", "บันทึก", "หมายเหตุ",
                "เค", "สแกน", "ตรวจ", "เลข", "รหัส"
            };

            if (invalidTexts.Any(invalid => note.Equals(invalid, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Reject if it still contains system keywords
            var systemKeywords = new[]
            {
                "ยอดคงเหลือ", "ค่าธรรมเนียม", "เลขที่รายการ",
                "สแกนตรวจสอบ", "รหัสพร้อมเพย์", "Mobile Banking"
            };

            if (systemKeywords.Any(keyword => note.Contains(keyword)))
            {
                return true;
            }

            return false;
        }

        private bool IsAccountNumber(string line)
        {
            return Regex.IsMatch(line, @"[xX]{3,}[-\d]+|[\d]+-[xX]{3,}|\d{3}-\d-\d{5}-\d") ||
                   line.Count(char.IsDigit) > line.Length / 2;
        }

        #endregion

        #region Helper Methods

        private bool ShouldSkipLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 2) return true;

            var skipPatterns = new[]
            {
                "เค+", "สแกนตรวจสอบ", "จํานวน:", "จำนวน:", "ค่าธรรมเนียม:",
                "รหัสพร้อมเพย์", "ยอดคงเหลือ", "เลขที่รายการ", "Mobile Banking",
                "ATM", "Online", "สำเร็จ", "เสร็จสิ้น", "completed"
            };

            return skipPatterns.Any(pattern => line.Contains(pattern));
        }

        private bool IsValidReceiverName(string line, string senderName)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;

            // Skip if same as sender
            if (!string.IsNullOrEmpty(senderName) && line.Equals(senderName, StringComparison.OrdinalIgnoreCase))
                return false;

            // Skip system keywords (keep existing logic)
            var systemKeywords = new[] { "จํานวน", "จำนวน", "ค่าธรรมเนียม", "บาท", "พร้อมเพย์", "รหัส", "สแกน", "ตรวจสอบ", "ยอด", "คงเหลือ", "เลขที่", "รายการ", "บันทึก" };
            if (systemKeywords.Any(keyword => line.Contains(keyword))) return false;

            // Skip account numbers
            if (Regex.IsMatch(line, @"[xX]{3,}|^\d+-\d+-\d+") || line.Count(char.IsDigit) > line.Length / 2)
                return false;

            // Thai name patterns
            var thaiNamePatterns = new[] { "นาย", "นาง", "น.ส.", "บจ.", "บมจ.", "บริษัท" };
            if (thaiNamePatterns.Any(pattern => line.StartsWith(pattern)))
                return true;

            // English company patterns - ADD THIS
            var englishCompanyPatterns = new[] { "COMPANY", "LTD", "LIMITED", "CORP", "INC", "CO.", "GROUP", "INTERNATIONAL" };
            if (englishCompanyPatterns.Any(pattern => line.ToUpper().Contains(pattern)))
                return true;

            // Check for capitalized English names (likely company names)
            if (Regex.IsMatch(line, @"^[A-Z][A-Z\s&\.]+$") && line.Length > 5)
                return true;

            return false;
        }

        private bool IsValidPersonOrCompanyName(string line, string excludeName)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 3) return false;

            if (!string.IsNullOrEmpty(excludeName) && line.Equals(excludeName, StringComparison.OrdinalIgnoreCase))
                return false;

            var validPrefixes = new[] { "นาย", "นาง", "น.ส.", "เด็กชาย", "เด็กหญิง", "บจ.", "บมจ.", "บริษัท", "ห้าง" };
            return validPrefixes.Any(prefix => line.StartsWith(prefix));
        }

        private string FindSenderNameNearAccount(List<string> lines, int accountLineIndex)
        {
            for (int i = Math.Max(0, accountLineIndex - 3); i <= Math.Min(lines.Count - 1, accountLineIndex + 2); i++)
            {
                if (i != accountLineIndex && IsValidPersonOrCompanyName(lines[i], ""))
                {
                    return lines[i];
                }
            }
            return "";
        }

        private string FindReceiverNameNearAccount(List<string> lines, int accountLineIndex, string senderName)
        {
            for (int i = Math.Max(0, accountLineIndex - 5); i <= Math.Min(lines.Count - 1, accountLineIndex + 3); i++)
            {
                if (i != accountLineIndex)
                {
                    var line = lines[i];
                    if (IsValidReceiverName(line, senderName))
                    {
                        _logger.LogDebug("Found potential receiver name near account: '{Name}'", line);
                        return line;
                    }
                }
            }
            return "";
        }

        private bool ShouldCombineWithNextLine(string currentLine, string nextLine)
        {
            if (string.IsNullOrWhiteSpace(nextLine)) return false;

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

        private void CleanupAndValidate(BankSlipData slip)
        {
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
                    slip.ReceiverName = "";
                }
            }

            // ADD THIS: Set default message when receiver name couldn't be resolved
            if (string.IsNullOrEmpty(slip.ReceiverName))
            {
                slip.ReceiverName = "Could not resolve receiver";
                _logger.LogDebug("No valid receiver name found, setting default message");
            }
        }

        private void LogParsingResults(BankSlipData slip, string imagePath)
        {
            _logger.LogDebug("=== Enhanced Parsing Results for {FileName} ===", Path.GetFileName(imagePath));
            _logger.LogDebug("Date: {Date}", slip.TransactionDate);
            _logger.LogDebug("Amount: {Amount}", slip.Amount);
            _logger.LogDebug("Sender: '{Sender}' | Account: '{SenderAccount}'", slip.AccountName, slip.AccountNumber);
            _logger.LogDebug("Receiver: '{Receiver}' | Account: '{ReceiverAccount}'",
                slip.ReceiverName ?? "NOT FOUND", slip.ReceiverAccount ?? "NOT FOUND");
            _logger.LogDebug("Note: '{Note}'", slip.Note ?? "NOT FOUND");

            var isValid = slip.TransactionDate != default && slip.Amount > 0 &&
                          !string.IsNullOrEmpty(slip.AccountName) && !string.IsNullOrEmpty(slip.AccountNumber);
            _logger.LogDebug("Valid: {IsValid}", isValid);
            _logger.LogDebug("================================================");
        }

        private string CleanAccountNumber(string line)
        {
            return line.Replace("+", "").Replace(" ", "").Replace(":", "").Replace(".", "");
        }

        private string ExtractAmount(string line)
        {
            var match = Regex.Match(line, @"([\d,]+\.?\d*)\s*บาท");
            if (match.Success)
            {
                return match.Groups[1].Value.Replace(",", "");
            }
            return line.Replace("บาท", "").Replace(",", "").Trim();
        }

        private DateTime ParseThaiDate(string dateText)
        {
            dateText = dateText.Replace("น.", "").Replace("'", "").Trim();

            var patterns = new[]
            {
                @"(\d{1,2})\s*(ม\.ค\.|ก\.พ\.|มี\.ค\.|เม\.ย\.|พ\.ค\.|มิ\.ย\.|ก\.ค\.|ส\.ค\.|ก\.ย\.|ต\.ค\.|พ\.ย\.|ธ\.ค\.)\s*(\d{2,4})",
                @"(\d{1,2})\s+(ม\.ค\.|ก\.พ\.|มี\.ค\.|เม\.ย\.|พ\.ค\.|มิ\.ย\.|ก\.ค\.|ส\.ค\.|ก\.ย\.|ต\.ค\.|พ\.ย\.|ธ\.ค\.)\s+(\d{2,4})"
            };

            foreach (var pattern in patterns)
            {
                var regex = new Regex(pattern);
                var match = regex.Match(dateText);

                if (match.Success)
                {
                    try
                    {
                        var day = int.Parse(match.Groups[1].Value);
                        var monthStr = match.Groups[2].Value;
                        var yearStr = match.Groups[3].Value;

                        var year = int.Parse(yearStr);
                        if (year < 100)
                        {
                            year += (year < 50) ? 2570 : 2500;
                        }
                        else if (year < 2500)
                        {
                            year += 543;
                        }

                        var month = GetMonthNumber(monthStr);
                        return new DateTime(year, month, day);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            throw new FormatException($"Could not parse Thai date: {dateText}");
        }

        private int GetMonthNumber(string monthStr)
        {
            return monthStr switch
            {
                "ม.ค." => 1,
                "ก.พ." => 2,
                "มี.ค." => 3,
                "เม.ย." => 4,
                "พ.ค." => 5,
                "มิ.ย." => 6,
                "ก.ค." => 7,
                "ส.ค." => 8,
                "ก.ย." => 9,
                "ต.ค." => 10,
                "พ.ย." => 11,
                "ธ.ค." => 12,
                _ => throw new ArgumentException($"Unknown month: {monthStr}")
            };
        }

        private bool IsDateLine(string line)
        {
            return ThaiMonths.Any(month => line.Contains(month)) && line.Any(char.IsDigit);
        }

        private ImageAnnotatorClient CreateVisionClient(string credentialsPath)
        {
            try
            {
                if (!File.Exists(credentialsPath))
                {
                    throw new FileNotFoundException($"Google Cloud credentials file not found: {credentialsPath}");
                }

                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
                return ImageAnnotatorClient.Create();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Vision API client");
                throw;
            }
        }

        private List<string> GetImageFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Directory does not exist: {Directory}", directory);
                return new List<string>();
            }

            try
            {
                return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsImageFile(f))
                    .Where(f => !Path.GetFileName(f).StartsWith("processed_") &&
                              !Path.GetFileName(f).StartsWith("enhanced_processed_"))
                    .OrderBy(f => f)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image files from {Directory}", directory);
                return new List<string>();
            }
        }

        private bool IsImageFile(string path)
        {
            var extension = Path.GetExtension(path).ToLower();
            return extension == ".jpg" || extension == ".jpeg" || extension == ".png";
        }

        private async Task SaveProcessedSlipsAsync(List<BankSlipData> slips, string username)
        {
            try
            {
                _logger.LogInformation("Attempting to save {Count} enhanced processed slips for user {Username}",
                    slips.Count, username);

                await EnsureStorageInitializedAsync();
                var existingSlips = await _slipsStorage!.LoadAsync(username) ?? new List<BankSlipData>();

                _logger.LogDebug("Loaded {ExistingCount} existing slips, adding {NewCount} new slips",
                    existingSlips.Count, slips.Count);

                existingSlips.AddRange(slips);
                await _slipsStorage.SaveAsync(username, existingSlips);

                _logger.LogInformation("Successfully saved enhanced processed slips to storage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving enhanced processed slips for user {Username}: {Error}",
                    username, ex.Message);
            }
        }

        #endregion
    }
}