using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace NewwaysAdmin.IO.Manager
{
    public class IOConfigLoader
    {
        private const string DEFAULT_CONFIG_PATH = "C:/NewwaysAdmin/Config/io-config.json";
        private readonly ILogger<IOConfigLoader> _logger;

        public IOConfigLoader(ILogger<IOConfigLoader> logger)
        {
            _logger = logger;
        }

        public async Task<GlobalIOConfig> LoadConfigAsync(string? configPath = null)
        {
            configPath ??= DEFAULT_CONFIG_PATH;

            try
            {
                if (!File.Exists(configPath))
                {
                    var config = CreateDefaultConfig();
                    await SaveConfigAsync(config, configPath);
                    return config;
                }

                var json = await File.ReadAllTextAsync(configPath);
                var loadedConfig = JsonConvert.DeserializeObject<GlobalIOConfig>(json);

                if (loadedConfig == null)
                {
                    throw new InvalidOperationException("Failed to deserialize configuration");
                }

                return loadedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load IO configuration. Using defaults.");
                return CreateDefaultConfig();
            }
        }

        private async Task SaveConfigAsync(GlobalIOConfig config, string path)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                await File.WriteAllTextAsync(path, json);
                _logger.LogInformation("Saved IO configuration to {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save IO configuration");
                throw;
            }
        }

        private GlobalIOConfig CreateDefaultConfig()
        {
            return new GlobalIOConfig
            {
                LocalBaseFolder = "C:/NewwaysData",
                ServerDefinitionsPath = "X:/NewwaysAdmin/Definitions"
            };
        }
    }

}
