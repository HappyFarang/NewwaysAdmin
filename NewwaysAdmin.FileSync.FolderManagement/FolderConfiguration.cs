// NewwaysAdmin.FileSync.FolderManagement/Models/FolderConfiguration.cs
using NewwaysAdmin.FileSync.FolderManagement.Interfaces;
using NewwaysAdmin.FileSync.FolderManagement.Models;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace NewwaysAdmin.FileSync.FolderManagement.Models
{
    public class FolderConfiguration
    {
        public required string FolderId { get; set; }
        public required string Path { get; set; }
        public required string DisplayName { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, FolderAccessRights> ClientAccess { get; set; } = new();

        [JsonIgnore]
        public bool IsActive => Directory.Exists(Path);
    }

    public class FolderAccessRights
    {
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }
        public bool NotifyOnChanges { get; set; }

        public static FolderAccessRights FullAccess => new()
        {
            CanRead = true,
            CanWrite = true,
            NotifyOnChanges = true
        };

        public static FolderAccessRights ReadOnly => new()
        {
            CanRead = true,
            CanWrite = false,
            NotifyOnChanges = true
        };
    }
}

// NewwaysAdmin.FileSync.FolderManagement/Interfaces/IFolderManager.cs
namespace NewwaysAdmin.FileSync.FolderManagement.Interfaces
{
    public interface IFolderManager
    {
        IEnumerable<FolderConfiguration> GetAllFolders();
        FolderConfiguration? GetFolder(string folderId);
        List<FolderConfiguration> GetClientFolders(string clientId);
        Task<bool> UpdateClientAccessAsync(string folderId, string clientId, FolderAccessRights rights);
        Task<bool> AddFolderAsync(FolderConfiguration folder);
        Task<bool> RemoveFolderAsync(string folderId);
        Task<bool> UpdateFolderAsync(FolderConfiguration folder);
        Task SaveConfigurationAsync();
        event EventHandler<FolderConfigurationChangedEventArgs>? ConfigurationChanged;
    }

    public class FolderConfigurationChangedEventArgs : EventArgs
    {
        public string? ChangedFolderId { get; init; }
        public string? AffectedClientId { get; init; }
        public ChangeType ChangeType { get; init; }
    }

    public enum ChangeType
    {
        Added,
        Updated,
        Removed,
        AccessChanged
    }
}

// NewwaysAdmin.FileSync.FolderManagement/ServerFolderManager.cs
namespace NewwaysAdmin.FileSync.FolderManagement
{
    public class ServerFolderManager : IFolderManager
    {
        private readonly ILogger<ServerFolderManager> _logger;
        private readonly string _configPath;
        private readonly Dictionary<string, FolderConfiguration> _folderConfigs = new();
        private readonly SemaphoreSlim _lock = new(1, 1);

        public event EventHandler<FolderConfigurationChangedEventArgs>? ConfigurationChanged;

        public ServerFolderManager(ILogger<ServerFolderManager> logger, string configPath)
        {
            _logger = logger;
            _configPath = configPath;
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var configs = JsonConvert.DeserializeObject<List<FolderConfiguration>>(json);
                    if (configs != null)
                    {
                        _folderConfigs.Clear();
                        foreach (var config in configs)
                        {
                            _folderConfigs[config.FolderId] = config;
                        }
                        _logger.LogInformation("Loaded {Count} folder configurations", _folderConfigs.Count);
                    }
                }
                else
                {
                    CreateDefaultConfiguration();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading folder configuration");
                CreateDefaultConfiguration();
            }
        }

        private void CreateDefaultConfiguration()
        {
            var defaultConfig = new FolderConfiguration
            {
                FolderId = "default",
                Path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sync"),
                DisplayName = "Default Sync Folder",
                Description = "Default synchronization folder"
            };

            _folderConfigs.Clear();
            _folderConfigs[defaultConfig.FolderId] = defaultConfig;
            SaveConfigurationAsync().Wait();
            _logger.LogInformation("Created default folder configuration");
        }

        public async Task SaveConfigurationAsync()
        {
            await _lock.WaitAsync();
            try
            {
                var json = JsonConvert.SerializeObject(_folderConfigs.Values.ToList(), Formatting.Indented);
                await File.WriteAllTextAsync(_configPath, json);
                _logger.LogInformation("Saved folder configuration");
            }
            finally
            {
                _lock.Release();
            }
        }

        public IEnumerable<FolderConfiguration> GetAllFolders() => _folderConfigs.Values;

        public FolderConfiguration? GetFolder(string folderId)
        {
            return _folderConfigs.GetValueOrDefault(folderId);
        }

        public List<FolderConfiguration> GetClientFolders(string clientId)
        {
            return _folderConfigs.Values
                .Where(f => f.ClientAccess.ContainsKey(clientId))
                .ToList();
        }

        public async Task<bool> UpdateClientAccessAsync(string folderId, string clientId, FolderAccessRights rights)
        {
            await _lock.WaitAsync();
            try
            {
                if (_folderConfigs.TryGetValue(folderId, out var folder))
                {
                    folder.ClientAccess[clientId] = rights;
                    await SaveConfigurationAsync();
                    ConfigurationChanged?.Invoke(this, new FolderConfigurationChangedEventArgs
                    {
                        ChangedFolderId = folderId,
                        AffectedClientId = clientId,
                        ChangeType = ChangeType.AccessChanged
                    });
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> AddFolderAsync(FolderConfiguration folder)
        {
            await _lock.WaitAsync();
            try
            {
                if (_folderConfigs.ContainsKey(folder.FolderId))
                    return false;

                _folderConfigs[folder.FolderId] = folder;
                await SaveConfigurationAsync();
                ConfigurationChanged?.Invoke(this, new FolderConfigurationChangedEventArgs
                {
                    ChangedFolderId = folder.FolderId,
                    ChangeType = ChangeType.Added
                });
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> RemoveFolderAsync(string folderId)
        {
            await _lock.WaitAsync();
            try
            {
                if (_folderConfigs.Remove(folderId))
                {
                    await SaveConfigurationAsync();
                    ConfigurationChanged?.Invoke(this, new FolderConfigurationChangedEventArgs
                    {
                        ChangedFolderId = folderId,
                        ChangeType = ChangeType.Removed
                    });
                    return true;
                }
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> UpdateFolderAsync(FolderConfiguration folder)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_folderConfigs.ContainsKey(folder.FolderId))
                    return false;

                _folderConfigs[folder.FolderId] = folder;
                await SaveConfigurationAsync();
                ConfigurationChanged?.Invoke(this, new FolderConfigurationChangedEventArgs
                {
                    ChangedFolderId = folder.FolderId,
                    ChangeType = ChangeType.Updated
                });
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}