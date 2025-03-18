using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewwaysAdmin.OrderProcessor
{
    public class ScanResult
    {
        public string Id { get; set; } = string.Empty;

        [JsonConverter(typeof(CustomDateTimeConverter))]
        public DateTime ScanTime { get; set; }

        public string Platform { get; set; } = string.Empty;
        public string? OrderNumber { get; set; }
        public Dictionary<string, int> SkuCounts { get; set; } = new();
        public string OriginalFileName { get; set; } = string.Empty;
        public string? Courier { get; set; }
        public int OrderCount { get; set; } = 1;

        // Product tracking fields - using more generic terminology
        public int TotalItems { get; set; } = 0;
        public Dictionary<string, int> ProductCount { get; set; } = new();

        // Existing fields for enhanced analysis
        public List<OrderData> OrderDetails { get; set; } = new();
        public Dictionary<string, int> CourierCounts { get; set; } = new();
        public List<UnusualSkuOrder> UnusualOrders { get; set; } = new();
    }

    public class CustomDateTimeConverter : IsoDateTimeConverter
    {
        public CustomDateTimeConverter()
        {
            DateTimeFormat = "yyyy-MM-ddTHH:mm:ss";
        }
    }

public class PrinterConfig
    {
        public bool EnablePrinting { get; set; }
        public string DefaultPrinterName { get; set; } = string.Empty;
        public Dictionary<string, string> PrinterMappings { get; set; } = new();
        public bool SimulatePrinting { get; set; }
    }

    public class PrinterManager
    {
        private readonly IOManager _ioManager;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<PrinterConfig>? _configStorage;
        private PrinterConfig? _currentConfig;

        public PrinterManager(IOManager ioManager, ILogger logger)
        {
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task EnsureInitializedAsync()
        {
            if (_configStorage != null && _currentConfig != null)
                return;

            await _initLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (_configStorage == null)
                { 
                    _configStorage = await _ioManager.GetStorageAsync<PrinterConfig>("PdfProcessor_Config");
                }

                if (_currentConfig == null)
                {
                    _currentConfig = await LoadConfigurationAsync();
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task<PrinterConfig> LoadConfigurationAsync()
        {
            if (_configStorage == null)
                throw new InvalidOperationException("Storage not initialized");

            try
            {
                var config = await _configStorage.LoadAsync("printer-config");
                if (config != null)
                    return config;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load printer configuration, using defaults");
            }

            // Default configuration
            return new PrinterConfig
            {
                EnablePrinting = false,
                DefaultPrinterName = "LABEL",
                PrinterMappings = new Dictionary<string, string>
            {
                { "label", "LABEL" },
                { "document", "DEFAULT" }
            },
                SimulatePrinting = true
            };
        }

        public static class PrinterHelper
        {
            public static Task PrintPdfAsync(string pdfPath, string printerName)
            {
                try
                {
                    // Validate inputs
                    if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath))
                    {
                        throw new ArgumentException("Invalid PDF file path.");
                    }
                    if (string.IsNullOrEmpty(printerName))
                    {
                        throw new ArgumentException("Printer name cannot be empty.");
                    }

                    // Use ProcessStartInfo to launch a print job silently
                    var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = pdfPath,
                            Verb = "printto",
                            Arguments = $"\"{printerName}\"",
                            CreateNoWindow = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                            UseShellExecute = true
                        }
                    };

                    process.Start();
                    if (!process.WaitForExit(5000)) // Wait up to 5 seconds
                    {
                        process.Kill();
                        throw new TimeoutException("Printing timed out after 5 seconds.");
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new Exception($"Print process failed with exit code {process.ExitCode}.");
                    }

                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to print PDF: {ex.Message}", ex);
                }
            }
        }

        public async Task<bool> PrintPdfAsync(string pdfPath, string printerType = "label")
        {
            await EnsureInitializedAsync();

            if (_currentConfig == null)
                throw new InvalidOperationException("Configuration not loaded");

            if (!_currentConfig.EnablePrinting)
            {
                _logger.LogInformation("Printing is disabled in configuration");
                return true;
            }

            if (_currentConfig.SimulatePrinting)
            {
                _logger.LogInformation($"SIMULATION: Would print {pdfPath} to {printerType} printer");
                return true;
            }

            try
            {
                if (!_currentConfig.PrinterMappings.TryGetValue(printerType.ToLower(), out var printerName))
                {
                    printerName = _currentConfig.DefaultPrinterName;
                }

                if (string.IsNullOrEmpty(printerName))
                {
                    throw new InvalidOperationException($"No printer configured for type: {printerType}");
                }

                await PrinterHelper.PrintPdfAsync(pdfPath, printerName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to print PDF {pdfPath}");
                return false;
            }
        }
    }
}