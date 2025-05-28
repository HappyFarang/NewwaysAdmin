// NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipOcrService.cs
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
    public interface IBankSlipOcrService
    {
        Task<BankSlipProcessingResult> ProcessSlipCollectionAsync(
            string collectionId,
            DateTime startDate,
            DateTime endDate,
            string username);
        Task<List<SlipCollection>> GetUserCollectionsAsync(string username);
        Task<SlipCollection?> GetCollectionAsync(string collectionId, string username);
        Task SaveCollectionAsync(SlipCollection collection, string username);
        Task DeleteCollectionAsync(string collectionId, string username);
        Task<BankSlipData?> TestProcessSingleFileAsync(string filePath, SlipCollection collection);
    }

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

        private async Task EnsureStorageInitializedAsync()
        {
            if (_collectionsStorage != null && _slipsStorage != null) return;

            await _initLock.WaitAsync();
            try
            {
                _collectionsStorage ??= await _ioManager.GetStorageAsync<List<SlipCollection>>("BankSlip_Collections");
                _slipsStorage ??= await _ioManager.GetStorageAsync<List<BankSlipData>>("BankSlip_Data");
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
            await EnsureStorageInitializedAsync();

            var collections = await _collectionsStorage!.LoadAsync(username) ?? new List<SlipCollection>();

            var existingIndex = collections.FindIndex(c => c.Id == collection.Id);
            if (existingIndex >= 0)
            {
                collections[existingIndex] = collection;
            }
            else
            {
                collection.CreatedBy = username;
                collection.CreatedAt = DateTime.UtcNow;
                collections.Add(collection);
            }

            await _collectionsStorage.SaveAsync(username, collections);
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
            string username)
        {
            var collection = await GetCollectionAsync(collectionId, username);
            if (collection == null)
            {
                throw new ArgumentException($"Collection not found: {collectionId}");
            }

            var result = new BankSlipProcessingResult
            {
                ProcessingStarted = DateTime.UtcNow
            };

            try
            {
                _visionClient = CreateVisionClient(collection.CredentialsPath);

                var files = GetImageFiles(collection.SourceDirectory);
                result.Summary.TotalFiles = files.Count;

                foreach (var file in files)
                {
                    try
                    {
                        var slipData = await ProcessSingleFileAsync(file, collection, startDate, endDate);
                        if (slipData != null)
                        {
                            slipData.ProcessedBy = username;
                            slipData.ProcessedAt = DateTime.UtcNow;
                            slipData.SlipCollectionName = collection.Name;
                            result.ProcessedSlips.Add(slipData);
                            result.Summary.ProcessedFiles++;
                        }
                        else
                        {
                            result.Summary.FailedFiles++;
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
                }

                // Save processed slips
                if (result.ProcessedSlips.Any())
                {
                    await SaveProcessedSlipsAsync(result.ProcessedSlips, username);
                }

                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;

                _logger.LogInformation("Processed {ProcessedCount} of {TotalCount} files for collection {CollectionName}",
                    result.Summary.ProcessedFiles, result.Summary.TotalFiles, collection.Name);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch processing for collection {CollectionId}", collectionId);
                throw;
            }
        }

        public async Task<BankSlipData?> TestProcessSingleFileAsync(string filePath, SlipCollection collection)
        {
            try
            {
                _visionClient = CreateVisionClient(collection.CredentialsPath);
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
            var tempProcessedPath = Path.Combine(collection.OutputDirectory, "temp_processed.jpg");

            // Try different processing passes
            foreach (var pass in collection.ProcessingSettings.ProcessingPasses)
            {
                try
                {
                    var parameters = GetProcessingParameters(pass, collection.ProcessingSettings);
                    ProcessImage(filePath, tempProcessedPath, parameters);

                    var slipData = await ExtractSlipDataAsync(tempProcessedPath);
                    if (slipData != null)
                    {
                        slipData.OriginalFilePath = filePath;

                        // Enhanced date validation for tablet files
                        if (!ValidateAndFixDate(slipData, filePath))
                        {
                            _logger.LogWarning("Date validation failed for {FilePath}", filePath);
                            continue;
                        }

                        var ceDate = slipData.TransactionDate.AddYears(-543);

                        if (startDate != DateTime.MinValue && endDate != DateTime.MaxValue)
                        {
                            if (ceDate >= startDate && ceDate <= endDate)
                            {
                                slipData.Status = BankSlipProcessingStatus.Completed;
                                return slipData;
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
                        pass, filePath, ex.Message);
                    continue;
                }
            }

            return null;
        }

        private bool ValidateAndFixDate(BankSlipData slipData, string filePath)
        {
            try
            {
                // If date parsing failed or resulted in a suspicious date, try to fix it
                if (slipData.TransactionDate == default ||
                    slipData.TransactionDate.Year < 2560 || // Before 2017 BE
                    slipData.TransactionDate.Year > 2570)   // After 2027 BE
                {
                    _logger.LogWarning("Invalid date detected: {Date} for file {FilePath}",
                        slipData.TransactionDate, filePath);

                    // Try to use file timestamp as fallback
                    var fileInfo = new FileInfo(filePath);
                    var fileDate = fileInfo.LastWriteTime;

                    // Convert to Buddhist Era
                    slipData.TransactionDate = fileDate.AddYears(543);

                    _logger.LogInformation("Using file timestamp as date: {Date} for {FilePath}",
                        slipData.TransactionDate, filePath);
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
            var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);
            var response = await _visionClient!.DetectTextAsync(image);

            if (!response.Any())
            {
                _logger.LogDebug("OCR returned no text for {ImagePath}", imagePath);
                return null;
            }

            var text = string.Join("\n", response.Select(r => r.Description));
            return ParseSlipText(text, imagePath);
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

            // Enhanced regex for better tablet support
            var patterns = new[]
            {
                @"(\d{1,2})\s*(ม\.ค\.|ก\.พ\.|มี\.ค\.|เม\.ย\.|พ\.ค\.|มิ\.ย\.|ก\.ค\.|ส\.ค\.|ก\.ย\.|ต\.ค\.|พ\.ย\.|ธ\.ค\.)\s*(\d{2,4})\s*(\d{1,2}:\d{2})?",
                @"(\d{1,2})\s+(ม\.ค\.|ก\.พ\.|มี\.ค\.|เม\.ย\.|พ\.ค\.|มิ\.ย\.|ก\.ค\.|ส\.ค\.|ก\.ย\.|ต\.ค\.|พ\.ย\.|ธ\.ค\.)\s+(\d{2,4})",
                @"(\d{1,2})/(ม\.ค\.|ก\.พ\.|มี\.ค\.|เม\.ย\.|พ\.ค\.|มิ\.ย\.|ก\.ค\.|ส\.ค\.|ก\.ย\.|ต\.ค\.|พ\.ย\.|ธ\.ค\.)(\d{2,4})"
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
                        var timeStr = match.Groups.Count > 4 ? match.Groups[4].Value : "";

                        var year = int.Parse(yearStr);

                        // Enhanced year handling for tablet dates
                        if (year < 100)
                        {
                            if (year < 50)
                                year += 2570; // 27xx BE (20xx CE)
                            else
                                year += 2500; // 25xx BE (19xx CE)
                        }
                        else if (year < 2500)
                        {
                            year += 543; // Convert CE to BE
                        }

                        // Special handling for common OCR errors
                        if (year == 2557) year = 2567; // Common OCR mistake

                        var month = GetMonthNumber(monthStr);
                        var hour = 0;
                        var minute = 0;

                        if (!string.IsNullOrEmpty(timeStr) && timeStr.Contains(":"))
                        {
                            var timeParts = timeStr.Split(':');
                            if (timeParts.Length == 2)
                            {
                                int.TryParse(timeParts[0], out hour);
                                int.TryParse(timeParts[1], out minute);
                            }
                        }

                        return new DateTime(year, month, day, hour, minute, 0);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Failed to parse date with pattern {Pattern}: {Error}", pattern, ex.Message);
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
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
            return ImageAnnotatorClient.Create();
        }

        private List<string> GetImageFiles(string directory)
        {
            if (!Directory.Exists(directory))
                return new List<string>();

            return Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => IsImageFile(f))
                .Where(f => !Path.GetFileName(f).StartsWith("processed_"))
                .ToList();
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
            await EnsureStorageInitializedAsync();

            var existingSlips = await _slipsStorage!.LoadAsync(username) ?? new List<BankSlipData>();
            existingSlips.AddRange(slips);

            await _slipsStorage.SaveAsync(username, existingSlips);
        }

        // Image processing methods (simplified - you'll need to implement the full image processing)
        private void ProcessImage(string inputPath, string outputPath, ProcessingParameters parameters)
        {
            // This is a placeholder - you'll need to implement the actual image processing
            // from your original EnhancedImageProcessor class
            File.Copy(inputPath, outputPath, true);
        }
    }
}