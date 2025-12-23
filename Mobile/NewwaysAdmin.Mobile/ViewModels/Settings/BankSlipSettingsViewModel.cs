// File: NewwaysAdmin.Mobile/ViewModels/Settings/BankSlipSettingsViewModel.cs
// ViewModel for bank slip sync settings - matches SettingsPage.xaml.cs expectations

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
        private readonly IBankSlipMonitorControl _monitorControl;

        private bool _isEnabled;
        private DateTime _syncFromDate = DateTime.Now;
        private string _statusMessage = "";

        public event PropertyChangedEventHandler? PropertyChanged;

        public BankSlipSettingsViewModel(
            ILogger<BankSlipSettingsViewModel> logger,
            BankSlipSettingsService settingsService,
            IBankSlipMonitorControl monitorControl)
        {
            _logger = logger;
            _settingsService = settingsService;
            _monitorControl = monitorControl;

            MonitoredFolders = new ObservableCollection<FolderItemViewModel>();
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
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
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

                UpdateStatusMessage();

                // Refresh file watchers
                if (_isEnabled)
                {
                    _monitorControl.RefreshWatchedFolders();
                }

                _logger.LogInformation("[BankSlipVM] ✅ Added folder: {Pattern}", patternIdentifier);
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
        public async Task RemoveFolderAsync(FolderItemViewModel folder)
        {
            try
            {
                _logger.LogInformation("[BankSlipVM] Removing folder: {Pattern}", folder.PatternIdentifier);

                await _settingsService.RemoveFolderAsync(folder.PatternIdentifier);
                MonitoredFolders.Remove(folder);

                UpdateStatusMessage();

                // Refresh file watchers
                if (_isEnabled)
                {
                    _monitorControl.RefreshWatchedFolders();
                }

                _logger.LogInformation("[BankSlipVM] ✅ Removed folder: {Pattern}", folder.PatternIdentifier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error removing folder");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Manual scan (placeholder - real-time monitoring handles this now)
        /// </summary>
        public Task ScanNowAsync()
        {
            _logger.LogInformation("[BankSlipVM] Manual scan requested");

            if (_isEnabled)
            {
                StatusMessage = "Real-time monitoring is active. New files are detected automatically.";
            }
            else
            {
                StatusMessage = "Enable sync to start monitoring for new bank slips.";
            }

            return Task.CompletedTask;
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
                PermissionStatus status;

                if (OperatingSystem.IsAndroidVersionAtLeast(33))
                {
                    // Android 13+ needs READ_MEDIA_IMAGES
                    _logger.LogInformation("[BankSlipVM] Checking Photos permission (Android 13+)");
                    status = await Permissions.CheckStatusAsync<Permissions.Photos>();

                    if (status != PermissionStatus.Granted)
                    {
                        _logger.LogInformation("[BankSlipVM] Requesting Photos permission...");
                        status = await Permissions.RequestAsync<Permissions.Photos>();
                    }
                }
                else
                {
                    // Android 12 and below needs READ_EXTERNAL_STORAGE
                    _logger.LogInformation("[BankSlipVM] Checking StorageRead permission (Android 12-)");
                    status = await Permissions.CheckStatusAsync<Permissions.StorageRead>();

                    if (status != PermissionStatus.Granted)
                    {
                        _logger.LogInformation("[BankSlipVM] Requesting StorageRead permission...");
                        status = await Permissions.RequestAsync<Permissions.StorageRead>();
                    }
                }

                _logger.LogInformation("[BankSlipVM] Permission status: {Status}", status);
                return status == PermissionStatus.Granted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BankSlipVM] Error requesting permission");
                return false;
            }
        }

        private void UpdateStatusMessage()
        {
            if (!_isEnabled)
            {
                StatusMessage = "Bank slip sync disabled";
            }
            else if (MonitoredFolders.Count == 0)
            {
                StatusMessage = "No folders configured - add a folder to start";
            }
            else
            {
                var validCount = MonitoredFolders.Count(f => f.Exists);
                if (validCount == MonitoredFolders.Count)
                {
                    StatusMessage = $"Monitoring {MonitoredFolders.Count} folder(s) for new bank slips";
                }
                else
                {
                    StatusMessage = $"⚠️ {MonitoredFolders.Count - validCount} folder(s) not accessible";
                }
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ViewModel for a monitored folder item
    /// </summary>
    public class FolderItemViewModel : INotifyPropertyChanged
    {
        public string PatternIdentifier { get; set; } = "";
        public string DeviceFolderPath { get; set; } = "";

        public string FolderDisplayName => string.IsNullOrEmpty(DeviceFolderPath)
            ? "Not configured"
            : TruncatePath(DeviceFolderPath);

        public bool Exists { get; private set; }
        public string StatusText { get; private set; } = "";

        public void UpdateStatus()
        {
            Exists = !string.IsNullOrEmpty(DeviceFolderPath) && Directory.Exists(DeviceFolderPath);
            StatusText = Exists ? "✓ Ready" : "⚠️ Not found";
            OnPropertyChanged(nameof(Exists));
            OnPropertyChanged(nameof(StatusText));
        }

        private static string TruncatePath(string path)
        {
            if (path.Length <= 35) return path;

            // Show last part of path
            var parts = path.Split('/');
            if (parts.Length <= 2) return path;

            return ".../" + string.Join("/", parts.TakeLast(2));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}