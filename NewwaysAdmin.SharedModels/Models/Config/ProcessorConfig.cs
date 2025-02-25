using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace NewwaysAdmin.SharedModels.Config
{
    public class ProcessorConfig
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("platforms")]
        public Dictionary<string, PlatformConfig> Platforms { get; set; } = new();

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("updatedBy")]
        public string UpdatedBy { get; set; } = string.Empty;
    }

    // This is the local processing state that doesn't get shared
    public class ProcessorLocalState
    {
        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = string.Empty;

        [JsonPropertyName("lastProcessedDate")]
        public DateTime LastProcessedDate { get; set; }

        [JsonPropertyName("processedFiles")]
        public List<ProcessedFileInfo> ProcessedFiles { get; set; } = new();
    }

    public class ProcessedFileInfo
    {
        [JsonPropertyName("fileName")]
        public string FileName { get; set; } = string.Empty;

        [JsonPropertyName("processedDate")]
        public DateTime ProcessedDate { get; set; }

        [JsonPropertyName("backupPath")]
        public string BackupPath { get; set; } = string.Empty;

        [JsonPropertyName("orderNumber")]
        public string? OrderNumber { get; set; }

        [JsonPropertyName("platform")]
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
        private const string ConfigPath = @"C:\NewwaysCore\config\platform-config.json";
        private readonly ILogger _logger;
        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            Formatting = Formatting.Indented
        };

        public ConfigProvider(ILogger logger)
        {
            _logger = logger;
            EnsureConfigDirectoryExists();
        }

        private void EnsureConfigDirectoryExists()
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }
        }

        public async Task<ProcessorConfig> LoadAsync()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    _logger.LogInformation("Config file not found. Creating default config.");
                    var defaultConfig = CreateDefaultConfig();
                    await SaveAsync(defaultConfig);
                    return defaultConfig;
                }

                var json = await File.ReadAllTextAsync(ConfigPath);
                var config = JsonConvert.DeserializeObject<ProcessorConfig>(json, _jsonSettings);

                if (config == null)
                {
                    throw new InvalidOperationException("Failed to deserialize configuration");
                }

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration");
                throw;
            }
        }

        public async Task SaveAsync(ProcessorConfig config)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(config);
                var json = JsonConvert.SerializeObject(config, _jsonSettings);
                await File.WriteAllTextAsync(ConfigPath, json);
                _logger.LogInformation("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration");
                throw;
            }
        }

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
    }
}