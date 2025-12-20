// File: NewwaysAdmin.Mobile/ViewModels/BankSlipSettingsViewModel.cs
// ViewModel for bank slip auto-sync settings section in SettingsPage

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NewwaysAdmin.Mobile.Services.BankSlip;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class BankSlipSettingsViewModel : INotifyPropertyChanged
    {
        private readonly BankSlipSettingsService _settingsService;
        private readonly BankSlipService _bankSlipService;

        private bool _isEnabled;
        private DateTime _syncFromDate = DateTime.UtcNow;
        private bool _isLoading;
        private bool _isScanning;
        private string _statusMessage = "";

        public ObservableCollection<FolderItemViewModel> MonitoredFolders { get; } = new();

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

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public BankSlipSettingsViewModel(
            BankSlipSettingsService settingsService,
            BankSlipService bankSlipService)
        {
            _settingsService = settingsService;
            _bankSlipService = bankSlipService;
        }

        public async Task LoadAsync()
        {
            IsLoading = true;
            try
            {
                var settings = await _settingsService.LoadSettingsAsync();

                _isEnabled = settings.IsEnabled;
                _syncFromDate = settings.SyncFromDate;

                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(SyncFromDate));

                MonitoredFolders.Clear();
                foreach (var folderName in settings.MonitoredFolders)
                {
                    var option = BankSlipSettingsService.AvailableFolders
                        .FirstOrDefault(f => f.FolderName == folderName);

                    var exists = _bankSlipService.FolderExists(folderName);
                    var pending = exists
                        ? _bankSlipService.GetPendingCount(folderName, settings.SyncFromDate)
                        : 0;

                    MonitoredFolders.Add(new FolderItemViewModel
                    {
                        FolderName = folderName,
                        DisplayName = option?.DisplayName ?? folderName,
                        Description = option?.Description ?? "",
                        Exists = exists,
                        PendingCount = pending
                    });
                }

                StatusMessage = MonitoredFolders.Count > 0
                    ? $"{MonitoredFolders.Count} folder(s) configured"
                    : "No folders configured";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task AddFolderAsync(string folderName)
        {
            if (MonitoredFolders.Any(f => f.FolderName == folderName))
            {
                StatusMessage = "Folder already added";
                return;
            }

            await _settingsService.AddFolderAsync(folderName);

            var option = BankSlipSettingsService.AvailableFolders
                .FirstOrDefault(f => f.FolderName == folderName);

            var exists = _bankSlipService.FolderExists(folderName);

            MonitoredFolders.Add(new FolderItemViewModel
            {
                FolderName = folderName,
                DisplayName = option?.DisplayName ?? folderName,
                Description = option?.Description ?? "",
                Exists = exists,
                PendingCount = 0
            });

            StatusMessage = exists
                ? $"Added {folderName}"
                : $"Added {folderName} (folder not found)";
        }

        public async Task RemoveFolderAsync(FolderItemViewModel folder)
        {
            await _settingsService.RemoveFolderAsync(folder.FolderName);
            MonitoredFolders.Remove(folder);
            StatusMessage = $"Removed {folder.DisplayName}";
        }

        public async Task ScanNowAsync()
        {
            IsScanning = true;
            StatusMessage = "Scanning...";

            try
            {
                var result = await _bankSlipService.ScanAndUploadAsync();

                StatusMessage = result.NewFilesFound == 0
                    ? "No new files found"
                    : $"Found {result.NewFilesFound}, uploaded {result.UploadedCount}, failed {result.FailedCount}";

                // Refresh pending counts
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// Get folders that haven't been added yet
        /// </summary>
        public List<FolderOption> GetAvailableFoldersToAdd()
        {
            var added = MonitoredFolders.Select(f => f.FolderName).ToHashSet();
            return BankSlipSettingsService.AvailableFolders
                .Where(f => !added.Contains(f.FolderName))
                .ToList();
        }

        private async Task OnEnabledChangedAsync(bool enabled)
        {
            await _settingsService.SetEnabledAsync(enabled);

            // Platform-specific worker management
            BankSlipWorkerHelper.SetWorkerEnabled(enabled);

            StatusMessage = enabled ? "Auto-sync enabled" : "Auto-sync disabled";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class FolderItemViewModel : INotifyPropertyChanged
    {
        private int _pendingCount;

        public string FolderName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Exists { get; set; }

        public int PendingCount
        {
            get => _pendingCount;
            set { _pendingCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        public string StatusText
        {
            get
            {
                if (!Exists) return "⚠️ folder not found";
                if (PendingCount > 0) return $"📤 {PendingCount} pending";
                return "✓";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Cross-platform helper for managing background worker
    /// </summary>
    public static class BankSlipWorkerHelper
    {
        public static void SetWorkerEnabled(bool enabled)
        {
#if ANDROID
            var context = Android.App.Application.Context;
            if (enabled)
            {
                Platforms.Android.Services.BankSlipWorkerManager.EnqueueMonitorWorker(context);
            }
            else
            {
                Platforms.Android.Services.BankSlipWorkerManager.CancelMonitorWorker(context);
            }
#else
            // Windows: No background worker - manual scan only
            System.Diagnostics.Debug.WriteLine($"BankSlip worker enabled: {enabled} (Windows - no background monitoring)");
#endif
        }

        public static void StartWorkerIfEnabled()
        {
#if ANDROID
            Task.Run(async () =>
            {
                try
                {
                    var settingsService = new BankSlipSettingsService();
                    var settings = await settingsService.LoadSettingsAsync();

                    if (settings.IsEnabled)
                    {
                        var context = Android.App.Application.Context;
                        Platforms.Android.Services.BankSlipWorkerManager.EnqueueMonitorWorker(context);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start bank slip worker: {ex.Message}");
                }
            });
#endif
        }
    }
}