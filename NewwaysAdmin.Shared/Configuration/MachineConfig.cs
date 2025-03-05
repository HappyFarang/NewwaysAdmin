using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace NewwaysAdmin.Shared.Configuration
{
    public class MachineConfig
    {
        public string MachineName { get; set; } = "UNKNOWN";
        public string MachineRole { get; set; } = "UNKNOWN";
        public string LocalBaseFolder { get; set; } = "C:/NewwaysData";
        public string ServerBasePath { get; set; } = "X:/NewwaysAdmin";
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
        private const string DEFAULT_CONFIG_PATH = @"C:\MachineConfig\machine.json";
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
                    _logger.LogError("Machine configuration file not found at {Path}", _configPath);
                    throw new FileNotFoundException($"Machine configuration file not found at {_configPath}");
                }

                var json = await File.ReadAllTextAsync(_configPath);
                var loadedConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<MachineConfig>(json);

                if (loadedConfig == null)
                {
                    _logger.LogError("Failed to deserialize machine configuration");
                    throw new InvalidOperationException("Failed to deserialize machine configuration");
                }

                return loadedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load machine configuration");
                throw; // Let the caller handle this - it's a critical configuration
            }
        }
    }
}