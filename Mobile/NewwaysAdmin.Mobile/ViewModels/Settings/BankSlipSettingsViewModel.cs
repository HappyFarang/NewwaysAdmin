// File: NewwaysAdmin.Mobile/ViewModels/Settings/BankSlipSettingsViewModel.cs
// ViewModel for bank slip sync settings
// UPDATED: Added SyncToDate and batch upload support

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.BankSlip;

namespace NewwaysAdmin.Mobile.ViewModels.Settings
{
    public class BankSlipSettingsViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<BankSlipSettingsViewModel> _logger;
        private readonly BankSlipSettingsService _settingsService;
        private readonly BankSlipService _bankSlipService;
        private readonly IBankSlipMonitorControl _monitorControl;

        private bool _isEnabled;
        private DateTime _syncFromDate = DateTime.Now;
        private DateTime? _syncToDate = null;
        private bool _useDateRange = false;
        private string _statusMessage = "";
        private bool _isBatchUploading = false;
        private int _pendingCount = 0;
        private double _batchProgress = 0;
        private string _batchProgressText = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public BankSlipSettingsViewModel(
            ILogger<BankSlipSettingsViewModel> logger,
            BankSlipSettingsService settingsService,
            BankSlipService bankSlipService,
            IBankSlipMonitorControl monitorControl)
        {
            _logger = logger;
            _settingsService = settingsService;
            _bankSlipService = bankSlipService;
            _monitorControl = monitorControl;

            MonitoredFolders = new ObservableCollection<FolderItemViewModel>();

            // Subscribe to batch progress events
            _bankSlipService.BatchProgress += OnBatchProgress;
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                    _ = OnEnabledChangedAsync(value);
                }
            }
        }

        public DateTime SyncFromDate
        {
            get => _syncFromDate;
            set
            {
                if (_syncFromDate != value)
                {
                    _syncFromDate = value;
                    OnPropertyChanged();
                    _ = _settingsService.SetSyncFromDateAsync(value);
                    _ = UpdatePendingCountAsync();
                }
            }
        }

        public DateTime? SyncToDate
        {
            get => _syncToDate;
            set
            {
                if (_syncToDate != value)
                {
                    _syncToDate = value;
                    OnPropertyChanged();
                    _ = _settingsService.SetSyncToDateAsync(value);
                    _ = UpdatePendingCountAsync();
                }
            }
        }

        public bool UseDateRange
        {
            get => _useDateRange;
            set
            {
                if (_useDateRange != value)
                {
                    _useDateRange = value;
                    OnPropertyChanged();

                    // Clear or set the SyncToDate based on toggle
                    if (!value)
                    {
                        SyncToDate = null;
                    }
                    else if (_syncToDate == null)
                    {
                        SyncToDate = DateTime.Now;
                    }
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsBatchUploading
        {
            get => _isBatchUploading;
            set
            {
                _isBatchUploading = value;
                OnPropertyChanged();
            }
        }

        public int PendingCount
        {
            get => _pendingCount;
            set
            {
                _pendingCount = value;
                OnPropertyChanged();
            }
        }

        public double BatchProgress
        {
            get => _batchProgress;
            set
            {
                _batchProgress = value;
                OnPropertyChanged();
            }
        }

        public string BatchProgressText
        {
            get => _batchProgressText;
            set
            {
                _batchProgressText = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<FolderItemViewModel> MonitoredFolders { get; }

        /// <summary>
        /// Load settings from storage
        /// </summary>
        public async Task LoadAsync()
        {
            try
            {
                _logger.LogInformation("[BankSlipVM] Loading settings...");

                var settings = await _settingsService.LoadSettingsAsync();

                _isEnabled = settings.IsEnabled;
                OnPropertyChanged(nameof(IsEnabled));

                _syncFromDate = settings.SyncFromDate;
                OnPropertyChanged(nameof(SyncFromDate));

                _syncToDate = settings.SyncToDate;
                OnPropertyChanged(nameof(SyncToDate));

                _useDateRange = settings.SyncToDate.HasValue;
                OnPropertyChanged(nameof(UseDateRange));

                MonitoredFolders.Clear();
                foreach (var folder in settings.MonitoredFolders)
                {
                    var vm = new FolderItemViewModel
                    {
                        PatternIdentifier = folder.PatternIdentifier,
                        DeviceFolderPath = folder.DeviceFolderPath
                    };
                    vm.UpdateStatus();
                    MonitoredFolders.Add(vm);
                }

                await UpdatePendingCountAsync();
                UpdateStatusMessage();

                // Start monitoring if enabled
                if (_isEnabled && MonitoredFolders.Count > 0)
                {
                    _monitorControl.StartMonitoring();
                }

                _logger.LogInformation("[BankSlipVM] Loaded {Count} folders, Enabled: {Enabled}",
                    MonitoredFolders.Count, _isEnabled);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error loading settings");
                StatusMessage = $"Error loading: {ex.Message}";
            }
        }

        /// <summary>
        /// Add a folder to monitor
        /// </summary>
        public async Task AddFolderAsync(string folderPath, string patternIdentifier)
        {
            try
            {
                _logger.LogInformation("[BankSlipVM] Adding folder: {Path} as {Pattern}",
                    folderPath, patternIdentifier);

                // Check for duplicates
                if (MonitoredFolders.Any(f => f.PatternIdentifier.Equals(patternIdentifier, StringComparison.OrdinalIgnoreCase)))
                {
                    StatusMessage = $"Pattern '{patternIdentifier}' already exists";
                    return;
                }

                await _settingsService.AddFolderAsync(folderPath, patternIdentifier);

                var vm = new FolderItemViewModel
                {
                    PatternIdentifier = patternIdentifier,
                    DeviceFolderPath = folderPath
                };
                vm.UpdateStatus();
                MonitoredFolders.Add(vm);

                await UpdatePendingCountAsync();
                StatusMessage = $"Added folder: {patternIdentifier}";

                _logger.LogInformation("[BankSlipVM] ✅ Folder added successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error adding folder");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Remove a folder from monitoring
        /// </summary>
        public async Task RemoveFolderAsync(string patternIdentifier)
        {
            try
            {
                _logger.LogInformation("[BankSlipVM] Removing folder: {Pattern}", patternIdentifier);

                await _settingsService.RemoveFolderAsync(patternIdentifier);

                var folder = MonitoredFolders.FirstOrDefault(f =>
                    f.PatternIdentifier.Equals(patternIdentifier, StringComparison.OrdinalIgnoreCase));

                if (folder != null)
                {
                    MonitoredFolders.Remove(folder);
                }

                await UpdatePendingCountAsync();
                StatusMessage = $"Removed folder: {patternIdentifier}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error removing folder");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Perform batch upload of historical files
        /// </summary>
        public async Task<ScanResult> BatchUploadAsync()
        {
            if (IsBatchUploading)
            {
                _logger.LogWarning("[BankSlipVM] Batch upload already in progress");
                return new ScanResult();
            }

            try
            {
                IsBatchUploading = true;
                BatchProgress = 0;
                BatchProgressText = "Starting...";

                var fromDate = SyncFromDate;
                var toDate = SyncToDate ?? DateTime.Now;

                _logger.LogInformation("[BankSlipVM] Starting batch upload from {From} to {To}", fromDate, toDate);
                StatusMessage = $"Uploading files from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}...";

                var progress = new Progress<BatchProgressEventArgs>(args =>
                {
                    BatchProgress = args.PercentComplete;
                    BatchProgressText = $"{args.CurrentIndex}/{args.TotalFiles}: {args.CurrentFile}";
                });

                var result = await _bankSlipService.BatchUploadAsync(fromDate, toDate, progress);

                StatusMessage = $"✅ Uploaded {result.UploadedCount} of {result.NewFilesFound} files";
                if (result.FailedCount > 0)
                {
                    StatusMessage += $" ({result.FailedCount} failed)";
                }

                await UpdatePendingCountAsync();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error during batch upload");
                StatusMessage = $"Error: {ex.Message}";
                return new ScanResult();
            }
            finally
            {
                IsBatchUploading = false;
                BatchProgress = 0;
                BatchProgressText = "";
            }
        }

        /// <summary>
        /// Manual scan and upload
        /// </summary>
        public async Task<ScanResult> ScanNowAsync()
        {
            try
            {
                _logger.LogInformation("[BankSlipVM] Manual scan triggered");
                StatusMessage = "Scanning...";

                var result = await _bankSlipService.ScanAndUploadAsync();

                if (result.NewFilesFound == 0)
                {
                    StatusMessage = "No new files found";
                }
                else
                {
                    StatusMessage = $"Uploaded {result.UploadedCount} of {result.NewFilesFound} files";
                    if (result.FailedCount > 0)
                    {
                        StatusMessage += $" ({result.FailedCount} failed)";
                    }
                }

                await UpdatePendingCountAsync();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error during manual scan");
                StatusMessage = $"Error: {ex.Message}";
                return new ScanResult();
            }
        }

        /// <summary>
        /// Update the pending file count
        /// </summary>
        public async Task UpdatePendingCountAsync()
        {
            try
            {
                var toDate = UseDateRange ? SyncToDate : null;
                PendingCount = await _bankSlipService.GetPendingCountAsync(SyncFromDate, toDate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error getting pending count");
                PendingCount = 0;
            }
        }

        private void OnBatchProgress(object? sender, BatchProgressEventArgs e)
        {
            BatchProgress = e.PercentComplete;
            BatchProgressText = $"{e.CurrentIndex}/{e.TotalFiles}: {e.CurrentFile}";
        }

        private void UpdateStatusMessage()
        {
            if (_isEnabled && MonitoredFolders.Count > 0)
            {
                StatusMessage = $"Monitoring {MonitoredFolders.Count} folder(s). New files are detected automatically.";
            }
            else
            {
                StatusMessage = "Enable sync to start monitoring for new bank slips.";
            }
        }

        private async Task OnEnabledChangedAsync(bool enabled)
        {
            try
            {
                _logger.LogInformation("[BankSlipVM] Sync toggled: {Enabled}", enabled);

                if (enabled)
                {
                    // Request storage permission first
                    var hasPermission = await EnsureStoragePermissionAsync();
                    if (!hasPermission)
                    {
                        _logger.LogWarning("[BankSlipVM] Storage permission denied");

                        // Revert toggle without triggering this method again
                        _isEnabled = false;
                        OnPropertyChanged(nameof(IsEnabled));

                        StatusMessage = "⚠️ Storage permission required to monitor folders";

                        await Shell.Current.DisplayAlert(
                            "Permission Required",
                            "To monitor bank slip folders, please grant access to photos and media when prompted. You can also enable this in Settings > Apps > NewwaysAdmin > Permissions.",
                            "OK");

                        return;
                    }

                    await _settingsService.SetEnabledAsync(true);

                    if (MonitoredFolders.Count > 0)
                    {
                        _monitorControl.StartMonitoring();
                        StatusMessage = $"Monitoring {MonitoredFolders.Count} folder(s) for new bank slips";
                        _logger.LogInformation("[BankSlipVM] ✅ Real-time monitoring STARTED");
                    }
                    else
                    {
                        StatusMessage = "Add folders to monitor first";
                    }
                }
                else
                {
                    await _settingsService.SetEnabledAsync(false);
                    _monitorControl.StopMonitoring();
                    StatusMessage = "Bank slip sync disabled";
                    _logger.LogInformation("[BankSlipVM] ⏹️ Real-time monitoring STOPPED");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error toggling sync");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task<bool> EnsureStoragePermissionAsync()
        {
            try
            {
#if ANDROID
                var status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();

                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.StorageRead>();
                }

                return status == PermissionStatus.Granted;
#else
                return true;
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error checking permissions");
                return false;
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ===== Folder Item ViewModel =====

    public class FolderItemViewModel : INotifyPropertyChanged
    {
        private string _statusText = "";
        private bool _exists = false;

        public string PatternIdentifier { get; set; } = "";
        public string DeviceFolderPath { get; set; } = "";

        public string FolderDisplayName => Path.GetFileName(DeviceFolderPath);

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        public bool Exists
        {
            get => _exists;
            set
            {
                _exists = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Exists)));
            }
        }

        public void UpdateStatus()
        {
            Exists = Directory.Exists(DeviceFolderPath);
            StatusText = Exists ? "✓ Ready" : "⚠️ Not found";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ===== Platform-specific helper =====

    public static class BankSlipWorkerHelper
    {
        public static void StartWorkerIfEnabled()
        {
#if ANDROID
            // TODO: Start Android WorkManager worker
            System.Diagnostics.Debug.WriteLine("[BankSlipWorkerHelper] Starting Android worker...");
#endif
        }

        public static void StopWorker()
        {
#if ANDROID
            // TODO: Stop Android WorkManager worker
            System.Diagnostics.Debug.WriteLine("[BankSlipWorkerHelper] Stopping Android worker...");
#endif
        }
    }
}