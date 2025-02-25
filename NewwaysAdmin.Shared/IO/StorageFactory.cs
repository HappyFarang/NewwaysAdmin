using NewwaysAdmin.Shared.IO.Binary;
using NewwaysAdmin.Shared.IO.Json;

namespace NewwaysAdmin.Shared.IO
{
    public class StorageFactory
    {
        private readonly string _baseDirectory;
        private readonly Dictionary<string, StorageStructure> _structures;
        private const string DEFAULT_STRUCTURE = "Default";

        [Obsolete("Use Structure.EnhancedStorageFactory instead. This class will be removed in a future version.")]
        public StorageFactory(string baseDirectory, IEnumerable<StorageStructure>? structures = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
            _baseDirectory = baseDirectory;

            // If no structures provided, create a default one
            structures ??= new[] { CreateDefaultStructure() };

            _structures = structures.ToDictionary(x => x.Name);
            InitializeStructures();
        }

        private StorageStructure CreateDefaultStructure()
        {
            var root = new StorageNode
            {
                Name = "Root",
                Type = StorageType.Binary,
                CreateBackups = true,
                MaxBackupCount = 5
            };

            // Create default paths for backwards compatibility
            var configNode = root.AddChild("Config", StorageType.Json);
            var dataNode = root.AddChild("Data", StorageType.Binary);

            return new StorageStructure
            {
                Name = DEFAULT_STRUCTURE,
                Description = "Default storage structure",
                RootNode = root
            };
        }

        private void InitializeStructures()
        {
            foreach (var structure in _structures.Values)
            {
                CreateStructure(structure.RootNode, new List<string>());
            }
        }

        private void CreateStructure(StorageNode node, List<string> parentPath)
        {
            var currentPath = new List<string>(parentPath) { node.Name };
            var fullPath = Path.Combine(_baseDirectory, Path.Combine(currentPath.ToArray()));

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
                Console.WriteLine($"Created directory: {fullPath}");
            }

            foreach (var child in node.Children)
            {
                CreateStructure(child, currentPath);
            }
        }

        // Generic interface method
        public IDataStorage<T> GetStorage<T>(string structureName, params string[] pathSegments) where T : class, new()
        {
            Console.WriteLine($"Attempting to get storage for structure: {structureName}");
            Console.WriteLine($"Path segments: {string.Join(", ", pathSegments)}");

            if (!_structures.TryGetValue(structureName, out var structure))
            {
                Console.WriteLine($"Structure not found: {structureName}");
                throw new StorageException($"Structure not found: {structureName}", structureName, StorageOperation.Load);
            }

            var node = structure.FindNode(pathSegments);
            if (node == null)
            {
                Console.WriteLine($"Invalid path: {string.Join("/", pathSegments)}");
                throw new StorageException($"Invalid path: {string.Join("/", pathSegments)}", structureName, StorageOperation.Load);
            }

            var fullPath = node.GetFullPath(_baseDirectory, pathSegments.Take(pathSegments.Length - 1).ToList());
            Console.WriteLine($"Full path: {fullPath}");

            return CreateStorage<T>(fullPath, node.Type, node.CreateBackups, node.MaxBackupCount);
        }

        // Methods that return concrete types
        public JsonStorage<T> CreateJsonStorage<T>(string path) where T : class, new()
        {
            var options = new StorageOptions
            {
                BasePath = Path.Combine(_baseDirectory, path),
                FileExtension = ".json",
                CreateBackups = true,
                MaxBackupCount = 5,
                ValidateAfterSave = true
            };

            return new JsonStorage<T>(options);
        }

        public BinaryStorage<T> CreateBinaryStorage<T>(string path) where T : class, new()
        {
            var options = new StorageOptions
            {
                BasePath = Path.Combine(_baseDirectory, path),
                FileExtension = ".bin",
                CreateBackups = true,
                MaxBackupCount = 5,
                ValidateAfterSave = true
            };

            return new BinaryStorage<T>(options);
        }

        private IDataStorage<T> CreateStorage<T>(string path, StorageType type, bool createBackups, int maxBackupCount) where T : class, new()
        {
            var options = new StorageOptions
            {
                BasePath = path,
                FileExtension = type == StorageType.Json ? ".json" : ".bin",
                CreateBackups = createBackups,
                MaxBackupCount = maxBackupCount,
                ValidateAfterSave = true
            };

            return type == StorageType.Json
                ? new JsonStorage<T>(options)
                : new BinaryStorage<T>(options);
        }

        public StorageStructure GetStructure(string name)
        {
            if (!_structures.TryGetValue(name, out var structure))
                throw new StorageException($"Structure not found: {name}", name, StorageOperation.Load);

            return structure;
        }
    }
}