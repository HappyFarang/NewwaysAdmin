using Microsoft.Extensions.DependencyInjection;

namespace NewwaysAdmin.IO.Manager
{
    public class IOManagerOptions
    {
        public required string LocalBaseFolder { get; init; }
        public required string ServerDefinitionsPath { get; init; }
        public required string ApplicationName { get; init; }
    }
}