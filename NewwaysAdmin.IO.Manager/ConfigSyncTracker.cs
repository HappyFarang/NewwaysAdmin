using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace NewwaysAdmin.IO.Manager
{
    public class ConfigSyncTracker
    {
        private readonly string _trackingFilePath;
        private Dictionary<string, DateTime> _lastKnownModifications;
        private readonly ILogger _logger;

        // Add this property to check if there are pending updates
        public bool HasPendingUpdates
        {
            get
            {
                try
                {
                    var directory = Path.GetDirectoryName(_trackingFilePath);
                    if (string.IsNullOrEmpty(directory))
                    {
                        _logger.LogWarning("Invalid tracking file path: {Path}", _trackingFilePath);
                        return false;
                    }

                    var networkConfigPath = Path.Combine(directory, "Config");
                    if (!Directory.Exists(networkConfigPath)) return false;

                    // Check if any files in the network config are newer than our tracking
                    return Directory.GetFiles(networkConfigPath, "*.*", SearchOption.AllDirectories)
                        .Any(file =>
                        {
                            var relativePath = Path.GetRelativePath(networkConfigPath, file);
                            var serverModTime = File.GetLastWriteTimeUtc(file);
                            return NeedsUpdate(relativePath, serverModTime);
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking for pending updates");
                    return false;
                }
            }
        }

        public ConfigSyncTracker(string localBasePath, ILogger logger)
        {
            _trackingFilePath = Path.Combine(localBasePath, "Config", ".sync-tracking");
            _logger = logger;
            _lastKnownModifications = LoadTrackingData();
        }

        private Dictionary<string, DateTime> LoadTrackingData()
        {
            try
            {
                if (File.Exists(_trackingFilePath))
                {
                    var json = File.ReadAllText(_trackingFilePath);
                    return JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(json)
                        ?? new Dictionary<string, DateTime>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load sync tracking data");
            }
            return new Dictionary<string, DateTime>();
        }

        private void SaveTrackingData()
        {
            try
            {
                var directory = Path.GetDirectoryName(_trackingFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                var json = JsonConvert.SerializeObject(_lastKnownModifications, Formatting.Indented);
                File.WriteAllText(_trackingFilePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save sync tracking data");
            }
        }

        public bool NeedsUpdate(string filePath, DateTime serverModifiedTime)
        {
            if (_lastKnownModifications.TryGetValue(filePath, out var lastKnownTime))
            {
                return serverModifiedTime > lastKnownTime;
            }
            return true; // First time seeing this file
        }

        public void UpdateTracking(string filePath, DateTime modifiedTime)
        {
            _lastKnownModifications[filePath] = modifiedTime;
            SaveTrackingData();
        }
    }
}