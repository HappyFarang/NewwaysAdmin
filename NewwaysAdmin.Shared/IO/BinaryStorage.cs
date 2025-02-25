using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;

namespace NewwaysAdmin.Shared.IO.Binary
{
    public class BinaryStorage<T> : IDataStorage<T> where T : class, new()
    {
        private readonly StorageOptions _options;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public BinaryStorage(StorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrEmpty(options.BasePath);
            ArgumentException.ThrowIfNullOrEmpty(options.FileExtension);

            _options = options;
            Directory.CreateDirectory(options.BasePath);
        }

        public async Task<T> LoadAsync(string identifier)
        {
            var filePath = GetFilePath(identifier);
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(filePath))
                    throw new StorageException($"Data not found for identifier: {identifier}",
                        identifier, StorageOperation.Load);

                // First read the bytes from file
                var bytes = await File.ReadAllBytesAsync(filePath);

                // Then deserialize using System.Text.Json
                using var ms = new MemoryStream(bytes);
                var data = await JsonSerializer.DeserializeAsync<T>(ms);

                if (data == null)
                    throw new StorageException("Failed to deserialize data", identifier, StorageOperation.Load);

                return data;
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
            var filePath = GetFilePath(identifier);
            var tempPath = filePath + ".tmp";

            Console.WriteLine($"Attempting to save to: {filePath}"); // Debug
            Console.WriteLine($"Temp path: {tempPath}"); // Debug

            await _lock.WaitAsync();
            try
            {
                // Make sure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (directory == null)
                {
                    throw new InvalidOperationException($"Could not determine directory path from {filePath}");
                }

                if (!Directory.Exists(directory))
                {
                    Console.WriteLine($"Creating directory: {directory}"); // Debug
                    Directory.CreateDirectory(directory);
                }

                // Serialize using System.Text.Json to memory stream
                Console.WriteLine("Serializing data..."); // Debug
                using var ms = new MemoryStream();
                await JsonSerializer.SerializeAsync(ms, data);
                var bytes = ms.ToArray();
                Console.WriteLine($"Serialized to {bytes.Length} bytes"); // Debug

                await File.WriteAllBytesAsync(tempPath, bytes);
                Console.WriteLine("Wrote temp file successfully"); // Debug

                // Validate if enabled
                if (_options.ValidateAfterSave)
                {
                    Console.WriteLine("Validating save..."); // Debug
                    var verifyBytes = await File.ReadAllBytesAsync(tempPath);
                    using var verifyMs = new MemoryStream(verifyBytes);
                    var verifyData = await JsonSerializer.DeserializeAsync<T>(verifyMs);
                    if (verifyData == null)
                        throw new StorageException("Data validation failed", identifier, StorageOperation.Save);
                    Console.WriteLine("Validation successful"); // Debug
                }

                // Move file to final location
                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, null);
                    Console.WriteLine("Replaced existing file"); // Debug
                }
                else
                {
                    File.Move(tempPath, filePath);
                    Console.WriteLine("Moved temp file to final location"); // Debug
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save failed: {ex.GetType().Name} - {ex.Message}"); // Debug
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                        Console.WriteLine("Cleaned up temp file"); // Debug
                    }
                    catch (Exception cleanupEx)
                    {
                        Console.WriteLine($"Failed to clean up temp file: {cleanupEx.Message}"); // Debug
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
            // Sanitize identifier to be safe for filenames
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