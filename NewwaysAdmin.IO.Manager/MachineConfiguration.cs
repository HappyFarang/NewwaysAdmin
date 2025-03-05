// MachineConfiguration.cs
using Newtonsoft.Json;
using System;
using System.IO;

namespace NewwaysAdmin.IO.Manager
{
    public class MachineConfiguration
    {
        public string MachineName { get; set; }
        public string Environment { get; set; }
        public string LocalBasePath { get; set; }
        public string ServerBasePath { get; set; }
        public string Role { get; set; }

        private const string DEFAULT_CONFIG_PATH = "C:/MachineConfig/machine.json";

        public MachineConfiguration()
        {
            // Set default values in constructor instead of in property initializers
            MachineName = System.Environment.MachineName;
            Environment = "Development";
            LocalBasePath = "C:/NewwaysData";
            ServerBasePath = "X:/NewwaysAdmin";
            Role = "Client"; // "Client" or "Server"
        }

        public static MachineConfiguration Load(string? configPath = null)
        {
            configPath ??= DEFAULT_CONFIG_PATH;

            if (!File.Exists(configPath))
            {
                var config = new MachineConfiguration();
                Directory.CreateDirectory(Path.GetDirectoryName(configPath));
                File.WriteAllText(configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
                return config;
            }

            var json = File.ReadAllText(configPath);
            return JsonConvert.DeserializeObject<MachineConfiguration>(json) ?? new MachineConfiguration();
        }
    }
}