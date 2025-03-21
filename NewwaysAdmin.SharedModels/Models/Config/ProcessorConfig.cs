﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.IO.Manager;

namespace NewwaysAdmin.SharedModels.Config
{
    public class ProcessorConfig
    {
        [JsonProperty("version")]
        public string Version { get; set; } = "1.0";

        [JsonProperty("platforms")]
        public Dictionary<string, PlatformConfig> Platforms { get; set; } = new();

        [JsonProperty("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [JsonProperty("updatedBy")]
        public string UpdatedBy { get; set; } = string.Empty;
    }

    // This is the local processing state that doesn't get shared
    public class ProcessorLocalState
    {
        [JsonProperty("machineName")]
        public string MachineName { get; set; } = string.Empty;

        [JsonProperty("lastProcessedDate")]
        public DateTime LastProcessedDate { get; set; }

        [JsonProperty("processedFiles")]
        public List<ProcessedFileInfo> ProcessedFiles { get; set; } = new();
    }

    public class ProcessedFileInfo
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonProperty("processedDate")]
        public DateTime ProcessedDate { get; set; }

        [JsonProperty("backupPath")]
        public string BackupPath { get; set; } = string.Empty;

        [JsonProperty("orderNumber")]
        public string? OrderNumber { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; } = string.Empty;
    }

    public class ProcessorSettings
    {
        [JsonProperty("pdfWatchFolder")]
        public string PdfWatchFolder { get; set; } = @"C:\PDFtemp";

        [JsonProperty("backupFolder")]
        public string BackupFolder { get; set; } = @"C:\PDFtemp\PDFbackup";

        [JsonProperty("logFile")]
        public string LogFile { get; set; } = @"C:\PDFtemp\log.txt";

        [JsonProperty("lastProcessedDate")]
        public DateTime LastProcessedDate { get; set; }
    }

    public class PlatformConfig
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonProperty("identifiers")]
        public List<string> Identifiers { get; set; } = new();

        [JsonProperty("skus")]
        public Dictionary<string, SkuConfig> Skus { get; set; } = new();

        [JsonProperty("orderNumberPattern")]
        public string OrderNumberPattern { get; set; } = "";

        [JsonProperty("courierPatterns")]
        public Dictionary<string, string> CourierPatterns { get; set; } = new();
    }

    public class SkuConfig
    {
        [JsonProperty("pattern")]
        public string Pattern { get; set; } = "";

        [JsonProperty("productName")]
        public string ProductName { get; set; } = "";

        [JsonProperty("productDescription")]
        public string ProductDescription { get; set; } = "";

        [JsonProperty("packSize")]
        public int PackSize { get; set; }
    }

    public class ConfigProvider
    {
        private readonly ILogger _logger;
        private readonly IOManager _ioManager;
        private IDataStorage<ProcessorConfig>? _configStorage;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented
        };

        // Updated constructor to use IOManager
        public ConfigProvider(
            ILogger logger,
            IOManager ioManager)
        {
            _logger = logger;
            _ioManager = ioManager;
        }

        private async Task EnsureStorageInitializedAsync()
        {
            if (_configStorage != null) return;
            await _initLock.WaitAsync();
            try
            {
                if (_configStorage == null)
                {
                    _configStorage = await _ioManager.GetStorageAsync<ProcessorConfig>("PdfProcessor_Config");
                    _logger.LogInformation("Initialized configuration storage with identifier 'PdfProcessor_Config'");

                    // Add diagnostic info about the storage path
                    try
                    {
                        var storageType = _configStorage.GetType();
                        var optionsField = storageType.GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (optionsField != null)
                        {
                            var options = optionsField.GetValue(_configStorage);
                            var basePathProp = options.GetType().GetProperty("BasePath");
                            if (basePathProp != null)
                            {
                                var basePath = basePathProp.GetValue(options) as string;
                                _logger.LogInformation("Storage base path: {BasePath}", basePath);
                                // Check if directory exists and is accessible
                                if (Directory.Exists(basePath))
                                {
                                    _logger.LogInformation("Directory exists and is accessible");
                                    // List files in the directory
                                    var files = Directory.GetFiles(basePath, "*.*").Select(Path.GetFileName);
                                    _logger.LogInformation("Files in directory: {Files}", string.Join(", ", files));
                                }
                                else
                                {
                                    _logger.LogWarning("Directory does not exist or is not accessible: {BasePath}", basePath);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Unable to get storage path information");
                    }
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        public async Task<ProcessorConfig> LoadAsync()
        {
            try
            {
                await EnsureStorageInitializedAsync();

                if (_configStorage == null)
                    throw new InvalidOperationException("Config storage not initialized");

                // List available files to diagnose the issue
                var identifiers = await _configStorage.ListIdentifiersAsync();
                _logger.LogInformation("Available configuration files: {Files}",
                    string.Join(", ", identifiers));

                // Try multiple variations of the filename
                string[] possibleFiles = { "platforms", "platform", "Platforms", "Platform" };

                foreach (var file in possibleFiles)
                {
                    if (await _configStorage.ExistsAsync(file))
                    {
                        _logger.LogInformation("Found configuration file: {FileName}", file);
                        var config = await _configStorage.LoadAsync(file);

                        if (config != null && config.Platforms != null && config.Platforms.Count > 0)
                        {
                            _logger.LogInformation("Loaded configuration with {Count} platforms: {PlatformNames}",
                                config.Platforms.Count,
                                string.Join(", ", config.Platforms.Keys));
                            return config;
                        }
                        else
                        {
                            _logger.LogWarning("Configuration file {FileName} exists but is empty or invalid", file);
                        }
                    }
                }

                _logger.LogInformation("No valid configuration found. Creating default config.");
                var defaultConfig = CreateDefaultConfig();
                await SaveAsync(defaultConfig);
                return defaultConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration: {Message}", ex.Message);
                throw;
            }
        }

        public async Task SaveAsync(ProcessorConfig config)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                if (_configStorage == null)
                    throw new InvalidOperationException("Config storage not initialized");

                ArgumentNullException.ThrowIfNull(config);

                // Save with the identifier "platforms" (plural) to match what PdfProcessor is looking for
                await _configStorage.SaveAsync("platforms", config);
                _logger.LogInformation("Configuration saved successfully as 'platforms'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration: {Message}", ex.Message);
                throw;
            }
        }

        // CreateDefaultConfig remains unchanged
        private ProcessorConfig CreateDefaultConfig()
        {
            return new ProcessorConfig
            {
                Version = "1.0",
                LastUpdated = DateTime.UtcNow,
                UpdatedBy = Environment.MachineName,
                Platforms = new Dictionary<string, PlatformConfig>()
            };
        }

        // ValidateConfig remains unchanged
        public bool ValidateConfig(ProcessorConfig config)
        {
            if (config == null) return false;
            if (config.Platforms == null) return false;

            foreach (var platform in config.Platforms)
            {
                if (string.IsNullOrEmpty(platform.Key)) return false;
                if (platform.Value.Skus == null) return false;

                foreach (var sku in platform.Value.Skus)
                {
                    if (string.IsNullOrEmpty(sku.Key)) return false;
                    if (string.IsNullOrEmpty(sku.Value.Pattern)) return false;
                }
            }

            return true;
        }
    }
}