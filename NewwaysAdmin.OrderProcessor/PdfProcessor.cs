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
                    _scanStorage = await _ioManager.GetStorageAsync<ScanResult>("PDFProcessor_Scans"); // Changed from "Scans"
                    _logger.LogDebug("Initialized scan storage");
                }
                if (_configStorage == null)
                {
                    _configStorage = await _ioManager.GetStorageAsync<ProcessorConfig>("PDFProcessor_Config"); // Changed from "Config"
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

                // Debug logs for file existence and size
                _logger.LogInformation("PDF file exists: {Exists}", File.Exists(pdfPath));
                _logger.LogInformation("PDF file size: {Size} bytes", new FileInfo(pdfPath).Length);

                // Initialize platformConfig as null before diagnostic code
                ProcessorConfig platformConfig = null;
                var directConfigFound = false;

                // DIAGNOSTIC CODE START
                string configDirectoryPath = Path.Combine("C:/NewwaysData/Config/PDFProcessor");
                _logger.LogInformation("Checking config directory: {Path}", configDirectoryPath);

                if (Directory.Exists(configDirectoryPath))
                {
                    _logger.LogInformation("Config directory exists");
                    var files = Directory.GetFiles(configDirectoryPath, "*.json");
                    _logger.LogInformation("JSON files in config directory: {Files}", string.Join(", ", files.Select(Path.GetFileName)));

                    string platformsFilePath = Path.Combine(configDirectoryPath, "platforms.json");
                    if (File.Exists(platformsFilePath))
                    {
                        _logger.LogInformation("platforms.json exists, size: {Size} bytes", new FileInfo(platformsFilePath).Length);

                        try
                        {
                            string rawContent = File.ReadAllText(platformsFilePath);
                            _logger.LogInformation("Raw file content (first 100 chars): {Content}",
                                rawContent.Length > 100 ? rawContent.Substring(0, 100) : rawContent);

                            // Try direct deserialization with Newtonsoft
                            var directConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<ProcessorConfig>(
                                rawContent,
                                new Newtonsoft.Json.JsonSerializerSettings
                                {
                                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                                });

                            _logger.LogInformation("Direct deserialization result: nullObject={IsNull}, platforms=null={PlatformsNull}, platformsCount={Count}",
                                directConfig == null,
                                directConfig?.Platforms == null,
                                directConfig?.Platforms?.Count ?? 0);

                            if (directConfig?.Platforms != null && directConfig.Platforms.Count > 0)
                            {
                                foreach (var platformKey in directConfig.Platforms.Keys)
                                {
                                    _logger.LogInformation("Platform found: {Key}", platformKey);
                                }

                                // If direct deserialization works, store it for later use
                                platformConfig = directConfig;
                                directConfigFound = true;
                                _logger.LogInformation("Successfully deserialized config directly from file");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error directly reading/parsing platforms.json");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("platforms.json file does not exist at: {Path}", platformsFilePath);
                        // Try searching for it with different casing
                        foreach (var file in files)
                        {
                            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                                Path.GetFileNameWithoutExtension(file).Equals("platforms", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Found potential platforms file with different casing: {File}", Path.GetFileName(file));
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Config directory does not exist: {Path}", configDirectoryPath);
                }
                // DIAGNOSTIC CODE END

                // Only attempt to load from storage if direct loading didn't work
                if (!directConfigFound)
                {
                    try
                    {
                        _logger.LogInformation("Attempting to load platforms configuration from storage");

                        // List identifiers before loading
                        var identifiers = await _configStorage.ListIdentifiersAsync();
                        _logger.LogInformation("Available identifiers: {Identifiers}", string.Join(", ", identifiers));

                        platformConfig = await _configStorage.LoadAsync("platforms");

                        _logger.LogInformation("Platforms config loaded successfully: {Count} platforms defined",
                            platformConfig?.Platforms?.Count ?? 0);

                        // Log all properties from the loaded object
                        _logger.LogInformation("Loaded config: Version={Version}, UpdatedBy={UpdatedBy}, LastUpdated={LastUpdated}",
                            platformConfig?.Version,
                            platformConfig?.UpdatedBy,
                            platformConfig?.LastUpdated);

                        if (platformConfig == null)
                        {
                            _logger.LogWarning("No platforms configuration found, creating default configuration");
                            platformConfig = CreateDefaultConfig();
                            await _configStorage.SaveAsync("platforms", platformConfig);

                            // Check if save worked
                            var savedConfig = await _configStorage.LoadAsync("platforms");
                            _logger.LogInformation("After save, loaded platforms count: {Count}", savedConfig?.Platforms?.Count ?? 0);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading platforms configuration");
                        throw;
                    }
                }

                _logger.LogInformation("Extracting text from PDF");
                string normalizedText = await ExtractAndNormalizeTextAsync(pdfPath);
                _logger.LogInformation("Text extraction complete, first 100 chars: {TextSample}",
                    normalizedText.Length > 100 ? normalizedText.Substring(0, 100) : normalizedText);

                // DIAGNOSTIC CODE: Test if reloading gives different results
                if (platformConfig?.Platforms?.Count == 0)
                {
                    var reloadedConfig = await SafeLoadConfigAsync("platforms");
                    _logger.LogInformation("Reloaded config platforms count: {Count}", reloadedConfig?.Platforms?.Count ?? 0);

                    // Compare loaded configs
                    if (reloadedConfig?.Platforms?.Count > 0)
                    {
                        _logger.LogInformation("Using reloaded config which has {Count} platforms", reloadedConfig.Platforms.Count);
                        platformConfig = reloadedConfig;
                    }
                    else
                    {
                        // Try loading directly from different file names
                        foreach (var identifier in new[] { "platform", "Platforms", "Platform" })
                        {
                            try
                            {
                                _logger.LogInformation("Trying to load from alternate identifier: {Identifier}", identifier);
                                var alternateConfig = await _configStorage.LoadAsync(identifier);
                                if (alternateConfig?.Platforms?.Count > 0)
                                {
                                    _logger.LogInformation("Found {Count} platforms in identifier: {Identifier}",
                                        alternateConfig.Platforms.Count, identifier);
                                    platformConfig = alternateConfig;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to load from alternate identifier: {Identifier}", identifier);
                            }
                        }
                    }
                }

                // As a fallback, create a default config with some platforms
                if (platformConfig == null || platformConfig.Platforms == null || platformConfig.Platforms.Count == 0)
                {
                    _logger.LogWarning("No platforms configuration could be loaded, using default");
                    platformConfig = CreateDefaultConfig();
                }

                DebugPlatformConfig(platformConfig, normalizedText);

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
                    await MovePdfToBackupAsync(pdfPath, platform.Value.platformId, orderNumber);
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

        // Add this new method to create a sample configuration with platforms
        private ProcessorConfig CreateDefaultConfigWithSamplePlatforms()
        {
            var config = new ProcessorConfig
            {
                Version = "1.0",
                LastUpdated = DateTime.UtcNow,
                UpdatedBy = "Emergency Default Creator",
                Platforms = new Dictionary<string, PlatformConfig>()
            };

            // Add sample platforms
            config.Platforms.Add("tiktok", new PlatformConfig
            {
                Name = "TikTok",
                Enabled = true,
                Identifiers = new List<string> { "Order ID:" },
                OrderNumberPattern = "Order ID: (\\d+)",
                Skus = new Dictionary<string, SkuConfig>
                {
                    ["SKU1"] = new SkuConfig
                    {
                        Pattern = "1\\s*ถ\\s*[\\u0E31-\\u0E4E]*\\s*ง[\\u0E31-\\u0E4E]*\\s*(\\d+)",
                        ProductName = "Package Type 1",
                        ProductDescription = "Small package",
                        PackSize = 1
                    }
                }
            });

            config.Platforms.Add("shopee", new PlatformConfig
            {
                Name = "Shopee",
                Enabled = true,
                Identifiers = new List<string> { "Shopee Order No" },
                OrderNumberPattern = "Shopee Order No\\. ([A-Z0-9]+)",
                Skus = new Dictionary<string, SkuConfig>
                {
                    ["SKU1"] = new SkuConfig
                    {
                        Pattern = "1\\s*ถง\\s*(\\d+)",
                        ProductName = "Package Type 1",
                        ProductDescription = "Small package",
                        PackSize = 1
                    }
                }
            });

            return config;
        }
        private async Task<ProcessorConfig> SafeLoadConfigAsync(string identifier)
        {
            try
            {
                if (!await _configStorage.ExistsAsync(identifier))
                {
                    _logger.LogWarning("Configuration file '{Identifier}' does not exist", identifier);
                    return null;
                }

                var config = await _configStorage.LoadAsync(identifier);
                if (config == null)
                {
                    _logger.LogWarning("Configuration loaded as null from '{Identifier}'", identifier);
                    return null;
                }

                if (config.Platforms == null)
                {
                    _logger.LogWarning("Configuration loaded successfully but Platforms property is null");
                    config.Platforms = new Dictionary<string, PlatformConfig>();
                }

                _logger.LogInformation("Config loaded successfully with {Count} platforms", config.Platforms.Count);
                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration '{Identifier}'", identifier);
                return null;
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
            foreach (var platform in config.Platforms)
            {
                if (!platform.Value.Enabled) continue;

                if (platform.Value.Identifiers.Any(id => normalizedText.Contains(id)))
                {
                    return (platform.Key, platform.Value);
                }
            }
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
                var cutoffDate = DateTime.Today.AddDays(-2);

                var recentFiles = Directory.GetFiles(_backupFolder)
                    .Where(f => File.GetCreationTime(f) >= cutoffDate);

                foreach (var file in recentFiles)
                {
                    using var stream = File.OpenRead(file);
                    using var reader = new PdfReader(stream);
                    using var document = new PdfDocument(reader);

                    var normalizedText = await Task.Run(() =>
                    {
                        var strategy = new LocationTextExtractionStrategy();
                        var pageText = PdfTextExtractor.GetTextFromPage(document.GetPage(1), strategy);
                        return NormalizeText(pageText);
                    });

                    if (!platform.config.Identifiers.Any(id => normalizedText.Contains(id)))
                        continue;

                    var match = Regex.Match(normalizedText, platform.config.OrderNumberPattern);
                    if (match.Success && match.Groups[1].Value == orderNumber)
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for processed file");
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

        private void DebugPlatformConfig(ProcessorConfig config, string text)
        {
            if (config == null || config.Platforms == null || config.Platforms.Count == 0)
            {
                _logger.LogWarning("PlatformConfig is null, empty, or has no platforms defined");
                return;
            }

            _logger.LogInformation("Platform config contains {Count} platforms: {Platforms}",
                config.Platforms.Count,
                string.Join(", ", config.Platforms.Keys));

            foreach (var platform in config.Platforms)
            {
                bool matchesAnyIdentifier = platform.Value.Identifiers.Any(id =>
                    text.Contains(id, StringComparison.OrdinalIgnoreCase));

                _logger.LogInformation("Platform {Name} matches identifiers: {Matches}",
                    platform.Key,
                    matchesAnyIdentifier);

                if (!string.IsNullOrEmpty(platform.Value.OrderNumberPattern))
                {
                    try
                    {
                        var match = Regex.Match(text, platform.Value.OrderNumberPattern);
                        _logger.LogInformation("Order number pattern match: {Success}, Value: {Value}",
                            match.Success,
                            match.Success ? match.Groups[1].Value : "none");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error with regex pattern: {Pattern}", platform.Value.OrderNumberPattern);
                    }
                }
            }
        }

        private ProcessorConfig CreateDefaultConfig()
        {
            return new ProcessorConfig
            {
                Platforms = new Dictionary<string, PlatformConfig>(),
                Version = "1.0",
                LastUpdated = DateTime.UtcNow,
                UpdatedBy = Environment.MachineName
            };
        }
    }
}