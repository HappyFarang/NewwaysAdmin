using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.Infrastructure.Storage
{
    public class StorageFolderDefinition
    {
        public required StorageFolder Folder { get; init; }
        public HashSet<string> AllowedMachines { get; init; } = new();
        public HashSet<string> AllowedApps { get; init; } = new();
    }

    public class StorageStructureRegistry
    {
        private readonly Dictionary<string, StorageFolderDefinition> _folderDefinitions = new();
        private readonly string _machineId;
        private readonly string _applicationId;
        private readonly ILogger<StorageStructureRegistry> _logger;

        public StorageStructureRegistry(
            ILogger<StorageStructureRegistry> logger,
            string machineId,
            string applicationId)
        {
            _logger = logger;
            _machineId = machineId;
            _applicationId = applicationId;
        }

        public void RegisterFolder(string moduleId, StorageFolderDefinition definition)
        {
            _folderDefinitions[moduleId] = definition;
            _logger.LogInformation($"Registered folder for module: {moduleId}");
        }

        public IEnumerable<StorageFolder> GetFoldersForCurrentMachine()
        {
            return _folderDefinitions
                .Where(kvp => HasAccess(kvp.Value))
                .Select(kvp => kvp.Value.Folder);
        }

        private bool HasAccess(StorageFolderDefinition definition)
        {
            return (definition.AllowedMachines.Count == 0 || definition.AllowedMachines.Contains(_machineId)) &&
                   (definition.AllowedApps.Count == 0 || definition.AllowedApps.Contains(_applicationId));
        }
    }

    // Example module definition
    public static class UserStorageDefinitions
    {
        public static class Folders
        {
            public const string Users = "Users";
            public const string Logs = "Logs";
        }

        public static void RegisterFolders(StorageStructureRegistry registry)
        {
            // Users folder
            registry.RegisterFolder(Folders.Users, new StorageFolderDefinition
            {
                Folder = new StorageFolder
                {
                    Name = "Users",
                    Description = "User data storage",
                    Type = StorageType.Json,
                    IsShared = false,
                    Path = "System",
                    CreatedBy = "NewwaysAdmin",
                    CreateBackups = true,
                    MaxBackupCount = 5
                },
                AllowedMachines = new HashSet<string> { "SERVER1", "ADMIN-PC" },
                AllowedApps = new HashSet<string> { "NewwaysAdmin" }
            });

            // Logs folder
            registry.RegisterFolder(Folders.Logs, new StorageFolderDefinition
            {
                Folder = new StorageFolder
                {
                    Name = "Logs",
                    Description = "System logs",
                    Type = StorageType.Json,
                    IsShared = true,
                    Path = "System",
                    CreatedBy = "NewwaysAdmin",
                    CreateBackups = false
                },
                // No machine restrictions - logs available everywhere
                AllowedApps = new HashSet<string> { "NewwaysAdmin", "ClientApp" }
            });
        }
    }

    // Extension method for service registration
    public static class StorageRegistryExtensions
    {
        public static IServiceCollection AddStorageRegistry(
            this IServiceCollection services,
            string applicationId,
            string? machineId = null)
        {
            services.AddSingleton<StorageStructureRegistry>(sp =>
                new StorageStructureRegistry(
                    sp.GetRequiredService<ILogger<StorageStructureRegistry>>(),
                    machineId ?? Environment.MachineName,
                    applicationId
                ));

            return services;
        }
    }

    // Factory extension to work with the registry
    public class EnhancedStorageFactoryWithRegistry
    {
        private readonly EnhancedStorageFactory _factory;
        private readonly StorageStructureRegistry _registry;

        public EnhancedStorageFactoryWithRegistry(
            EnhancedStorageFactory factory,
            StorageStructureRegistry registry)
        {
            _factory = factory;
            _registry = registry;
        }

        public void InitializeFromRegistry()
        {
            foreach (var folder in _registry.GetFoldersForCurrentMachine())
            {
                _factory.RegisterFolder(folder);
            }
        }

        // Delegate other methods to the original factory
        public IDataStorage<T> GetStorage<T>(string folderName) where T : class, new()
            => _factory.GetStorage<T>(folderName);

        public string GetDirectoryStructure()
            => _factory.GetDirectoryStructure();
    }
}