using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace NewwaysAdmin.IO.Manager
{
    public class FileWatcherService
    {
        private readonly ILogger<FileWatcherService> _logger;
        private readonly IOManager _ioManager;
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
        private bool _isStarted;

        public FileWatcherService(ILogger<FileWatcherService> logger, IOManager ioManager)
        {
            _logger = logger;
            _ioManager = ioManager ?? throw new ArgumentNullException(nameof(ioManager));
        }

        public void Start()
        {
            if (_isStarted) return; // Line 28
            _isStarted = true;

            // Fix: Use IOManager.NetworkBaseFolder instead of _ioManager.NetworkBaseFolder
            SetupWatchers(_ioManager.IsServer ? IOManager.NetworkBaseFolder : _ioManager.LocalBaseFolder);
            _logger.LogInformation("FileWatcherService started as singleton");
        }

        private void SetupWatchers(string baseFolder)
        {
            if (!Directory.Exists(baseFolder)) Directory.CreateDirectory(baseFolder);
            if (_watchers.ContainsKey(baseFolder)) return;

            var watcher = new FileSystemWatcher(baseFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Created += async (s, e) => await OnFileCreatedAsync(e.FullPath);
            _watchers[baseFolder] = watcher;

            foreach (var subDir in Directory.GetDirectories(baseFolder, "*", SearchOption.AllDirectories))
            {
                if (!_watchers.ContainsKey(subDir))
                {
                    var subWatcher = new FileSystemWatcher(subDir)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                        EnableRaisingEvents = true
                    };
                    subWatcher.Created += async (s, e) => await OnFileCreatedAsync(e.FullPath);
                    _watchers[subDir] = subWatcher;
                }
            }
        }

        private async Task OnFileCreatedAsync(string filePath)
        {
            try
            {
                await Task.Delay(1000); // Debounce
                var relativePath = Path.GetRelativePath(_ioManager.IsServer ? IOManager.NetworkBaseFolder : _ioManager.LocalBaseFolder, filePath); // Fix here too
                _ioManager.NotifyNewData(Path.GetDirectoryName(relativePath) ?? string.Empty, filePath);
                _logger.LogDebug("Notified new file: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            }
        }
    }
}
