// File: NewwaysAdmin.Mobile/Platforms/Android/Services/BankSlipFileObserver.cs
// Real-time file system watcher for bank slip folders

using Android.OS;
using Android.Runtime;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Platforms.Android.Services
{
    /// <summary>
    /// Watches a folder for new files using Android's FileObserver
    /// </summary>
    public class BankSlipFileObserver : FileObserver
    {
        private readonly ILogger? _logger;
        private readonly string _folderPath;
        private readonly string _sourceType;
        private readonly Action<string, string>? _onNewFile;

        // Watch for file creation and close-write (file finished writing)
        private const FileObserverEvents WatchEvents =
            FileObserverEvents.CloseWrite |
            FileObserverEvents.MovedTo;

        // Required for Java interop - DO NOT REMOVE
        protected BankSlipFileObserver(IntPtr javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
            _folderPath = string.Empty;
            _sourceType = string.Empty;
            _onNewFile = null;
            _logger = null;
        }

        public BankSlipFileObserver(
            string folderPath,
            string sourceType,
            Action<string, string> onNewFile,
            ILogger? logger = null)
            : base(folderPath, WatchEvents)
        {
            _folderPath = folderPath;
            _sourceType = sourceType;
            _onNewFile = onNewFile;
            _logger = logger;
        }

        public override void OnEvent(FileObserverEvents e, string? path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            // Only process image files
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                return;

            var fullPath = Path.Combine(_folderPath, path);

            _logger?.LogInformation(
                "[FileObserver] New file detected: {Path} (Event: {Event})",
                fullPath, e);

            try
            {
                _onNewFile?.Invoke(_sourceType, fullPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[FileObserver] Error handling new file: {Path}", fullPath);
            }
        }

        public string FolderPath => _folderPath;
        public string SourceType => _sourceType;
    }

    /// <summary>
    /// Manages multiple FileObservers for different bank folders
    /// </summary>
    public class BankSlipObserverManager : IDisposable
    {
        private readonly ILogger<BankSlipObserverManager> _logger;
        private readonly Dictionary<string, BankSlipFileObserver> _observers = new();
        private readonly object _lock = new();
        private Action<string, string>? _onNewFile;

        public BankSlipObserverManager(ILogger<BankSlipObserverManager> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Set the callback for when new files are detected
        /// </summary>
        public void SetNewFileCallback(Action<string, string> callback)
        {
            _onNewFile = callback;
        }

        /// <summary>
        /// Start watching a folder
        /// </summary>
        public bool StartWatching(string sourceType, string folderPath)
        {
            lock (_lock)
            {
                // Stop existing observer for this source type if any
                StopWatching(sourceType);

                if (!Directory.Exists(folderPath))
                {
                    _logger.LogWarning(
                        "[ObserverManager] Folder does not exist: {Path}", folderPath);
                    return false;
                }

                try
                {
                    var observer = new BankSlipFileObserver(
                        folderPath,
                        sourceType,
                        (src, path) => _onNewFile?.Invoke(src, path),
                        _logger);

                    observer.StartWatching();
                    _observers[sourceType] = observer;

                    _logger.LogInformation(
                        "[ObserverManager] Started watching {SourceType}: {Path}",
                        sourceType, folderPath);

                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[ObserverManager] Failed to start observer for {SourceType}: {Path}",
                        sourceType, folderPath);
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop watching a specific source type
        /// </summary>
        public void StopWatching(string sourceType)
        {
            lock (_lock)
            {
                if (_observers.TryGetValue(sourceType, out var observer))
                {
                    observer.StopWatching();
                    observer.Dispose();
                    _observers.Remove(sourceType);

                    _logger.LogInformation(
                        "[ObserverManager] Stopped watching {SourceType}", sourceType);
                }
            }
        }

        /// <summary>
        /// Stop all observers
        /// </summary>
        public void StopAll()
        {
            lock (_lock)
            {
                foreach (var kvp in _observers)
                {
                    kvp.Value.StopWatching();
                    kvp.Value.Dispose();
                    _logger.LogInformation(
                        "[ObserverManager] Stopped watching {SourceType}", kvp.Key);
                }
                _observers.Clear();
            }
        }

        /// <summary>
        /// Get list of currently watched folders
        /// </summary>
        public IReadOnlyDictionary<string, string> GetWatchedFolders()
        {
            lock (_lock)
            {
                return _observers.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.FolderPath);
            }
        }

        public bool IsWatching(string sourceType)
        {
            lock (_lock)
            {
                return _observers.ContainsKey(sourceType);
            }
        }

        public int WatcherCount
        {
            get
            {
                lock (_lock)
                {
                    return _observers.Count;
                }
            }
        }

        public void Dispose()
        {
            StopAll();
        }
    }
}