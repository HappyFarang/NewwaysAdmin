// Fixed BankSlipOcrService with proper date filtering and file counting
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.SharedModels.BankSlips;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class BankSlipOcrService : IBankSlipOcrService
    {
        private readonly ILogger<BankSlipOcrService> _logger;
        private readonly IOManager _ioManager;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<List<SlipCollection>>? _collectionsStorage;
        private IDataStorage<List<BankSlipData>>? _slipsStorage;
        private ImageAnnotatorClient? _visionClient;

        private static readonly string[] ThaiMonths = {
            "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
            "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."
        };

        public BankSlipOcrService(ILogger<BankSlipOcrService> logger, IOManager ioManager)
        {
            _logger = logger;
            _ioManager = ioManager;
        }

        // ... [Previous methods remain the same until ProcessSlipCollectionAsync] ...

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
            var collections = await _collectionsStorage!.LoadAsync(username) ?? new List<SlipCollection>();
            return collections.Where(c => c.IsActive).ToList();
        }

        public async Task<SlipCollection?> GetCollectionAsync(string collectionId, string username)
        {
            var collections = await GetUserCollectionsAsync(username);
            return collections.FirstOrDefault(c => c.Id == collectionId);
        }

        public async Task SaveCollectionAsync(SlipCollection collection, string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                _logger.LogInformation("Saving collection {CollectionName} for user {Username}",
                    collection.Name, username);

                var collections = await _collectionsStorage!.LoadAsync(username) ?? new List<SlipCollection>();
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

                await _collectionsStorage.SaveAsync(username, collections);
                _logger.LogInformation("Collection saved successfully");
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
            var collections = await _collectionsStorage!.LoadAsync(username) ?? new List<SlipCollection>();
            var collection = collections.FirstOrDefault(c => c.Id == collectionId);

            if (collection != null)
            {
                collection.IsActive = false;
                await _collectionsStorage.SaveAsync(username, collections);
            }
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

            _logger.LogInformation("Starting bank slip processing for collection {CollectionName} from {StartDate} to {EndDate}",
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

                // Initialize Vision API client
                _visionClient = CreateVisionClient(collection.CredentialsPath);
                _logger.LogInformation("Vision API client initialized");

                // Get all image files - but we'll filter by date during processing
                var allFiles = GetImageFiles(collection.SourceDirectory);
                _logger.LogInformation("Found {TotalFileCount} total image files in source directory", allFiles.Count);

                if (allFiles.Count == 0)
                {
                    _logger.LogWarning("No image files found in {Directory}", collection.SourceDirectory);
                    result.ProcessingCompleted = DateTime.UtcNow;
                    result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;
                    return result;
                }

                // Pre-filter files by file timestamp to get a better estimate
                // File timestamps are in CE (Christian Era), so use CE dates for comparison
                var endDateInclusive = endDate.AddDays(1); // Add one day to include end date

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
                        return true; // Include files we can't check timestamp for
                    }
                }).ToList();

                _logger.LogInformation("Pre-filtered to {CandidateCount} files based on file timestamps (CE date range: {StartCE} to {EndCE})",
                    candidateFiles.Count, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

                // Use candidate files for progress reporting
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

                        // Report progress with current file
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
                            _logger.LogDebug("Successfully processed {FileName}, Amount: {Amount}, Date: {Date}",
                                fileName, slipData.Amount, ceDate.ToString("yyyy-MM-dd"));
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

                    // Small delay to prevent overwhelming the system
                    await Task.Delay(50);
                }

                // Update final counts
                result.Summary.DateOutOfRangeFiles = candidateFiles.Count - result.Summary.ProcessedFiles - result.Summary.FailedFiles;
                result.Summary.TotalFiles = allFiles.Count; // Set to actual total for reporting

                // Report final progress
                progressReporter?.ReportProgress(candidateFiles.Count, candidateFiles.Count, "Processing complete");

                // Save processed slips if any
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
                        // Don't fail the entire operation - user can still download CSV
                    }
                }

                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;

                _logger.LogInformation("Processing completed. Total files: {Total}, Candidates: {Candidates}, Success: {Success}, Failed: {Failed}, Out of range: {OutOfRange}",
                    allFiles.Count, candidateFiles.Count, result.Summary.ProcessedFiles, result.Summary.FailedFiles, result.Summary.DateOutOfRangeFiles);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch processing");
                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;
                throw;
            }
        }

        public async Task<BankSlipData?> TestProcessSingleFileAsync(string filePath, SlipCollection collection)
        {
            try
            {
                _visionClient = CreateVisionClient(collection.CredentialsPath);
                // For testing, don't apply date filters
                return await ProcessSingleFileAsync(filePath, collection, DateTime.MinValue, DateTime.MaxValue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in test processing: {FilePath}", filePath);
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

            var tempProcessedPath = Path.Combine(collection.OutputDirectory, $"temp_processed_{Guid.NewGuid():N}.jpg");

            try
            {
                // Try different processing passes
                foreach (var pass in collection.ProcessingSettings.ProcessingPasses)
                {
                    try
                    {
                        _logger.LogDebug("Trying processing pass {Pass} for {FilePath}", pass, Path.GetFileName(filePath));

                        var parameters = GetProcessingParameters(pass, collection.ProcessingSettings);
                        ProcessImage(filePath, tempProcessedPath, parameters);

                        var slipData = await ExtractSlipDataAsync(tempProcessedPath);
                        if (slipData != null)
                        {
                            slipData.OriginalFilePath = filePath;

                            // Validate and fix date
                            if (!ValidateAndFixDate(slipData, filePath))
                            {
                                _logger.LogDebug("Date validation failed for {FilePath}", filePath);
                                continue;
                            }

                            // Convert BE to CE for date range checking
                            var ceDate = slipData.TransactionDate.AddYears(-543);

                            // Check date range if specified (skip if testing mode)
                            if (startDate != DateTime.MinValue && endDate != DateTime.MaxValue)
                            {
                                if (ceDate.Date >= startDate.Date && ceDate.Date <= endDate.Date)
                                {
                                    slipData.Status = BankSlipProcessingStatus.Completed;
                                    _logger.LogDebug("Successfully processed {FilePath} with pass {Pass}, Date: {Date}",
                                        Path.GetFileName(filePath), pass, ceDate.ToString("yyyy-MM-dd"));
                                    return slipData;
                                }
                                else
                                {
                                    _logger.LogDebug("Date {Date} outside range {StartDate} to {EndDate} for {FilePath}",
                                        ceDate.ToString("yyyy-MM-dd"), startDate.ToString("yyyy-MM-dd"),
                                        endDate.ToString("yyyy-MM-dd"), Path.GetFileName(filePath));
                                    // Don't continue to next pass - this file is just out of range
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
                        _logger.LogDebug("Processing pass {Pass} failed for {FilePath}: {Error}",
                            pass, Path.GetFileName(filePath), ex.Message);
                        continue;
                    }
                }

                _logger.LogDebug("All processing passes failed for {FilePath}", Path.GetFileName(filePath));
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

        // ... [Rest of the methods remain the same] ...

        private void ProcessImage(string inputPath, string outputPath, ProcessingParameters parameters)
        {
            try
            {
                // For now, just copy the file - you can implement actual image processing later
                File.Copy(inputPath, outputPath, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image {InputPath}", inputPath);
                throw;
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
                return ParseSlipText(text, imagePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during OCR extraction for {ImagePath}", imagePath);
                return null;
            }
        }

        private BankSlipData? ParseSlipText(string text, string imagePath)
        {
            var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            var slip = new BankSlipData
            {
                OriginalFilePath = imagePath
            };

            foreach (var line in lines)
            {
                if (IsDateLine(line) && slip.TransactionDate == default)
                {
                    try
                    {
                        slip.TransactionDate = ParseThaiDate(line);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Failed to parse date from '{Line}': {Error}", line, ex.Message);
                    }
                }
                else if (string.IsNullOrEmpty(slip.AccountName) &&
                        (line.StartsWith("น.ส.") || line.StartsWith("นาย") ||
                         line.StartsWith("นาง") || line.StartsWith("บจ.") ||
                         line.Contains("บริษัท")))
                {
                    slip.AccountName = line;
                }
                else if ((line.Contains("XXX") || line.Contains("xxx")) && line.Contains("-"))
                {
                    var cleanLine = line.Replace("+", "").Replace(" ", "").Replace(":", "");
                    if (string.IsNullOrEmpty(slip.AccountNumber))
                    {
                        slip.AccountNumber = cleanLine;
                    }
                    else if (slip.AccountNumber != cleanLine)
                    {
                        slip.ReceiverAccount = cleanLine;
                    }
                }
                else if (line.Contains("บาท"))
                {
                    var amountStr = line.Replace("บาท", "").Replace(",", "").Trim();
                    if (decimal.TryParse(amountStr, out decimal amount) && amount > 0)
                    {
                        slip.Amount = amount;
                    }
                }
                else if (line.Contains("บันทึกช่วย"))
                {
                    slip.Note = line;
                }
            }

            // Validation
            if (slip.TransactionDate == default || slip.Amount == 0 ||
                string.IsNullOrEmpty(slip.AccountName) || string.IsNullOrEmpty(slip.AccountNumber))
            {
                return null;
            }

            return slip;
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
                    .Where(f => !Path.GetFileName(f).StartsWith("processed_"))
                    .OrderBy(f => f) // Sort files for consistent processing order
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

        private ProcessingParameters GetProcessingParameters(ProcessingPass pass, ProcessingParameters baseSettings)
        {
            return pass switch
            {
                ProcessingPass.Default => baseSettings,
                ProcessingPass.Fallback => new ProcessingParameters
                {
                    GaussianSigma = 0.8,
                    BinarizationWindow = 30,
                    BinarizationK = 0.15,
                    PreserveGrays = false,
                    BorderSize = 30
                },
                ProcessingPass.Tablet => new ProcessingParameters
                {
                    GaussianSigma = 0.7,
                    BinarizationWindow = 20,
                    BinarizationK = 0.3,
                    PreserveGrays = false,
                    BorderSize = 30
                },
                _ => baseSettings
            };
        }

        private async Task SaveProcessedSlipsAsync(List<BankSlipData> slips, string username)
        {
            try
            {
                _logger.LogInformation("Attempting to save {Count} processed slips for user {Username}", slips.Count, username);

                await EnsureStorageInitializedAsync();

                _logger.LogDebug("Storage initialized, loading existing slips...");
                var existingSlips = await _slipsStorage!.LoadAsync(username) ?? new List<BankSlipData>();

                _logger.LogDebug("Loaded {ExistingCount} existing slips, adding {NewCount} new slips",
                    existingSlips.Count, slips.Count);

                existingSlips.AddRange(slips);

                _logger.LogDebug("Saving {TotalCount} total slips to storage", existingSlips.Count);
                await _slipsStorage.SaveAsync(username, existingSlips);

                _logger.LogInformation("Successfully saved processed slips to storage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving processed slips for user {Username}: {Error}", username, ex.Message);
                // Don't rethrow - this shouldn't fail the entire processing operation
                // The results are still available in memory for the user to download
            }
        }
    }
}