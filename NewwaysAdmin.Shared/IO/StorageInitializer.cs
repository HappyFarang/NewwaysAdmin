// NewwaysAdmin.Shared/IO/StorageInitializer.cs
namespace NewwaysAdmin.Shared.IO
{
    public class StorageInitializer
    {
        public class StoragePaths
        {
            public string BaseDirectory { get; set; } = "";
            public string SalesData { get; set; } = "";
            public string Configuration { get; set; } = "";
            public string Backups { get; set; } = "";
            public string Logs { get; set; } = "";
        }

        private readonly StoragePaths _paths;

        public StorageInitializer(string baseDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(baseDirectory);

            _paths = new()
            {
                BaseDirectory = baseDirectory,
                SalesData = Path.Combine(baseDirectory, "Data", "Sales"),
                Configuration = Path.Combine(baseDirectory, "Data", "Config"),
                Backups = Path.Combine(baseDirectory, "Data", "Backups"),
                Logs = Path.Combine(baseDirectory, "Logs")
            };
        }

        public StoragePaths Initialize()
        {
            try
            {
                // Create all required directories
                CreateDirectory(_paths.BaseDirectory);
                CreateDirectory(_paths.SalesData);
                CreateDirectory(_paths.Configuration);
                CreateDirectory(_paths.Backups);
                CreateDirectory(_paths.Logs);

                // Create subdirectories for different data types
                CreateDirectory(Path.Combine(_paths.SalesData, "Daily"));
                CreateDirectory(Path.Combine(_paths.SalesData, "Monthly"));
                CreateDirectory(Path.Combine(_paths.Configuration, "Platform"));
                CreateDirectory(Path.Combine(_paths.Configuration, "System"));

                // Test write permissions by creating and deleting a test file
                VerifyWriteAccess(_paths.SalesData);
                VerifyWriteAccess(_paths.Configuration);
                VerifyWriteAccess(_paths.Backups);
                VerifyWriteAccess(_paths.Logs);

                return _paths;
            }
            catch (Exception ex)
            {
                throw new StorageException(
                    $"Failed to initialize storage structure: {ex.Message}",
                    "initialization",
                    StorageOperation.Save,
                    ex);
            }
        }

        private void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Console.WriteLine($"Created directory: {path}");
            }
        }

        private void VerifyWriteAccess(string path)
        {
            var testFile = Path.Combine(path, ".write_test");
            try
            {
                // Try to create a test file
                File.WriteAllText(testFile, "Write test");
                // If successful, delete it
                File.Delete(testFile);
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException(
                    $"No write access to directory: {path}", ex);
            }
        }
    }
}
