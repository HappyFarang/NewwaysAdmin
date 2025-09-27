// NewwaysAdmin.Shared/IO/Json/JsonStorage.cs

using Newtonsoft.Json;
using JsonException = Newtonsoft.Json.JsonException;
using Formatting = Newtonsoft.Json.Formatting;

namespace NewwaysAdmin.Shared.IO.Json
{
    public class JsonStorage<T> : IDataStorage<T> where T : class, new()
    {
        private readonly StorageOptions _options;
        private readonly JsonSerializerSettings _serializerSettings;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly bool _passThroughMode; // NEW: PassThrough mode flag

        // Existing constructor (backwards compatible)
        public JsonStorage(StorageOptions options) : this(options, false)
        {
        }

        // NEW: Constructor with PassThrough mode support
        public JsonStorage(StorageOptions options, bool passThroughMode = false)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrEmpty(options.BasePath);
            ArgumentException.ThrowIfNullOrEmpty(options.FileExtension);

            _options = options;
            _passThroughMode = passThroughMode;
            Directory.CreateDirectory(options.BasePath);

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            };
        }

        // NEW: Property to check if this storage is in passthrough mode
        public bool IsPassThroughMode => _passThroughMode;

        // NEW: Method for copying files directly (passthrough mode only)
        /// <summary>
        /// Copies a file directly without serialization. Only available in PassThrough mode.
        /// Used for syncing external JSON files that are already properly serialized.
        /// </summary>
        /// <param name="sourceFilePath">Path to the source file to copy</param>
        /// <param name="identifier">Storage identifier for the target file</param>
        public async Task CopyFileDirectlyAsync(string sourceFilePath, string identifier)
        {
            if (!_passThroughMode)
                throw new InvalidOperationException("CopyFileDirectlyAsync is only available in PassThrough mode");

            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Source file not found: {sourceFilePath}");

            var targetPath = GetFilePath(identifier);
            await _lock.WaitAsync();
            try
            {
                // Create backup if file exists and backups are enabled
                if (_options.CreateBackups && File.Exists(targetPath))
                {
                    await CreateBackupAsync(targetPath);
                }

                // Ensure target directory exists
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Copy file as-is, preserving original JSON content
                File.Copy(sourceFilePath, targetPath, overwrite: true);
            }
            finally
            {
                _lock.Release();
            }
        }

        // Existing LoadAsync method (works in both modes)
        public async Task<T> LoadAsync(string identifier)
        {
            var filePath = GetFilePath(identifier);
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(filePath))
                    return new T();

                var json = await File.ReadAllTextAsync(filePath);
                var data = JsonConvert.DeserializeObject<T>(json, _serializerSettings);
                if (data == null)
                    throw new StorageException("Failed to deserialize data", identifier, StorageOperation.Load);

                return data;
            }
            catch (JsonException ex)
            {
                throw new StorageException("Invalid JSON format", identifier, StorageOperation.Load, ex);
            }
            catch (Exception ex)
            {
                throw new StorageException("Failed to load data", identifier, StorageOperation.Load, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        // Updated SaveAsync method (respects PassThrough mode)
        public async Task SaveAsync(string identifier, T data)
        {
            if (_passThroughMode)
            {
                throw new InvalidOperationException(
                    "SaveAsync is not supported in PassThrough mode. Use CopyFileDirectlyAsync instead.");
            }

            var filePath = GetFilePath(identifier);
            await _lock.WaitAsync();
            try
            {
                // Create backup if file exists and backups are enabled
                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupAsync(filePath);
                }

                var json = JsonConvert.SerializeObject(data, _serializerSettings);
                await File.WriteAllTextAsync(filePath, json);

                // Validate after save if enabled
                if (_options.ValidateAfterSave)
                {
                    await ValidateFileAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                throw new StorageException("Failed to save data", identifier, StorageOperation.Save, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        // Existing methods remain unchanged...
        public async Task<bool> ExistsAsync(string identifier)
        {
            var filePath = GetFilePath(identifier);
            return File.Exists(filePath);
        }

        public async Task<IEnumerable<string>> ListIdentifiersAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!Directory.Exists(_options.BasePath))
                    return Enumerable.Empty<string>();

                return Directory.GetFiles(_options.BasePath, $"*{_options.FileExtension}")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task DeleteAsync(string identifier)
        {
            var filePath = GetFilePath(identifier);
            await _lock.WaitAsync();
            try
            {
                if (File.Exists(filePath))
                {
                    // Create backup before deletion if enabled
                    if (_options.CreateBackups)
                    {
                        await CreateBackupAsync(filePath);
                    }
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                throw new StorageException("Failed to delete data", identifier, StorageOperation.Delete, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        // Private helper methods (existing)
        private string GetFilePath(string identifier)
        {
            return Path.Combine(_options.BasePath, $"{identifier}{_options.FileExtension}");
        }

        private async Task CreateBackupAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var backupDir = Path.Combine(Path.GetDirectoryName(filePath)!, "Backups");
            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = Path.GetFileName(filePath);
            var backupPath = Path.Combine(backupDir, $"{timestamp}_{fileName}");

            File.Copy(filePath, backupPath);

            // Clean up old backups
            await CleanupOldBackupsAsync(backupDir);
        }

        private async Task CleanupOldBackupsAsync(string backupDir)
        {
            if (!Directory.Exists(backupDir)) return;

            var backupFiles = Directory.GetFiles(backupDir)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            // Remove files beyond the max backup count
            if (backupFiles.Count > _options.MaxBackupCount)
            {
                for (int i = _options.MaxBackupCount; i < backupFiles.Count; i++)
                {
                    backupFiles[i].Delete();
                }
            }
        }

        private async Task ValidateFileAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                JsonConvert.DeserializeObject<T>(json, _serializerSettings);
            }
            catch (JsonException ex)
            {
                throw new StorageException("File validation failed after save",
                    Path.GetFileNameWithoutExtension(filePath), StorageOperation.Save, ex);
            }
        }
    }
}