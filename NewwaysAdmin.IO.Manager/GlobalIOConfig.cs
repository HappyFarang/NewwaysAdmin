// GlobalIOConfig.cs
using Newtonsoft.Json;

namespace NewwaysAdmin.IO.Manager
{
    public class GlobalIOConfig
    {
        public string LocalBaseFolder { get; set; } = "C:/NewwaysData";
        public string ServerDefinitionsPath { get; set; } = "X:/NewwaysAdmin/Definitions";
        public string MachineRole { get; set; } = "CLIENT";
    }
}