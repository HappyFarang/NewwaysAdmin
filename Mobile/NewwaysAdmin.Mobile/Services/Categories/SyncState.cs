// File: Mobile/NewwaysAdmin.Mobile/Services/Categories/SyncState.cs
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Services.Categories
{
    /// <summary>
    /// Persisted sync state - survives app restart
    /// Tracks local version and download status
    /// </summary>
    public class SyncState
    {
        private readonly ILogger<SyncState> _logger;
        private readonly string _stateFilePath;

        private SyncStateData _state;

        public SyncState(ILogger<SyncState> logger)
        {
            _logger = logger;

            var cacheDir = Path.Combine(FileSystem.AppDataDirectory, "CategoryCache");
            Directory.CreateDirectory(cacheDir);
            _stateFilePath = Path.Combine(cacheDir, "sync_state.json");

            _state = Load();
        }

        // ===== PROPERTIES =====

        /// <summary>
        /// Version of data we have locally (0 if no data yet)
        /// </summary>
        public int LocalVersion
        {
            get => _state.LocalVersion;
            private set
            {
                _state.LocalVersion = value;
                Save();
            }
        }

        /// <summary>
        /// Last known server version (0 if never connected)
        /// </summary>
        public int RemoteVersion
        {
            get => _state.RemoteVersion;
            private set
            {
                _state.RemoteVersion = value;
                Save();
            }
        }

        /// <summary>
        /// True if we know server has newer data we haven't downloaded yet
        /// </summary>
        public bool NeedsDownload
        {
            get => _state.NeedsDownload;
            private set
            {
                _state.NeedsDownload = value;
                Save();
            }
        }

        /// <summary>
        /// When we last successfully synced
        /// </summary>
        public DateTime? LastSyncTime
        {
            get => _state.LastSyncTime;
            private set
            {
                _state.LastSyncTime = value;
                Save();
            }
        }

        /// <summary>
        /// True if this is first run (no local data exists)
        /// </summary>
        public bool IsFirstRun => LocalVersion == 0;

        // ===== METHODS =====

        /// <summary>
        /// Called when we learn about server's version
        /// </summary>
        public void SetRemoteVersion(int version)
        {
            RemoteVersion = version;

            if (version > LocalVersion)
            {
                NeedsDownload = true;
                _logger.LogInformation("Server has newer version (v{Remote} > v{Local}), download needed",
                    version, LocalVersion);
            }
        }

        /// <summary>
        /// Called after successful download and save of data
        /// </summary>
        public void MarkDownloadComplete(int downloadedVersion)
        {
            LocalVersion = downloadedVersion;
            RemoteVersion = downloadedVersion;
            NeedsDownload = false;
            LastSyncTime = DateTime.UtcNow;

            _logger.LogInformation("Download complete - now at v{Version}", downloadedVersion);
        }

        /// <summary>
        /// Called if download fails - keeps NeedsDownload = true for retry
        /// </summary>
        public void MarkDownloadFailed()
        {
            _logger.LogWarning("Download failed - will retry on next connection");
            // NeedsDownload stays true, will retry later
        }

        /// <summary>
        /// Reset state (for debugging/testing)
        /// </summary>
        public void Reset()
        {
            _state = new SyncStateData();
            Save();
            _logger.LogInformation("Sync state reset");
        }

        // ===== PERSISTENCE =====

        private SyncStateData Load()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    var json = File.ReadAllText(_stateFilePath);
                    var state = JsonSerializer.Deserialize<SyncStateData>(json);

                    if (state != null)
                    {
                        _logger.LogDebug("Loaded sync state: v{Version}, NeedsDownload={NeedsDownload}",
                            state.LocalVersion, state.NeedsDownload);
                        return state;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading sync state, starting fresh");
            }

            _logger.LogInformation("No sync state found - first run");
            return new SyncStateData();
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_stateFilePath, json);
                _logger.LogDebug("Sync state saved");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving sync state");
            }
        }

        // ===== INNER CLASS =====

        private class SyncStateData
        {
            public int LocalVersion { get; set; } = 0;
            public int RemoteVersion { get; set; } = 0;
            public bool NeedsDownload { get; set; } = true; // Default true for first run
            public DateTime? LastSyncTime { get; set; }
        }
    }
}