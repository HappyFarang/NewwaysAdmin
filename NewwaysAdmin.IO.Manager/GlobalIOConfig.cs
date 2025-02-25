using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NewwaysAdmin.IO.Manager
{
    public class GlobalIOConfig
    {
        public string LocalBaseFolder { get; set; } = "C:/NewwaysData";
        public string ServerDefinitionsPath { get; set; } = "X:/NewwaysAdmin/Definitions";
    }
}