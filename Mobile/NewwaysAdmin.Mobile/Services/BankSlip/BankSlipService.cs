// File: NewwaysAdmin.Mobile/Services/BankSlip/BankSlipService.cs
// Handles bank slip scanning and uploading via SignalR

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Sync;
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.Mobile.Services.BankSlip
{
    public class BankSlipService
    {
        private readonly ILogger<BankSlipService> _logger;
        private readonly SyncCoordinator _syncCoordinator;
        private readonly ProcessedSlipsTracker _tracker;
        private readonly BankSlipSettingsService _settingsService;
        private readonly string _username;

        public event EventHandler<SlipUploadedEventArgs>? SlipUploaded;
        public event EventHandler<SlipUploadFailedEventArgs>? SlipUploadFailed;

        public BankSlipService(
            ILogger<BankSlipService> logger,
            SyncCoordinator syncCoordinator,
            ProcessedSlipsTracker tracker,
            BankSlipSettingsService settingsService)
        {
            _logger = logger;
            _syncCoordinator = syncCoordinator;
            _tracker = tracker;
            _settingsService = settingsService;
            _username = Preferences.Get("Username", "Unknown");
        }

        /// <summary>
        /// Scan all monitored folders for new images and upload them
        /// </summary>
        public async Task<ScanResult> ScanAndUploadAsync()
        {
            var result = new ScanResult();
            var settings = await _settingsService.LoadSettingsAsync();

            if (!settings.IsEnabled)
            {
                _logger.LogDebug("Bank slip sync is disabled");
                return result;
            }

            foreach (var folderName in settings.MonitoredFolders)
            {
                try
                {
                    // Folder name IS the source type (kbiz, kplus, etc.)
                    var folderResult = await ScanFolderAsync(folderName, folderName, settings.SyncFromDate);
                    result.ScannedFolders++;
                    result.NewFilesFound += folderResult.NewFilesFound;
                    result.UploadedCount += folderResult.UploadedCount;
                    result.FailedCount += folderResult.FailedCount;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning folder: {Folder}", folderName);
                    result.FailedCount++;
                }
            }

            // Clean old entries periodically
            _tracker.CleanOldEntries(500);

            _logger.LogInformation(
                "Scan complete: {Scanned} folders, {Found} new files, {Uploaded} uploaded, {Failed} failed",
                result.ScannedFolders, result.NewFilesFound, result.UploadedCount, result.FailedCount);

            return result;
        }

        /// <summary>
        /// Scan a specific folder for new images
        /// </summary>
        public async Task<FolderScanResult> ScanFolderAsync(string folderName, string sourceType, DateTime syncFromDate)
        {
            var result = new FolderScanResult { FolderName = folderName };

            var folderPath = GetFolderPath(folderName);
            if (!Directory.Exists(folderPath))
            {
                _logger.LogDebug("Folder does not exist: {Path}", folderPath);
                return result;
            }

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var files = Directory.GetFiles(folderPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => File.GetCreationTimeUtc(f) >= syncFromDate)
                .Where(f => !_tracker.IsAlreadyProcessed(f))
                .ToList();

            result.NewFilesFound = files.Count;

            foreach (var file in files)
            {
                try
                {
                    var success = await UploadFileAsync(file, sourceType);
                    if (success)
                    {
                        _tracker.MarkAsProcessed(file);
                        result.UploadedCount++;
                        SlipUploaded?.Invoke(this, new SlipUploadedEventArgs(file, sourceType));
                    }
                    else
                    {
                        result.FailedCount++;
                        SlipUploadFailed?.Invoke(this, new SlipUploadFailedEventArgs(file, "Upload failed"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading file: {File}", file);
                    result.FailedCount++;
                    SlipUploadFailed?.Invoke(this, new SlipUploadFailedEventArgs(file, ex.Message));
                }
            }

            return result;
        }

        /// <summary>
        /// Upload a single file to the server
        /// </summary>
        public async Task<bool> UploadFileAsync(string filePath, string sourceType)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {Path}", filePath);
                    return false;
                }

                var bytes = await File.ReadAllBytesAsync(filePath);
                var base64 = Convert.ToBase64String(bytes);
                var fileName = Path.GetFileName(filePath);

                var request = new DocumentUploadRequest
                {
                    SourceFolder = sourceType,
                    FileName = fileName,
                    ImageBase64 = base64,
                    Username = _username
                };

                _logger.LogDebug("Uploading {FileName} ({Size} bytes) as {SourceType}",
                    fileName, bytes.Length, sourceType);

                var response = await _syncCoordinator.UploadDocumentAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("Uploaded: {FileName} -> {DocumentId}", fileName, response.DocumentId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Upload failed for {FileName}: {Error}", fileName, response.Message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Get the full path to a monitored folder
        /// </summary>
        public string GetFolderPath(string folderName)
        {
            // On Android, bank apps typically save to DCIM or Pictures
            // Common locations:
            // - /storage/emulated/0/DCIM/kbiz/
            // - /storage/emulated/0/Pictures/kbiz/
            // - /storage/emulated/0/Download/kbiz/

#if ANDROID
            var dcimPath = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryDcim)?.AbsolutePath;
            var picturesPath = Android.OS.Environment.GetExternalStoragePublicDirectory(
                Android.OS.Environment.DirectoryPictures)?.AbsolutePath;

            // Check DCIM first (most banking apps use this)
            var dcimFolder = Path.Combine(dcimPath ?? "", folderName);
            if (Directory.Exists(dcimFolder))
                return dcimFolder;

            // Check Pictures
            var picturesFolder = Path.Combine(picturesPath ?? "", folderName);
            if (Directory.Exists(picturesFolder))
                return picturesFolder;

            // Default to DCIM
            return dcimFolder;
#else
            // Windows/other platforms - use Pictures folder
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                folderName);
#endif
        }

        /// <summary>
        /// Check if a folder exists
        /// </summary>
        public bool FolderExists(string folderName)
        {
            var path = GetFolderPath(folderName);
            return Directory.Exists(path);
        }

        /// <summary>
        /// Get count of pending files in a folder
        /// </summary>
        public int GetPendingCount(string folderName, DateTime syncFromDate)
        {
            var folderPath = GetFolderPath(folderName);
            if (!Directory.Exists(folderPath))
                return 0;

            var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var count = Directory.GetFiles(folderPath)
                .Where(f => imageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Where(f => File.GetCreationTimeUtc(f) >= syncFromDate)
                .Count(f => !_tracker.IsAlreadyProcessed(f));

            return count;
        }
    }

    // Event args
    public class SlipUploadedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public string SourceType { get; }

        public SlipUploadedEventArgs(string filePath, string sourceType)
        {
            FilePath = filePath;
            SourceType = sourceType;
        }
    }

    public class SlipUploadFailedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public string Error { get; }

        public SlipUploadFailedEventArgs(string filePath, string error)
        {
            FilePath = filePath;
            Error = error;
        }
    }

    // Result classes
    public class ScanResult
    {
        public int ScannedFolders { get; set; }
        public int NewFilesFound { get; set; }
        public int UploadedCount { get; set; }
        public int FailedCount { get; set; }
    }

    public class FolderScanResult
    {
        public string FolderName { get; set; } = "";
        public int NewFilesFound { get; set; }
        public int UploadedCount { get; set; }
        public int FailedCount { get; set; }
    }
}