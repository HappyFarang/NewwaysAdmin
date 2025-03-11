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
        private readonly ILogger<MachineConfigProvider> _logger;
        private readonly string[] _args;
        private const string DEFAULT_CONFIG_PATH = @"C:\MachineConfig\machine.json";
        private const string TEST_SERVER_CONFIG_PATH = @"C:\TestServer\machine.json";
        private const string TEST_CLIENT_CONFIG_PATH = @"C:\TestClient\machine.json";

        public MachineConfigProvider(ILogger<MachineConfigProvider> logger, string[]? args = null)
        {
            _logger = logger;
            _args = args ?? Array.Empty<string>();
        }

        public async Task<MachineConfig> LoadConfigAsync()
        {
            try
            {
                // Determine which config file to use based on arguments
                string configPath;

                if (_args.Contains("--testserver"))
                {
                    configPath = TEST_SERVER_CONFIG_PATH;
                    _logger.LogInformation("Using test server configuration: {Path}", configPath);
                }
                else if (_args.Contains("--testclient"))
                {
                    configPath = TEST_CLIENT_CONFIG_PATH;
                    _logger.LogInformation("Using test client configuration: {Path}", configPath);
                }
                else
                {
                    configPath = DEFAULT_CONFIG_PATH;
                }

                if (!File.Exists(configPath))
                {
                    _logger.LogError("Machine configuration file not found at {Path}", configPath);
                    throw new FileNotFoundException($"Machine configuration file not found at {configPath}");
                }

                var json = await File.ReadAllTextAsync(configPath);
                var loadedConfig = JsonConvert.DeserializeObject<MachineConfig>(json);

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
                throw;
            }
        }
    }
}