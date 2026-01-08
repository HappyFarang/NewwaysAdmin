// NewwaysAdmin.Shared/IO/Binary/BinaryStorage.cs
// UPDATED: Added raw file methods for direct byte[] storage

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

        // ===================================================================
        // EXISTING TYPED METHODS (unchanged)
        // ===================================================================

        public async Task<T> LoadAsync(string identifier)
        {
            var filePath = GetFilePath(identifier);
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(filePath))
                    throw new StorageException($"Data not found for identifier: {identifier}",
                        identifier, StorageOperation.Load);

                var bytes = await File.ReadAllBytesAsync(filePath);

                using var ms = new MemoryStream(bytes);
                var data = await JsonSerializer.DeserializeAsync<T>(ms);

                if (data == null)
                    throw new StorageException("Failed to deserialize data", identifier, StorageOperation.Load);

                return data;
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
            var filePath = GetFilePath(identifier);
            var tempPath = filePath + ".tmp";

            await _lock.WaitAsync();
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (directory == null)
                {
                    throw new InvalidOperationException($"Could not determine directory path from {filePath}");
                }

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var ms = new MemoryStream();
                await JsonSerializer.SerializeAsync(ms, data);
                var bytes = ms.ToArray();

                await File.WriteAllBytesAsync(tempPath, bytes);

                if (_options.ValidateAfterSave)
                {
                    var verifyBytes = await File.ReadAllBytesAsync(tempPath);
                    using var verifyMs = new MemoryStream(verifyBytes);
                    var verifyData = await JsonSerializer.DeserializeAsync<T>(verifyMs);
                    if (verifyData == null)
                        throw new StorageException("Data validation failed", identifier, StorageOperation.Save);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, null);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
            }
            catch (StorageException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
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

            // For raw mode, identifier IS the filename (including extension)
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

                // Create backup if file exists and backups are enabled
                if (_options.CreateBackups && File.Exists(filePath))
                {
                    await CreateBackupForRawAsync(filePath, identifier);
                }

                // Write to temp file first
                await File.WriteAllBytesAsync(tempPath, data);

                // Move to final location
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
        /// <param name="searchPattern">Optional pattern like "*.jpg" or "project_*"</param>
        public Task<IEnumerable<string>> ListRawFilesAsync(string? searchPattern = null)
        {
            var pattern = searchPattern ?? "*.*";

            if (!Directory.Exists(_options.BasePath))
                return Task.FromResult(Enumerable.Empty<string>());

            var files = Directory.GetFiles(_options.BasePath, pattern)
                .Select(f => Path.GetFileName(f))
                .Where(f => !f.EndsWith(".tmp")); // Exclude temp files

            return Task.FromResult(files);
        }

        // ===================================================================
        // PRIVATE HELPERS
        // ===================================================================

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

            CleanupOldBackups(backupDir);
        }

        private async Task CreateBackupForRawAsync(string filePath, string identifier)
        {
            if (!File.Exists(filePath)) return;

            // Use filename without extension as the backup folder name
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
    }
}