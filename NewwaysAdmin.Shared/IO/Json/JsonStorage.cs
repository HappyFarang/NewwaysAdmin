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

        public JsonStorage(StorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrEmpty(options.BasePath);
            ArgumentException.ThrowIfNullOrEmpty(options.FileExtension);

            _options = options;
            Directory.CreateDirectory(options.BasePath);

            _serializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatHandling = DateFormatHandling.IsoDateFormat
            };
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
            finally
            {
                _lock.Release();
            }
        }

        public async Task SaveAsync(string identifier, T data)
        {
            var filePath = GetFilePath(identifier);
            var tempPath = filePath + ".tmp";

            await _lock.WaitAsync();
            try
            {
                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupAsync(filePath, identifier);
                }

                // Serialize to temporary file
                var json = JsonConvert.SerializeObject(data, _serializerSettings);
                await File.WriteAllTextAsync(tempPath, json);

                // Validate if enabled
                if (_options.ValidateAfterSave)
                {
                    var verifyJson = await File.ReadAllTextAsync(tempPath);
                    var verifyData = JsonConvert.DeserializeObject<T>(verifyJson, _serializerSettings);

                    if (verifyData == null)
                        throw new StorageException("Data validation failed", identifier, StorageOperation.Save);
                }

                // Move file to final location
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
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
                throw new StorageException("Failed to save data", identifier, StorageOperation.Save, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        public Task<bool> ExistsAsync(string identifier)
        {
            return Task.FromResult(File.Exists(GetFilePath(identifier)));
        }

        public Task<IEnumerable<string>> ListIdentifiersAsync()
        {
            var files = Directory.GetFiles(_options.BasePath, $"*{_options.FileExtension}")
                .Select(f => Path.GetFileNameWithoutExtension(f));
            return Task.FromResult(files);
        }

        public async Task DeleteAsync(string identifier)
        {
            var filePath = GetFilePath(identifier);

            await _lock.WaitAsync();
            try
            {
                if (_options.CreateBackups)
                {
                    await CreateBackupAsync(filePath, identifier);
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

        private string GetFilePath(string identifier)
        {
            var safeIdentifier = string.Join("_", identifier.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_options.BasePath, $"{safeIdentifier}{_options.FileExtension}");
        }

        private async Task CreateBackupAsync(string filePath, string identifier)
        {
            if (!File.Exists(filePath)) return;

            var backupDir = Path.Combine(_options.BasePath, "backups", identifier);
            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var backupPath = Path.Combine(backupDir, $"{timestamp}{_options.FileExtension}");

            await Task.Run(() => File.Copy(filePath, backupPath, true));

            // Cleanup old backups if we exceed MaxBackupCount
            if (_options.MaxBackupCount > 0)
            {
                var backups = Directory.GetFiles(backupDir)
                    .OrderByDescending(f => f)
                    .Skip(_options.MaxBackupCount);

                foreach (var oldBackup in backups)
                {
                    try
                    {
                        File.Delete(oldBackup);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }
        }
    }
}