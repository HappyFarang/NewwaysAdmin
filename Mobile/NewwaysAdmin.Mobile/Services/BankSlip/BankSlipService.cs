// File: NewwaysAdmin.Mobile/Services/BankSlip/BankSlipService.cs
// Handles bank slip scanning and uploading via SignalR
// UPDATED: Added date range filtering for historical batch uploads

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.Mobile.Services.BankSlip
{
    public class BankSlipService
    {
        private readonly ILogger<BankSlipService> _logger;
        private readonly CategoryHubConnector _hubConnector;
        private readonly ProcessedSlipsTracker _tracker;
        private readonly BankSlipSettingsService _settingsService;

        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png" };

        public event EventHandler<SlipUploadedEventArgs>? SlipUploaded;
        public event EventHandler<SlipUploadFailedEventArgs>? SlipUploadFailed;
        public event EventHandler<BatchProgressEventArgs>? BatchProgress;

        public BankSlipService(
            ILogger<BankSlipService> logger,
            CategoryHubConnector hubConnector,
            ProcessedSlipsTracker tracker,
            BankSlipSettingsService settingsService)
        {
            _logger = logger;
            _hubConnector = hubConnector;
            _tracker = tracker;
            _settingsService = settingsService;
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

            foreach (var folder in settings.MonitoredFolders)
            {
                try
                {
                    await ScanFolderAsync(folder, settings.SyncFromDate, settings.SyncToDate, result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning folder {Pattern} at {Path}",
                        folder.PatternIdentifier, folder.DeviceFolderPath);
                }
            }

            return result;
        }

        /// <summary>
        /// Batch upload files within a specific date range
        /// </summary>
        public async Task<ScanResult> BatchUploadAsync(DateTime fromDate, DateTime toDate, IProgress<BatchProgressEventArgs>? progress = null)
        {
            var result = new ScanResult();
            var settings = await _settingsService.LoadSettingsAsync();

            _logger.LogInformation("Starting batch upload from {From} to {To}", fromDate, toDate);

            foreach (var folder in settings.MonitoredFolders)
            {
                try
                {
                    await ScanFolderAsync(folder, fromDate, toDate, result, progress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning folder {Pattern} at {Path}",
                        folder.PatternIdentifier, folder.DeviceFolderPath);
                }
            }

            _logger.LogInformation("Batch upload complete: {Found} found, {Uploaded} uploaded, {Failed} failed",
                result.NewFilesFound, result.UploadedCount, result.FailedCount);

            return result;
        }

        private async Task ScanFolderAsync(
            MonitoredFolder folder,
            DateTime syncFromDate,
            DateTime? syncToDate,
            ScanResult result,
            IProgress<BatchProgressEventArgs>? progress = null)
        {
            if (!Directory.Exists(folder.DeviceFolderPath))
            {
                _logger.LogWarning("Folder does not exist: {Path}", folder.DeviceFolderPath);
                return;
            }

            var files = Directory.GetFiles(folder.DeviceFolderPath)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            var eligibleFiles = new List<string>();

            // First pass: count eligible files
            foreach (var filePath in files)
            {
                var fileInfo = new FileInfo(filePath);

                // Skip if older than sync-from date
                if (fileInfo.LastWriteTime < syncFromDate)
                    continue;

                // Skip if newer than sync-to date (for historical batch uploads)
                if (syncToDate.HasValue && fileInfo.LastWriteTime > syncToDate.Value)
                    continue;

                // Skip if already processed
                if (_tracker.IsAlreadyProcessed(filePath))
                    continue;

                eligibleFiles.Add(filePath);
            }

            result.NewFilesFound += eligibleFiles.Count;
            var processedCount = 0;

            // Second pass: upload files
            foreach (var filePath in eligibleFiles)
            {
                try
                {
                    processedCount++;

                    // Report progress
                    progress?.Report(new BatchProgressEventArgs
                    {
                        CurrentFile = Path.GetFileName(filePath),
                        CurrentIndex = processedCount,
                        TotalFiles = eligibleFiles.Count,
                        FolderPattern = folder.PatternIdentifier
                    });

                    BatchProgress?.Invoke(this, new BatchProgressEventArgs
                    {
                        CurrentFile = Path.GetFileName(filePath),
                        CurrentIndex = processedCount,
                        TotalFiles = eligibleFiles.Count,
                        FolderPattern = folder.PatternIdentifier
                    });

                    // Upload the file
                    var success = await UploadFileAsync(filePath, folder.PatternIdentifier);

                    if (success)
                    {
                        _tracker.MarkAsProcessed(filePath);
                        result.UploadedCount++;
                        SlipUploaded?.Invoke(this, new SlipUploadedEventArgs(filePath, folder.PatternIdentifier));
                    }
                    else
                    {
                        result.FailedCount++;
                        SlipUploadFailed?.Invoke(this, new SlipUploadFailedEventArgs(filePath, "Upload failed"));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
                    result.FailedCount++;
                    SlipUploadFailed?.Invoke(this, new SlipUploadFailedEventArgs(filePath, ex.Message));
                }
            }
        }

        /// <summary>
        /// Get count of files pending upload within date range
        /// </summary>
        public async Task<int> GetPendingCountAsync(DateTime fromDate, DateTime? toDate = null)
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var totalCount = 0;

            foreach (var folder in settings.MonitoredFolders)
            {
                totalCount += GetPendingCountForFolder(folder.DeviceFolderPath, fromDate, toDate);
            }

            return totalCount;
        }

        /// <summary>
        /// Upload a single file to the server
        /// </summary>
        public async Task<bool> UploadFileAsync(string filePath, string patternIdentifier)
        {
            try
            {
                var fileBytes = await ReadFileBytesAsync(filePath);
                var fileName = Path.GetFileName(filePath);

                // Get file info - use LOCAL time, not UTC
                var fileInfo = new FileInfo(filePath);
                var lastWriteTime = fileInfo.Exists ? fileInfo.LastWriteTime : DateTime.Now;
                var fileSize = fileBytes.Length;

                var request = new DocumentUploadRequest
                {
                    FileName = fileName,
                    ImageBase64 = Convert.ToBase64String(fileBytes),
                    SourceFolder = patternIdentifier,
                    Username = MobileSessionState.Current?.Username ?? "Unknown",
                    DeviceTimestamp = lastWriteTime,
                    FileSizeBytes = fileSize,
                    ContentType = GetContentType(filePath)
                };

                // Use the hub connector for upload
                var response = await _hubConnector.UploadDocumentAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("✅ Uploaded {FileName} to {Pattern}", fileName, patternIdentifier);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Upload failed for {FileName}: {Message}", fileName, response.Message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception uploading {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// Read file bytes using platform-appropriate method
        /// </summary>
        private async Task<byte[]> ReadFileBytesAsync(string filePath)
        {
#if ANDROID
            _logger.LogDebug("Reading file via Java FileInputStream: {Path}", filePath);

            using var fileInputStream = new Java.IO.FileInputStream(filePath);
            using var memoryStream = new MemoryStream();

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = fileInputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                memoryStream.Write(buffer, 0, bytesRead);
            }

            _logger.LogDebug("Read {Bytes} bytes from file", memoryStream.Length);
            return memoryStream.ToArray();
#else
            return await File.ReadAllBytesAsync(filePath);
#endif
        }

        private string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Check if a folder path exists on the device
        /// </summary>
        public bool FolderExists(string folderPath)
        {
            return Directory.Exists(folderPath);
        }

        /// <summary>
        /// Get count of pending (unprocessed) images in a folder within date range
        /// </summary>
        public int GetPendingCountForFolder(string folderPath, DateTime syncFromDate, DateTime? syncToDate = null)
        {
            if (!Directory.Exists(folderPath))
                return 0;

            try
            {
                var count = 0;
                var files = Directory.GetFiles(folderPath)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var filePath in files)
                {
                    var fileInfo = new FileInfo(filePath);

                    // Skip if older than sync-from date
                    if (fileInfo.LastWriteTime < syncFromDate)
                        continue;

                    // Skip if newer than sync-to date
                    if (syncToDate.HasValue && fileInfo.LastWriteTime > syncToDate.Value)
                        continue;

                    // Skip if already processed
                    if (_tracker.IsAlreadyProcessed(filePath))
                        continue;

                    count++;
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Get count of pending images using current settings
        /// </summary>
        public int GetPendingCount(string folderPath, DateTime syncFromDate)
        {
            return GetPendingCountForFolder(folderPath, syncFromDate, null);
        }
    }

    // ===== Event Args =====

    public class SlipUploadedEventArgs : EventArgs
    {
        public string FilePath { get; }
        public string PatternIdentifier { get; }

        public SlipUploadedEventArgs(string filePath, string patternIdentifier)
        {
            FilePath = filePath;
            PatternIdentifier = patternIdentifier;
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

    public class BatchProgressEventArgs : EventArgs
    {
        public string CurrentFile { get; set; } = "";
        public int CurrentIndex { get; set; }
        public int TotalFiles { get; set; }
        public string FolderPattern { get; set; } = "";
        public double PercentComplete => TotalFiles > 0 ? (double)CurrentIndex / TotalFiles * 100 : 0;
    }

    public class ScanResult
    {
        public int NewFilesFound { get; set; }
        public int UploadedCount { get; set; }
        public int FailedCount { get; set; }
    }
}