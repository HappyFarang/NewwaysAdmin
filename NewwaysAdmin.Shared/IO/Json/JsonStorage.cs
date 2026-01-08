// NewwaysAdmin.Shared/IO/Json/JsonStorage.cs
// UPDATED: Added raw file methods for direct byte[] storage

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
        private readonly bool _passThroughMode;

        // Existing constructor (backwards compatible)
        public JsonStorage(StorageOptions options) : this(options, false)
        {
        }

        // Constructor with PassThrough mode support
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

        public bool IsPassThroughMode => _passThroughMode;

        // ===================================================================
        // EXISTING METHODS (unchanged)
        // ===================================================================

        /// <summary>
        /// Copies a file directly without serialization. Only available in PassThrough mode.
        /// </summary>
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
                if (_options.CreateBackups && File.Exists(targetPath))
                {
                    await CreateBackupAsync(targetPath);
                }

                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(sourceFilePath, targetPath, overwrite: true);
            }
            finally
            {
                _lock.Release();
            }
        }

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
            catch (StorageException)
            {
                throw;
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
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupAsync(filePath);
                }

                var json = JsonConvert.SerializeObject(data, _serializerSettings);
                await File.WriteAllTextAsync(filePath, json);

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
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(f => f != null)!;
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
                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupAsync(filePath);
                }

                if (File.Exists(filePath))
                    File.Delete(filePath);
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

        // ===================================================================
        // NEW: RAW FILE METHODS
        // For direct byte[] storage without serialization wrapper
        // Identifier INCLUDES the file extension (e.g., "image_001.jpg")
        // ===================================================================

        /// <summary>
        /// Save raw bytes directly to storage without serialization.
        /// Identifier should include the file extension (e.g., "photo_001.jpg").
        /// </summary>
        public async Task SaveRawAsync(string identifier, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            ArgumentException.ThrowIfNullOrEmpty(identifier);

            var filePath = Path.Combine(_options.BasePath, identifier);
            var tempPath = filePath + ".tmp";

            await _lock.WaitAsync();
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupForRawAsync(filePath, identifier);
                }

                await File.WriteAllBytesAsync(tempPath, data);

                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, null);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw new StorageException("Failed to save raw file", identifier, StorageOperation.Save, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Load raw bytes directly from storage without deserialization.
        /// </summary>
        public async Task<byte[]?> LoadRawAsync(string identifier)
        {
            ArgumentException.ThrowIfNullOrEmpty(identifier);

            var filePath = Path.Combine(_options.BasePath, identifier);

            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(filePath))
                    return null;

                return await File.ReadAllBytesAsync(filePath);
            }
            catch (Exception ex)
            {
                throw new StorageException("Failed to load raw file", identifier, StorageOperation.Load, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Check if a raw file exists
        /// </summary>
        public Task<bool> ExistsRawAsync(string identifier)
        {
            ArgumentException.ThrowIfNullOrEmpty(identifier);
            var filePath = Path.Combine(_options.BasePath, identifier);
            return Task.FromResult(File.Exists(filePath));
        }

        /// <summary>
        /// Delete a raw file
        /// </summary>
        public async Task DeleteRawAsync(string identifier)
        {
            ArgumentException.ThrowIfNullOrEmpty(identifier);

            var filePath = Path.Combine(_options.BasePath, identifier);

            await _lock.WaitAsync();
            try
            {
                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupForRawAsync(filePath, identifier);
                }

                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                throw new StorageException("Failed to delete raw file", identifier, StorageOperation.Delete, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// List all raw files in storage (with extensions)
        /// </summary>
        public Task<IEnumerable<string>> ListRawFilesAsync(string? searchPattern = null)
        {
            var pattern = searchPattern ?? "*.*";

            if (!Directory.Exists(_options.BasePath))
                return Task.FromResult(Enumerable.Empty<string>());

            var files = Directory.GetFiles(_options.BasePath, pattern)
                .Select(f => Path.GetFileName(f))
                .Where(f => !f.EndsWith(".tmp"));

            return Task.FromResult(files);
        }

        // ===================================================================
        // PRIVATE HELPERS
        // ===================================================================

        private string GetFilePath(string identifier)
        {
            return Path.Combine(_options.BasePath, $"{identifier}{_options.FileExtension}");
        }

        private async Task CreateBackupAsync(string filePath)
        {
            if (!File.Exists(filePath)) return;

            var identifier = Path.GetFileNameWithoutExtension(filePath);
            var backupDir = Path.Combine(_options.BasePath, "backups", identifier);
            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var backupPath = Path.Combine(backupDir, $"{timestamp}{_options.FileExtension}");

            await Task.Run(() => File.Copy(filePath, backupPath, true));

            CleanupOldBackups(backupDir);
        }

        private async Task CreateBackupForRawAsync(string filePath, string identifier)
        {
            if (!File.Exists(filePath)) return;

            var nameWithoutExt = Path.GetFileNameWithoutExtension(identifier);
            var extension = Path.GetExtension(identifier);

            var backupDir = Path.Combine(_options.BasePath, "backups", nameWithoutExt);
            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var backupPath = Path.Combine(backupDir, $"{timestamp}{extension}");

            await Task.Run(() => File.Copy(filePath, backupPath, true));

            CleanupOldBackups(backupDir);
        }

        private void CleanupOldBackups(string backupDir)
        {
            if (_options.MaxBackupCount <= 0) return;

            var backups = Directory.GetFiles(backupDir)
                .OrderByDescending(f => f)
                .Skip(_options.MaxBackupCount);

            foreach (var oldBackup in backups)
            {
                try { File.Delete(oldBackup); } catch { }
            }
        }

        private async Task ValidateFileAsync(string filePath)
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonConvert.DeserializeObject<T>(json, _serializerSettings);
            if (data == null)
            {
                throw new StorageException("File validation failed after save",
                    Path.GetFileNameWithoutExtension(filePath), StorageOperation.Save);
            }
        }
    }
}