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
        // need to update the way we process the PDF files. Each page is 1 order. But we need to keep each PDF file resoult in one scan resoult
        // so scan resoults can contain multiple orders but with collective summary of all orders in the PDF file.
        private readonly IOManager _ioManager;
        private readonly ILogger<PdfProcessor> _logger;
        private readonly PrinterManager _printerManager;
        private readonly string _backupFolder;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<ScanResult>? _scanStorage;
        private IDataStorage<ProcessorConfig>? _configStorage;

        public PdfProcessor(
            IOManager ioManager,
            string backupFolderOld,
            ILogger<PdfProcessor> logger)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _backupFolder = "C:/PDFtemp/PDFbackup";
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
                    _scanStorage = await _ioManager.GetStorageAsync<ScanResult>("PdfProcessor_Scans");
                    _logger.LogDebug("Initialized scan storage");
                }
                if (_configStorage == null)
                {
                    _configStorage = await _ioManager.GetStorageAsync<ProcessorConfig>("PdfProcessor_Config");
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

                // Configuration loading code (keeping as is)
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

                        // ... (rest of config loading code)
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

                    // ... (rest of config loading code)

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

                // Use our new analysis method
                var scanResult = await AnalyzePdfContentAsync(pdfPath, originalFileName, platformConfig);

                if (scanResult == null || string.IsNullOrEmpty(scanResult.Platform) || scanResult.Platform == "UNKNOWN")
                {
                    _logger.LogWarning("Could not analyze PDF: {File}", originalFileName);
                    return;
                }

                // Check if any of the orders have been processed before
                bool isDuplicate = false;
                foreach (var order in scanResult.OrderDetails)
                {
                    if (order.OrderNumber != null &&
                        await IsOrderProcessedAsync((scanResult.Platform, platformConfig.Platforms[scanResult.Platform]), order.OrderNumber))
                    {
                        _logger.LogInformation("Order {OrderNumber} already processed, skipping entire file", order.OrderNumber);
                        isDuplicate = true;
                        break;
                    }
                }

                if (isDuplicate)
                {
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

                // Save the scan result directly - no conversion needed anymore
                await _scanStorage.SaveAsync(scanResult.Id, scanResult);
                _logger.LogInformation("Saved scan result locally with ID: {ScanId}", scanResult.Id);

                // Queue for transfer to server 
                try
                {
                    // The path where scan results are stored
                    string scanFolderPath = "PdfProcessor/Scans";
                    await _ioManager.QueueForServerTransferAsync(scanFolderPath, scanResult.Id, scanResult);
                    _logger.LogInformation("Queued scan result for server transfer: {ScanId}", scanResult.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not queue scan for server transfer, but local save was successful");
                }

                // Move the PDF file to backup
                await MovePdfToBackupAsync(pdfPath, scanResult.Platform,
                    scanResult.OrderDetails.FirstOrDefault()?.OrderNumber);

                // Print if configured
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

                // If we have any matches at all, we assume by default it's ONE item
                // unless a quantity is explicitly specified in the pattern
                if (matches.Count > 0)
                {
                    _logger.LogDebug($"Found {matches.Count} matches for pattern '{sku.Value.Pattern}' for SKU '{sku.Key}'");

                    // Check if the pattern has a quantity capture group
                    bool hasQuantityCapture = false;
                    int totalQuantity = 0;

                    foreach (Match m in matches)
                    {
                        // Check if this match has a quantity group (Group 1)
                        if (m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                        {
                            hasQuantityCapture = true;
                            if (int.TryParse(m.Groups[1].Value, out int qty))
                            {
                                totalQuantity += qty;
                                _logger.LogDebug($"Captured quantity {qty} for SKU '{sku.Key}'");
                            }
                        }
                    }

                    // If no quantity was captured in any match, use match count = 1
                    // We're treating multiple pattern matches as still just ONE item unless 
                    // specifically captured in the regex
                    if (!hasQuantityCapture)
                    {
                        _logger.LogDebug($"No quantity captured for SKU '{sku.Key}', assuming quantity 1");
                        totalQuantity = 1; // Default to ONE item only, regardless of match count
                    }

                    skuCounts[sku.Key] = totalQuantity;
                    _logger.LogInformation($"Extracted SKU '{sku.Key}' with quantity {totalQuantity}");
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

        private string? ExtractCourier(string text, (string platformId, PlatformConfig config) platform)
        {
            if (platform.config.CourierPatterns == null || !platform.config.CourierPatterns.Any())
                return null;

            foreach (var courierEntry in platform.config.CourierPatterns)
            {
                string courierName = courierEntry.Key;
                string pattern = courierEntry.Value;

                if (string.IsNullOrEmpty(pattern))
                    continue;

                var match = Regex.Match(text, pattern);
                if (match.Success)
                {
                    // Return either the captured group or the courier name if no capture group
                    return match.Groups.Count > 1 ? match.Groups[1].Value : courierName;
                }
            }

            return null;
        }

        private async Task MovePdfToBackupAsync(string pdfPath, string platform, string? orderNumber)
        {
            try
            {
                // Use the direct path without Path.Combine
                string backupFolder = "C:/PDFtemp/PDFbackup";

                if (!Directory.Exists(backupFolder))
                {
                    await Task.Run(() => Directory.CreateDirectory(backupFolder));
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{timestamp}_{platform}_{orderNumber ?? "unknown"}_{Path.GetFileName(pdfPath)}";

                // Use raw path with simple string concatenation for clarity
                string backupPath = backupFolder + "/" + fileName;

                _logger.LogDebug("Moving PDF from {SourcePath} to {BackupPath}", pdfPath, backupPath);

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

        // Helper method to convert PdfAnalysisResult to ScanResult for backward compatibility
        private ScanResult ConvertToScanResult(PdfAnalysisResult analysis)
        {
            // Get the first order's data (or empty values if no orders)
            var firstOrder = analysis.Orders.FirstOrDefault();
            string? orderNumber = firstOrder?.OrderNumber;
            string? courier = firstOrder?.Courier;

            // Create a ScanResult instance with aggregated data
            var result = new ScanResult
            {
                Id = analysis.Id,
                ScanTime = analysis.ScanTime,
                Platform = analysis.Platform,
                OrderNumber = orderNumber,
                SkuCounts = analysis.TotalSkuCounts,
                OriginalFileName = analysis.OriginalFileName,
                Courier = courier,
                // Add a new property to track order count
                OrderCount = analysis.Orders.Count
            };

            return result;
        }


        private async Task<ScanResult> AnalyzePdfContentAsync(
    string pdfPath,
    string originalFileName,
    ProcessorConfig platformConfig)
        {
            // Create a new scan result
            string date = DateTime.Now.ToString("yyyyMMdd");
            string time = DateTime.Now.ToString("HHmmss");

            // Initialize the result object
            var result = new ScanResult
            {
                Id = $"[{date}][{time}]",  // We'll append platform later
                OriginalFileName = originalFileName,
                ScanTime = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day,
                                      DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second),
                OrderDetails = new List<OrderData>(),
                CourierCounts = new Dictionary<string, int>(),
                UnusualOrders = new List<UnusualSkuOrder>()
            };

            try
            {
                // Open the PDF for analysis
                using var stream = File.OpenRead(pdfPath);
                using var reader = new PdfReader(stream);
                using var document = new PdfDocument(reader);

                // Store the platform tuple once identified
                (string platformId, PlatformConfig config)? identifiedPlatform = null;

                // Extract and analyze each page as a separate order
                for (int pageNum = 1; pageNum <= document.GetNumberOfPages(); pageNum++)
                {
                    var strategy = new LocationTextExtractionStrategy();
                    var pageText = PdfTextExtractor.GetTextFromPage(document.GetPage(pageNum), strategy);
                    var normalizedText = NormalizeText(pageText);

                    // Identify platform (only need to do this once)
                    if (identifiedPlatform == null)
                    {
                        identifiedPlatform = IdentifyPlatform(normalizedText, platformConfig);
                        if (identifiedPlatform == null)
                        {
                            _logger.LogWarning("No platform identified for file {File}", originalFileName);
                            result.Platform = "UNKNOWN";
                            return result;
                        }

                        result.Platform = identifiedPlatform.Value.platformId;
                        result.Id = $"[{date}][{time}][{identifiedPlatform.Value.platformId}]";
                    }

                    // Process the page/order using our existing method
                    var orderData = ExtractOrderData(normalizedText, pageNum, identifiedPlatform.Value);

                    // Add to the order details collection
                    result.OrderDetails.Add(orderData);
                }

                // Set OrderCount
                result.OrderCount = result.OrderDetails.Count;

                // If there are orders, set the first order's details for backward compatibility
                if (result.OrderDetails.Count > 0)
                {
                    var firstOrder = result.OrderDetails[0];
                    result.OrderNumber = firstOrder.OrderNumber;
                    result.Courier = firstOrder.Courier;
                }

                /// Pass the specific PlatformConfig to AggregateAnalysisData
                if (identifiedPlatform != null)
                {
                    AggregateAnalysisData(result, identifiedPlatform.Value.config);
                }
                else
                {
                    AggregateAnalysisData(result, null); // Handle case where no platform is identified
                }

                return result;            
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing PDF: {File}", originalFileName);
                result.Platform = "ERROR";
                return result;
            }
        }

        private OrderData ExtractOrderData(string normalizedText, int pageNumber, (string platformId, PlatformConfig config) platform)
        {
            var orderData = new OrderData
            {
                PageNumber = pageNumber,
                OrderNumber = ExtractOrderNumber(normalizedText, platform),
                Courier = ExtractCourier(normalizedText, platform),
                SkuCounts = ExtractSkuCounts(normalizedText, platform)
            };

            return orderData;
        }

        private void AggregateAnalysisData(ScanResult result, PlatformConfig platformConfig)
        {
            // Reset aggregate collections
            result.SkuCounts = new Dictionary<string, int>();
            result.CourierCounts = new Dictionary<string, int>();
            result.UnusualOrders = new List<UnusualSkuOrder>();
            result.TotalItems = 0;
            result.ProductCount = new Dictionary<string, int>();

            // First pass: identify unusual orders and track couriers
            foreach (var order in result.OrderDetails)
            {
                // Check for unusual orders (quantity > 1)
                foreach (var skuCount in order.SkuCounts)
                {
                    if (skuCount.Value > 1)
                    {
                        result.UnusualOrders.Add(new UnusualSkuOrder
                        {
                            Sku = skuCount.Key,
                            Quantity = skuCount.Value,
                            OrderNumber = order.OrderNumber,
                            PageNumber = order.PageNumber
                        });
                    }
                }

                // Track couriers
                if (!string.IsNullOrEmpty(order.Courier))
                {
                    if (!result.CourierCounts.ContainsKey(order.Courier))
                        result.CourierCounts[order.Courier] = 0;

                    result.CourierCounts[order.Courier]++;
                }
            }

            // Second pass: aggregate regular SKU counts (excluding unusual orders)
            foreach (var order in result.OrderDetails)
            {
                foreach (var skuCount in order.SkuCounts)
                {
                    // Skip unusual orders (they're tracked separately)
                    if (skuCount.Value > 1)
                        continue;

                    // Add regular orders to SKU counts
                    if (!result.SkuCounts.ContainsKey(skuCount.Key))
                        result.SkuCounts[skuCount.Key] = 0;

                    result.SkuCounts[skuCount.Key] += skuCount.Value;
                }
            }

            // Calculate total items and product counts
            CalculateTotalItems(result, platformConfig);
        }
        private void CalculateTotalItems(ScanResult result, PlatformConfig platformConfig)
        {
            // Reset total items counter
            int totalItems = 0;

            // Process regular SKUs
            foreach (var skuEntry in result.SkuCounts)
            {
                string skuId = skuEntry.Key;
                int packCount = skuEntry.Value;

                // Look up the SKU configuration
                if (platformConfig?.Skus != null && platformConfig.Skus.TryGetValue(skuId, out var skuConfig))
                {
                    // Get product name and pack size
                    string productName = skuConfig.ProductName;
                    int packSize = skuConfig.PackSize > 0 ? skuConfig.PackSize : 1;

                    // Calculate items for this SKU
                    int itemsForThisSku = packCount * packSize;

                    // Add to product count dictionary
                    if (!result.ProductCount.ContainsKey(productName))
                        result.ProductCount[productName] = 0;

                    result.ProductCount[productName] += itemsForThisSku;

                    // Add to total items
                    totalItems += itemsForThisSku;

                    _logger.LogDebug($"Regular SKU {skuId}: {packCount} packages × {packSize} items/package = {itemsForThisSku} items");
                }
                else
                {
                    // If no config found, assume 1 item per package
                    if (!result.ProductCount.ContainsKey(skuId))
                        result.ProductCount[skuId] = 0;

                    result.ProductCount[skuId] += packCount;
                    totalItems += packCount;
                }
            }

            // Process unusual orders separately
            foreach (var unusual in result.UnusualOrders)
            {
                string skuId = unusual.Sku;
                int packCount = unusual.Quantity;

                // Look up the SKU configuration
                if (platformConfig?.Skus != null && platformConfig.Skus.TryGetValue(skuId, out var skuConfig))
                {
                    // Get product name and pack size
                    string productName = skuConfig.ProductName;
                    int packSize = skuConfig.PackSize > 0 ? skuConfig.PackSize : 1;

                    // Calculate items for this unusual order
                    int itemsForThisUnusual = packCount * packSize;

                    // Add to product count dictionary
                    if (!result.ProductCount.ContainsKey(productName))
                        result.ProductCount[productName] = 0;

                    result.ProductCount[productName] += itemsForThisUnusual;

                    // Add to total items
                    totalItems += itemsForThisUnusual;

                    _logger.LogDebug($"Unusual SKU {skuId}: {packCount} packages × {packSize} items/package = {itemsForThisUnusual} items");
                }
                else
                {
                    // If no config found, assume 1 item per package
                    if (!result.ProductCount.ContainsKey(skuId))
                        result.ProductCount[skuId] = 0;

                    result.ProductCount[skuId] += packCount;
                    totalItems += packCount;
                }
            }

            // Set the total items to our calculated value
            result.TotalItems = totalItems;

            _logger.LogInformation($"Calculated TotalItems: {totalItems} (from {result.SkuCounts.Count} regular SKUs and {result.UnusualOrders.Count} unusual orders)");

            // Log product breakdown
            foreach (var product in result.ProductCount)
            {
                _logger.LogInformation($"Product: {product.Key}, Items: {product.Value}");
            }
        }
    }
    // Main analysis result class
    public class PdfAnalysisResult
    {
        // Basic identification
        public string Id { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string Platform { get; set; } = string.Empty;

        // Page/order level data
        public List<OrderData> Orders { get; set; } = new();

        // Aggregated statistics
        public int TotalOrders => Orders.Count;
        public Dictionary<string, int> TotalSkuCounts { get; set; } = new();
        public Dictionary<string, int> CourierCounts { get; set; } = new();
        public List<UnusualSkuOrder> UnusualOrders { get; set; } = new();
    }

    

    // Represents a single order (page)
    public class OrderData
    {
        public int PageNumber { get; set; }
        public string? OrderNumber { get; set; }
        public string? Courier { get; set; }
        public Dictionary<string, int> SkuCounts { get; set; } = new();
    }

    // Represents an unusual SKU order (quantity > 1)
    public class UnusualSkuOrder
    {
        public string Sku { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? OrderNumber { get; set; }
        public int PageNumber { get; set; }
    }
}