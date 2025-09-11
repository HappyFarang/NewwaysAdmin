// NewwaysAdmin.WebAdmin/Services/Background/BankSlipFileProcessor.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.Services.FileProcessing;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips;
using System.Globalization;

namespace NewwaysAdmin.WebAdmin.Services.Background
{
    /// <summary>
    /// Processes bank slip images detected in external collections
    /// Performs OCR scanning and saves results as .bin files for fast access
    /// </summary>
    public class BankSlipFileProcessor : IExternalFileProcessor
    {
        private readonly ILogger<BankSlipFileProcessor> _logger;
        private readonly DocumentParser _documentParser;
        private readonly EnhancedStorageFactory _storageFactory;

        public string Name => "BankSlipOCR";
        public string[] Extensions => [".jpg", ".jpeg", ".png", ".tiff", ".bmp"];

        public BankSlipFileProcessor(
            ILogger<BankSlipFileProcessor> logger,
            DocumentParser documentParser,
            EnhancedStorageFactory storageFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _documentParser = documentParser ?? throw new ArgumentNullException(nameof(documentParser));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        }

        /// <summary>
        /// Process a bank slip image file
        /// </summary>
        public async Task<bool> ProcessAsync(string filePath, string collectionName)
        {
            try
            {
                _logger.LogInformation("🔄 Processing bank slip: {FileName} from collection: {CollectionName}",
                    Path.GetFileName(filePath), collectionName);

                // Step 1: Perform OCR scan using existing DocumentParser
                var extractedData = await PerformOcrScanAsync(filePath, collectionName);
                if (extractedData == null)
                {
                    _logger.LogWarning("❌ OCR scan failed for {FileName}", Path.GetFileName(filePath));
                    return false;
                }

                // Step 2: Create scan result
                var scanResult = CreateScanResult(filePath, collectionName, extractedData);

                // Step 3: Save as .bin file in appropriate subfolder
                var success = await SaveScanResultAsync(scanResult, collectionName);

                if (success)
                {
                    _logger.LogInformation("✅ Successfully processed {FileName} → {Summary}",
                        Path.GetFileName(filePath), scanResult.GetSummary());
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing bank slip: {FileName}", Path.GetFileName(filePath));
                return false;
            }
        }

        /// <summary>
        /// Perform OCR scan using existing DocumentParser infrastructure
        /// </summary>
        private async Task<Dictionary<string, string>?> PerformOcrScanAsync(string filePath, string collectionName)
        {
            try
            {
                // Use existing DocumentParser - same as current bank slip page
                var result = await _documentParser.ParseAsync(
                    ocrText: "", // Not used in spatial parsing, just pass empty string
                    imagePath: filePath,
                    documentType: "BankSlips",
                    formatName: collectionName  // Use collection name as format
                );

                if (result != null && result.Any())
                {
                    _logger.LogDebug("OCR extracted {FieldCount} fields from {FileName}",
                        result.Count, Path.GetFileName(filePath));
                    return result;
                }

                _logger.LogWarning("OCR scan returned no data for {FileName}",
                    Path.GetFileName(filePath));
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR scan failed for {FileName}", Path.GetFileName(filePath));
                return null;
            }
        }

        /// <summary>
        /// Create BankSlipScanResult from OCR data
        /// </summary>
        private BankSlipScanResult CreateScanResult(string filePath, string collectionName, Dictionary<string, string> extractedData)
        {
            var scanResult = new BankSlipScanResult
            {
                CollectionName = collectionName,
                OriginalFilePath = filePath,
                ScannedAt = DateTime.UtcNow,
                ExtractedData = extractedData,
                ProcessingStatus = "Completed"
            };

            // Try to parse document date from extracted data
            scanResult.DocumentDate = TryParseDocumentDate(extractedData);

            return scanResult;
        }

        /// <summary>
        /// Try to parse document date from extracted data
        /// </summary>
        private DateTime? TryParseDocumentDate(Dictionary<string, string> extractedData)
        {
            // Try common date field names
            var dateFields = new[] { "Date", "TransactionDate", "DateTime", "Time" };

            foreach (var fieldName in dateFields)
            {
                if (extractedData.TryGetValue(fieldName, out var dateText) && !string.IsNullOrWhiteSpace(dateText))
                {
                    // Try various date formats
                    var formats = new[]
                    {
                        "dd/MM/yyyy",
                        "d/M/yyyy",
                        "dd-MM-yyyy",
                        "yyyy-MM-dd",
                        "dd/MM/yyyy HH:mm",
                        "d/M/yyyy HH:mm"
                    };

                    foreach (var format in formats)
                    {
                        if (DateTime.TryParseExact(dateText.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                        {
                            return parsedDate;
                        }
                    }

                    // Try general parse as fallback
                    if (DateTime.TryParse(dateText, out var generalParse))
                    {
                        return generalParse;
                    }
                }
            }

            return null; // No valid date found
        }

        /// <summary>
        /// Save scan result as .bin file in collection subfolder
        /// </summary>
        private async Task<bool> SaveScanResultAsync(BankSlipScanResult scanResult, string collectionName)
        {
            try
            {
                // Get storage for BankSlipResults folder
                var storage = _storageFactory.GetStorage<BankSlipScanResult>("BankSlipResults");

                // Create unique filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var originalFileName = Path.GetFileNameWithoutExtension(scanResult.OriginalFilePath);
                var fileName = $"{timestamp}_{originalFileName}";

                // Use collection name as subfolder path
                var identifier = $"{collectionName}/{fileName}";

                await storage.SaveAsync(identifier, scanResult);

                _logger.LogDebug("Saved scan result: {Identifier}", identifier);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save scan result for {FileName}", Path.GetFileName(scanResult.OriginalFilePath));
                return false;
            }
        }
    }
}