// File: Mobile/NewwaysAdmin.Mobile/Infrastructure/MobileMachineConfigProvider.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.Configuration;

namespace NewwaysAdmin.Mobile.Infrastructure
{
    /// <summary>
    /// Mobile-specific machine config provider that doesn't read from machine config files
    /// Instead provides mobile-appropriate defaults
    /// </summary>
    public class MobileMachineConfigProvider
    {
        private readonly ILogger<MobileMachineConfigProvider> _logger;
        private readonly string _mobileBaseFolder;

        public MobileMachineConfigProvider(ILogger<MobileMachineConfigProvider> logger, string mobileBaseFolder)
        {
            _logger = logger;
            _mobileBaseFolder = mobileBaseFolder;
        }

        public async Task<MachineConfig> LoadConfigAsync()
        {
            // Don't try to load from machine config files - provide mobile defaults
            _logger.LogInformation("Using mobile-specific configuration instead of machine config files");

            var mobileConfig = new MachineConfig
            {
                MachineName = Environment.MachineName + "_Mobile",
                MachineRole = "MOBILE_CLIENT",
                LocalBaseFolder = _mobileBaseFolder, // Use the mobile-specific path
                ServerBasePath = "", // Mobile doesn't need server paths
                Apps = new Dictionary<string, AppConfig>
                {
                    ["NewwaysAdmin.Mobile"] = new AppConfig
                    {
                        Version = "1.0",
                        LocalPaths = new Dictionary<string, string>
                        {
                            ["Base"] = _mobileBaseFolder
                        },
                        Settings = new Dictionary<string, string>
                        {
                            ["Type"] = "Mobile",
                            ["Platform"] = "MAUI"
                        }
                    }
                }
            };

            _logger.LogInformation("Mobile config created with base folder: {BaseFolder}", _mobileBaseFolder);

            return await Task.FromResult(mobileConfig);
        }
    }
}