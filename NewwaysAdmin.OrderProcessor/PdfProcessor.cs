using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.SharedModels.Config;
using System.Text;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.OrderProcessor
{
    public class PdfProcessor
    {
        private readonly IOManager _ioManager;
        private readonly ILogger<PdfProcessor> _logger;
        private readonly PrinterManager _printerManager;
        private readonly string _backupFolder;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<ScanResult>? _scanStorage;
        private IDataStorage<ProcessorConfig>? _configStorage;

        public PdfProcessor(
            IOManager ioManager,
            string backupFolder,
            ILogger<PdfProcessor> logger)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _backupFolder = backupFolder ?? throw new ArgumentNullException(nameof(backupFolder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _printerManager = new PrinterManager(ioManager, logger);

            // Subscribe to new file events from IOManager
            _ioManager.NewDataArrived += async (s, e) => await OnNewDataAsync(e.FilePath);
            _logger.LogInformation("PdfProcessor initialized and subscribed to IOManager events");
        }

        private async Task EnsureStorageInitializedAsync()
        {
            if (_scanStorage != null && _configStorage != null)
                return;

            await _initLock.WaitAsync();
            try
            {
                if (_scanStorage == null)
                {
                    _scanStorage = await _ioManager.GetStorageAsync<ScanResult>("PDFProcessor_Scans");
                    _logger.LogDebug("Initialized scan storage");
                }
                if (_configStorage == null)
                {
                    _configStorage = await _ioManager.GetStorageAsync<ProcessorConfig>("PDFProcessor_Config");
                    _logger.LogDebug("Initialized config storage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize storage");
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task OnNewDataAsync(string filePath)
        {
            if (!filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return;

            _logger.LogInformation("Detected new PDF file: {FilePath}", filePath);
            await ProcessPdfAsync(filePath);
        }

        public async Task ProcessPdfAsync(string pdfPath)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                if (_scanStorage == null || _configStorage == null)
                {
                    _logger.LogError("Storage not initialized properly");
                    throw new InvalidOperationException("Storage not initialized properly");
                }

                string originalFileName = Path.GetFileName(pdfPath);
                _logger.LogInformation("Processing PDF: {File}", originalFileName);

                // Load platforms configuration
                ProcessorConfig platformConfig = null;
                const string configFileName = "platforms.json";

                try
                {
                    // First try to get the file from the server in all locations it might be stored
                    try
                    {
                        // Check in X:/NewwaysAdmin/Definitions/PdfProcessor
                        string[] possibleServerPaths = {
                            Path.Combine("X:/NewwaysAdmin/Definitions/PdfProcessor", configFileName),
                            Path.Combine("X:/NewwaysAdmin/Config/PdfProcessor", configFileName),
                            Path.Combine("X:/NewwaysAdmin/PdfProcessor", configFileName)
                        };

                        string serverFilePath = null;
                        DateTime newestModTime = DateTime.MinValue;
                        bool foundOnServer = false;

                        // Find the newest version of the file on the server
                        foreach (var path in possibleServerPaths)
                        {
                            try
                            {
                                if (File.Exists(path))
                                {
                                    var modTime = File.GetLastWriteTimeUtc(path);
                                    _logger.LogInformation("Found server configuration at {Path} (Modified: {Time})", path, modTime);
                                    foundOnServer = true;

                                    if (modTime > newestModTime)
                                    {
                                        serverFilePath = path;
                                        newestModTime = modTime;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error checking server path: {Path}", path);
                            }
                        }

                        if (foundOnServer && !string.IsNullOrEmpty(serverFilePath))
                        {
                            _logger.LogInformation("Using newest server configuration: {Path}", serverFilePath);

                            // Get the local file information
                            string localDirPath = Path.Combine("C:/NewwaysData/Config/PdfProcessor");
                            Directory.CreateDirectory(localDirPath);
                            string localFilePath = Path.Combine(localDirPath, configFileName);

                            bool shouldCopy = true;
                            // Check if local file exists and compare timestamps
                            if (File.Exists(localFilePath))
                            {
                                var localModTime = File.GetLastWriteTimeUtc(localFilePath);
                                if (newestModTime <= localModTime)
                                {
                                    _logger.LogInformation("Local file is newer or same age ({LocalTime}) than server ({ServerTime}). Not copying.",
                                        localModTime, newestModTime);
                                    shouldCopy = false;
                                }
                                else
                                {
                                    _logger.LogInformation("Server file is newer ({ServerTime}) than local ({LocalTime}). Copying.",
                                        newestModTime, localModTime);
                                }
                            }

                            if (shouldCopy)
                            {
                                // Copy server file to local directory
                                File.Copy(serverFilePath, localFilePath, true);
                                _logger.LogInformation("Copied server configuration to local path: {Path}", localFilePath);

                                // Also ensure the file timestamp is preserved
                                File.SetLastWriteTimeUtc(localFilePath, newestModTime);
                            }

                            // Load configuration from the file
                            string rawContent = await File.ReadAllTextAsync(localFilePath);
                            platformConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<ProcessorConfig>(
                                rawContent,
                                new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore }
                            );

                            if (platformConfig?.Platforms != null && platformConfig.Platforms.Count > 0)
                            {
                                _logger.LogInformation("Successfully loaded platform configuration from server with {Count} platforms (Version: {Version})",
                                    platformConfig.Platforms.Count, platformConfig.Version);

                                // Also save to the IO storage system for future use
                                await _configStorage.SaveAsync("platforms", platformConfig);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Could not find configuration file on server in any of the expected locations");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to access server configuration. Will try local version");
                    }

                    // If we still don't have valid config, try IO storage
                    if (platformConfig?.Platforms == null || platformConfig.Platforms.Count == 0)
                    {
                        try
                        {
                            _logger.LogInformation("Trying to load configuration from IO storage");
                            platformConfig = await _configStorage.LoadAsync("platforms");

                            if (platformConfig?.Platforms != null && platformConfig.Platforms.Count > 0)
                            {
                                _logger.LogInformation("Successfully loaded platform configuration from IO storage with {Count} platforms",
                                    platformConfig.Platforms.Count);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load configuration from IO storage");
                        }
                    }

                    // If still no config, try direct local file access
                    if (platformConfig?.Platforms == null || platformConfig.Platforms.Count == 0)
                    {
                        try
                        {
                            _logger.LogInformation("Trying direct local file access");
                            string localFilePath = Path.Combine("C:/NewwaysData/Config/PdfProcessor", configFileName);

                            if (File.Exists(localFilePath))
                            {
                                string rawContent = await File.ReadAllTextAsync(localFilePath);
                                platformConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<ProcessorConfig>(
                                    rawContent,
                                    new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore }
                                );

                                if (platformConfig?.Platforms != null && platformConfig.Platforms.Count > 0)
                                {
                                    _logger.LogInformation("Successfully loaded platform configuration from local file with {Count} platforms",
                                        platformConfig.Platforms.Count);

                                    // Save to storage for future use
                                    await _configStorage.SaveAsync("platforms", platformConfig);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Local configuration file not found: {Path}", localFilePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to load configuration from local file");
                        }
                    }

                    // If still no valid platforms, log warning and exit early
                    if (platformConfig == null || platformConfig.Platforms == null || platformConfig.Platforms.Count == 0)
                    {
                        _logger.LogWarning("No platforms configuration found. Please ensure platforms.json is properly set up");
                        return; // Exit early as we can't process without platform definitions
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error loading platforms configuration");
                    _logger.LogWarning("Cannot continue processing without valid platform configuration");
                    return; // Exit early
                }

                // Extract and normalize text from PDF
                string normalizedText = await ExtractAndNormalizeTextAsync(pdfPath);

                // Identify platform from text
                var platform = IdentifyPlatform(normalizedText, platformConfig);
                if (platform == null)
                {
                    _logger.LogWarning("No platforms identified for {File}", originalFileName);
                    return;
                }

                string? orderNumber = ExtractOrderNumber(normalizedText, platform.Value);
                var skuCounts = ExtractSkuCounts(normalizedText, platform.Value);

                // Check if order was already processed
                if (orderNumber != null && await IsOrderProcessedAsync(platform.Value, orderNumber))
                {
                    _logger.LogInformation("Order {OrderNumber} already processed, skipping", orderNumber);

                    // Delete the duplicate file instead of backing it up
                    try
                    {
                        File.Delete(pdfPath);
                        _logger.LogInformation("Deleted duplicate file: {FileName}", originalFileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting duplicate file: {FileName}", originalFileName);
                    }

                    return;
                }

                var scanResult = new ScanResult
                {
                    Id = Guid.NewGuid().ToString(),
                    ScanTime = DateTime.UtcNow,
                    Platform = platform.Value.platformId,
                    OrderNumber = orderNumber,
                    SkuCounts = skuCounts,
                    OriginalFileName = originalFileName
                };

                await _scanStorage.SaveAsync(scanResult.Id, scanResult);
                await MovePdfToBackupAsync(pdfPath, platform.Value.platformId, orderNumber);

                // Optional: Print if configured
                if (await _printerManager.PrintPdfAsync(pdfPath))
                {
                    _logger.LogInformation("Printed PDF: {File}", originalFileName);
                }

                _logger.LogInformation("PDF processing completed successfully for {File}", originalFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing PDF: {ErrorType} - {ErrorMessage}", ex.GetType().Name, ex.Message);
                throw;
            }
        }

        private async Task<string> ExtractAndNormalizeTextAsync(string pdfPath)
        {
            var normalizedText = new StringBuilder();

            using var stream = File.OpenRead(pdfPath);
            using var reader = new PdfReader(stream);
            using var document = new PdfDocument(reader);

            await Task.Run(() =>
            {
                for (int i = 1; i <= document.GetNumberOfPages(); i++)
                {
                    var strategy = new LocationTextExtractionStrategy();
                    var pageText = PdfTextExtractor.GetTextFromPage(document.GetPage(i), strategy);
                    var normalized = NormalizeText(pageText);
                    normalizedText.AppendLine(normalized);
                }
            });

            return normalizedText.ToString();
        }

        private string NormalizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var lines = input.Split(new[] { '\n', '\r' }, StringSplitOptions.None);
            var processedLines = lines.Select(line =>
            {
                var normalized = line.Normalize(NormalizationForm.FormD);
                normalized = new string(normalized.Where(c => !char.IsControl(c) && !IsCombiningCharacter(c)).ToArray());
                normalized = Regex.Replace(normalized, @"\s+", " ");
                return normalized.Trim();
            });

            return string.Join(Environment.NewLine, processedLines.Where(l => !string.IsNullOrWhiteSpace(l)));
        }

        private static bool IsCombiningCharacter(char c)
        {
            return (c >= '\u0300' && c <= '\u036F') ||  // Combining Diacritical Marks
                   (c >= '\u0E31' && c <= '\u0E3A') ||  // Thai vowel marks and tone marks
                   (c >= '\u0E47' && c <= '\u0E4E');    // Thai diacritics
        }

        private (string platformId, PlatformConfig config)? IdentifyPlatform(string normalizedText, ProcessorConfig config)
        {
            if (config == null || config.Platforms == null || config.Platforms.Count == 0)
            {
                _logger.LogWarning("No platforms defined in configuration");
                return null;
            }

            _logger.LogInformation("Identifying platform from {Count} configured platforms", config.Platforms.Count);

            foreach (var platform in config.Platforms)
            {
                if (!platform.Value.Enabled) continue;

                bool matches = false;
                if (platform.Value.Identifiers != null && platform.Value.Identifiers.Any())
                {
                    matches = platform.Value.Identifiers.Any(id =>
                        !string.IsNullOrEmpty(id) && normalizedText.Contains(id, StringComparison.OrdinalIgnoreCase));

                    if (matches)
                    {
                        _logger.LogInformation("Identified platform: {Platform}", platform.Key);
                        return (platform.Key, platform.Value);
                    }
                }
            }

            _logger.LogWarning("No matching platform found in configuration");
            return null;
        }

        private string? ExtractOrderNumber(string text, (string platformId, PlatformConfig config) platform)
        {
            if (string.IsNullOrEmpty(platform.config.OrderNumberPattern))
                return null;

            var match = Regex.Match(text, platform.config.OrderNumberPattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        private Dictionary<string, int> ExtractSkuCounts(string text, (string platformId, PlatformConfig config) platform)
        {
            var skuCounts = new Dictionary<string, int>();

            foreach (var sku in platform.config.Skus)
            {
                if (string.IsNullOrEmpty(sku.Value.Pattern))
                    continue;

                var matches = Regex.Matches(text, sku.Value.Pattern);
                if (matches.Count > 0)
                {
                    int totalQuantity = matches.Sum(m =>
                        int.TryParse(m.Groups[1].Value, out int qty) ? qty : 0);

                    if (totalQuantity > 0)
                    {
                        skuCounts[sku.Key] = totalQuantity * sku.Value.PackSize;
                    }
                }
            }

            return skuCounts;
        }

        private async Task<bool> IsOrderProcessedAsync((string platformId, PlatformConfig config) platform, string orderNumber)
        {
            try
            {
                _logger.LogInformation("Checking if order {OrderNumber} for platform {Platform} has already been processed",
                    orderNumber, platform.platformId);

                // Look back for 3 days to catch orders that might have been saved overnight or multiple days
                var cutoffDate = DateTime.Today.AddDays(-3);
                _logger.LogDebug("Using cutoff date {CutoffDate} for order processing check", cutoffDate);

                // Check in the backup folder for PDFs with matching order numbers
                if (Directory.Exists(_backupFolder))
                {
                    // Get all PDF files from the backup folder within the time range
                    var recentFiles = Directory.GetFiles(_backupFolder, "*.pdf")
                        .Where(f => File.GetCreationTime(f) >= cutoffDate)
                        .ToList();

                    _logger.LogDebug("Found {Count} recent PDF files to check for duplicates", recentFiles.Count);

                    // Check the contents of the files - focusing only on first page for efficiency
                    foreach (var file in recentFiles)
                    {
                        try
                        {
                            using var stream = File.OpenRead(file);
                            using var reader = new PdfReader(stream);
                            using var document = new PdfDocument(reader);

                            // Only extract text from the first page
                            var normalizedText = await Task.Run(() =>
                            {
                                var strategy = new LocationTextExtractionStrategy();
                                var pageText = PdfTextExtractor.GetTextFromPage(document.GetPage(1), strategy);
                                return NormalizeText(pageText);
                            });

                            // Check if this could be the same platform
                            bool isPotentialMatch = false;
                            if (platform.config.Identifiers != null && platform.config.Identifiers.Any())
                            {
                                isPotentialMatch = platform.config.Identifiers.Any(id =>
                                    !string.IsNullOrEmpty(id) && normalizedText.Contains(id, StringComparison.OrdinalIgnoreCase));
                            }

                            if (!isPotentialMatch)
                                continue;

                            // Use the platform's order number pattern to extract the order number
                            if (!string.IsNullOrEmpty(platform.config.OrderNumberPattern))
                            {
                                var match = Regex.Match(normalizedText, platform.config.OrderNumberPattern);
                                if (match.Success)
                                {
                                    string extractedOrderNumber = match.Groups[1].Value;

                                    // Compare order numbers ignoring case
                                    if (extractedOrderNumber.Equals(orderNumber, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogInformation("Found duplicate order: {OrderNumber} in file {File}",
                                            extractedOrderNumber, Path.GetFileName(file));
                                        return true;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Just log and continue to the next file if there's an error with this one
                            _logger.LogWarning(ex, "Error checking PDF file {File} for duplicate orders", file);
                        }
                    }
                }

                // Check the database storage for already processed orders
                if (_scanStorage != null)
                {
                    try
                    {
                        // Get list of all processed scans
                        var identifiers = await _scanStorage.ListIdentifiersAsync();

                        foreach (var id in identifiers)
                        {
                            try
                            {
                                var scan = await _scanStorage.LoadAsync(id);

                                // Check if this scan matches our platform and order number
                                if (scan != null &&
                                    scan.Platform.Equals(platform.platformId, StringComparison.OrdinalIgnoreCase) &&
                                    scan.OrderNumber != null &&
                                    scan.OrderNumber.Equals(orderNumber, StringComparison.OrdinalIgnoreCase) &&
                                    scan.ScanTime >= cutoffDate)
                                {
                                    _logger.LogInformation("Found existing scan in database from {ScanTime} with order number {OrderNumber}",
                                        scan.ScanTime, scan.OrderNumber);
                                    return true;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error loading scan {Id}", id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error checking scan database for duplicate orders");
                    }
                }

                _logger.LogInformation("Order {OrderNumber} for platform {Platform} has not been processed before",
                    orderNumber, platform.platformId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for processed orders");
                // In case of errors, assume it's not a duplicate to be safe
                return false;
            }
        }

        private async Task MovePdfToBackupAsync(string pdfPath, string platform, string? orderNumber)
        {
            try
            {
                if (!Directory.Exists(_backupFolder))
                {
                    await Task.Run(() => Directory.CreateDirectory(_backupFolder));
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{timestamp}_{platform}_{orderNumber ?? "unknown"}_{Path.GetFileName(pdfPath)}";
                string backupPath = Path.Combine(_backupFolder, fileName);

                using (var sourceStream = File.OpenRead(pdfPath))
                using (var destinationStream = File.Create(backupPath))
                {
                    await sourceStream.CopyToAsync(destinationStream);
                }

                await Task.Run(() => File.Delete(pdfPath));
                _logger.LogInformation("Moved processed file to backup: {File}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moving PDF to backup");
                throw;
            }
        }
    }
}