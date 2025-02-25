using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace NewwaysAdmin.Shared.Configuration
{
    public class MachineConfig
    {
        public string MachineName { get; set; } = "UNKNOWN";
        public string MachineRole { get; set; } = "UNKNOWN";
        public Dictionary<string, AppConfig> Apps { get; set; } = new();
    }

    public class AppConfig
    {
        public string Version { get; set; } = "1.0";
        public Dictionary<string, string> LocalPaths { get; set; } = new();
        public Dictionary<string, string> Settings { get; set; } = new();
    }

    public class MachineConfigProvider
    {
        private const string DEFAULT_CONFIG_PATH = @"C:\NewwaysAdmin\Machine\machine-config.json";
        private readonly ILogger<MachineConfigProvider> _logger;
        private readonly string _configPath;

        public MachineConfigProvider(ILogger<MachineConfigProvider> logger, string? providedPath = null)
        {
            _logger = logger;
            _configPath = providedPath ?? DEFAULT_CONFIG_PATH;
        }

        public async Task<MachineConfig> LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    var defaultConfig = CreateDefaultConfig();
                    await SaveConfigAsync(defaultConfig);
                    return defaultConfig;
                }

                var json = await File.ReadAllTextAsync(_configPath);
                var loadedConfig = JsonSerializer.Deserialize<MachineConfig>(json);
                return loadedConfig ?? CreateDefaultConfig();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load machine configuration");
                return CreateDefaultConfig();
            }
        }

        private async Task SaveConfigAsync(MachineConfig machineConfig)
        {
            try
            {
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(machineConfig, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                await File.WriteAllTextAsync(_configPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save machine configuration");
                throw;
            }
        }

        private MachineConfig CreateDefaultConfig()
        {
            return new MachineConfig
            {
                MachineName = Environment.MachineName,
                MachineRole = "WORKSTATION",
                Apps = new Dictionary<string, AppConfig>
                {
                    ["PDFProcessor"] = new AppConfig
                    {
                        LocalPaths = new Dictionary<string, string>
                        {
                            ["WatchFolder"] = @"C:\PDFtemp",
                            ["BackupFolder"] = @"C:\PDFtemp\PDFbackup",
                            ["LogFile"] = @"C:\PDFtemp\log.txt"
                        }
                    }
                }
            };
        }
    }
}