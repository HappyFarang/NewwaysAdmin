// File: NewwaysAdmin.Mobile/Pages/SettingsPage.xaml.cs
// UPDATED: Added date range and batch upload support

using NewwaysAdmin.Mobile.Config;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.ViewModels.Settings;
using NewwaysAdmin.Mobile.Services.BankSlip;

#if ANDROID
using Android.Content;
using Android.App;
using Android.Provider;
#endif

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class SettingsPage : ContentPage
    {
        private readonly CredentialStorageService _credentialStorage;
        private readonly BankSlipSettingsViewModel _bankSlipViewModel;

        // Holds the selected folder path from the picker
        private string? _selectedFolderPath;

        public SettingsPage(
            CredentialStorageService credentialStorage,
            BankSlipSettingsViewModel bankSlipViewModel)
        {
            InitializeComponent();
            _credentialStorage = credentialStorage;
            _bankSlipViewModel = bankSlipViewModel;
            ServerLabel.Text = AppConfig.ServerUrl;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadUserDataAsync();
            await LoadBankSlipSettingsAsync();
        }

        // ===== USER DATA =====

        private async Task LoadUserDataAsync()
        {
            try
            {
                var creds = await _credentialStorage.GetSavedCredentialsAsync();
                UsernameLabel.Text = creds?.Username ?? "Not logged in";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading user data: {ex.Message}");
                UsernameLabel.Text = "Error loading";
            }
        }

        // ===== BANK SLIP SETTINGS =====

        private async Task LoadBankSlipSettingsAsync()
        {
            try
            {
                BankSlipStatusLabel.Text = "Loading...";

                await _bankSlipViewModel.LoadAsync();

                // Update UI from ViewModel
                EnabledSwitch.IsToggled = _bankSlipViewModel.IsEnabled;
                SyncFromDatePicker.Date = _bankSlipViewModel.SyncFromDate;

                // Date range settings
                DateRangeSwitch.IsToggled = _bankSlipViewModel.UseDateRange;
                SyncToDateContainer.IsVisible = _bankSlipViewModel.UseDateRange;
                if (_bankSlipViewModel.SyncToDate.HasValue)
                {
                    SyncToDatePicker.Date = _bankSlipViewModel.SyncToDate.Value;
                }
                else
                {
                    SyncToDatePicker.Date = DateTime.Now;
                }

                RefreshFolderList();
                ClearAddFolderForm();
                UpdatePendingCountDisplay();

                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading bank slip settings: {ex.Message}");
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private void ClearAddFolderForm()
        {
            _selectedFolderPath = null;
            SelectedFolderLabel.Text = "No folder selected";
            PatternNameEntry.Text = "";
        }

        private void RefreshFolderList()
        {
            // Clear existing folder items (keep the NoFoldersLabel)
            var toRemove = FolderListContainer.Children
                .Where(c => c is Frame)
                .ToList();

            foreach (var item in toRemove)
            {
                FolderListContainer.Children.Remove(item);
            }

            // Show/hide the "no folders" label
            NoFoldersLabel.IsVisible = !_bankSlipViewModel.MonitoredFolders.Any();

            // Add folder items
            foreach (var folder in _bankSlipViewModel.MonitoredFolders)
            {
                var frame = CreateFolderItemView(folder);
                FolderListContainer.Children.Add(frame);
            }
        }

        private Frame CreateFolderItemView(FolderItemViewModel folder)
        {
            var statusColor = folder.Exists ? Colors.Green : Colors.Orange;

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto }
                }
            };

            var infoStack = new StackLayout { Spacing = 2 };
            infoStack.Children.Add(new Label
            {
                Text = folder.PatternIdentifier,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            });
            infoStack.Children.Add(new Label
            {
                Text = folder.DeviceFolderPath,
                FontSize = 10,
                TextColor = Colors.Gray,
                LineBreakMode = LineBreakMode.TailTruncation
            });
            infoStack.Children.Add(new Label
            {
                Text = folder.StatusText,
                FontSize = 10,
                TextColor = statusColor
            });

            Grid.SetColumn(infoStack, 0);
            grid.Children.Add(infoStack);

            var deleteButton = new Button
            {
                Text = "🗑️",
                BackgroundColor = Colors.Transparent,
                TextColor = Colors.Red,
                FontSize = 18,
                WidthRequest = 40,
                HeightRequest = 40,
                VerticalOptions = LayoutOptions.Center
            };
            deleteButton.Clicked += async (s, e) => await OnDeleteFolderClicked(folder.PatternIdentifier);

            Grid.SetColumn(deleteButton, 1);
            grid.Children.Add(deleteButton);

            return new Frame
            {
                BackgroundColor = Colors.White,
                Padding = new Thickness(10),
                CornerRadius = 5,
                Content = grid
            };
        }

        private void UpdatePendingCountDisplay()
        {
            var count = _bankSlipViewModel.PendingCount;
            PendingCountLabel.Text = count == 1
                ? "1 file pending"
                : $"{count} files pending";
        }

        // ===== EVENT HANDLERS =====

        private void OnEnabledToggled(object? sender, ToggledEventArgs e)
        {
            _bankSlipViewModel.IsEnabled = e.Value;
            BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
        }

        private void OnSyncFromDateChanged(object? sender, DateChangedEventArgs e)
        {
            _bankSlipViewModel.SyncFromDate = e.NewDate;
            UpdatePendingCountDisplay();
        }

        private void OnSyncToDateChanged(object? sender, DateChangedEventArgs e)
        {
            _bankSlipViewModel.SyncToDate = e.NewDate;
            UpdatePendingCountDisplay();
        }

        private void OnDateRangeToggled(object? sender, ToggledEventArgs e)
        {
            _bankSlipViewModel.UseDateRange = e.Value;
            SyncToDateContainer.IsVisible = e.Value;

            if (e.Value && _bankSlipViewModel.SyncToDate == null)
            {
                SyncToDatePicker.Date = DateTime.Now;
            }

            UpdatePendingCountDisplay();
        }

        private async void OnBrowseFolderClicked(object? sender, EventArgs e)
        {
            try
            {
#if ANDROID
                // Use the FolderPickerService for proper async folder selection
                var folderPicker = new NewwaysAdmin.Mobile.Platforms.Android.Services.FolderPickerService();
                var path = await folderPicker.PickFolderAsync();

                if (!string.IsNullOrEmpty(path))
                {
                    _selectedFolderPath = path;
                    SelectedFolderLabel.Text = path;

                    // Auto-suggest pattern name based on folder
                    if (string.IsNullOrEmpty(PatternNameEntry.Text))
                    {
                        var folderName = Path.GetFileName(path);
                        PatternNameEntry.Text = folderName?.Replace(" ", "") ?? "";
                    }

                    System.Diagnostics.Debug.WriteLine($"[FolderPicker] Selected: {path}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[FolderPicker] No folder selected or cancelled");
                }
#else
                // Fallback for non-Android
                var result = await DisplayPromptAsync(
                    "Enter Folder Path",
                    "Enter the full path to the screenshot folder:",
                    placeholder: "/path/to/folder",
                    keyboard: Keyboard.Text);

                if (!string.IsNullOrWhiteSpace(result))
                {
                    _selectedFolderPath = result.Trim();
                    SelectedFolderLabel.Text = _selectedFolderPath;
                }
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error browsing folder: {ex.Message}");
                await DisplayAlert("Error", $"Could not open folder picker: {ex.Message}", "OK");
            }
        }

        private async void OnAddFolderClicked(object? sender, EventArgs e)
        {
            try
            {
                var pattern = PatternNameEntry.Text?.Trim();

                if (string.IsNullOrWhiteSpace(_selectedFolderPath))
                {
                    await DisplayAlert("Missing Folder", "Please select a folder first", "OK");
                    return;
                }

                if (string.IsNullOrWhiteSpace(pattern))
                {
                    await DisplayAlert("Missing Pattern", "Please enter a pattern name (e.g., KPLUS_Thomas)", "OK");
                    return;
                }

                await _bankSlipViewModel.AddFolderAsync(_selectedFolderPath, pattern);

                RefreshFolderList();
                ClearAddFolderForm();
                UpdatePendingCountDisplay();

                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding folder: {ex.Message}");
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task OnDeleteFolderClicked(string patternIdentifier)
        {
            var confirm = await DisplayAlert(
                "Remove Folder",
                $"Remove '{patternIdentifier}' from monitoring?",
                "Remove", "Cancel");

            if (confirm)
            {
                await _bankSlipViewModel.RemoveFolderAsync(patternIdentifier);
                RefreshFolderList();
                UpdatePendingCountDisplay();
                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
        }

        private async void OnBatchUploadClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_bankSlipViewModel.IsBatchUploading)
                {
                    await DisplayAlert("In Progress", "Batch upload is already running", "OK");
                    return;
                }

                var pendingCount = _bankSlipViewModel.PendingCount;
                if (pendingCount == 0)
                {
                    await DisplayAlert("No Files", "No pending files to upload in the selected date range", "OK");
                    return;
                }

                var fromDate = _bankSlipViewModel.SyncFromDate;
                var toDate = _bankSlipViewModel.SyncToDate ?? DateTime.Now;

                var confirm = await DisplayAlert(
                    "Batch Upload",
                    $"Upload {pendingCount} files from {fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}?",
                    "Upload", "Cancel");

                if (!confirm) return;

                // Show progress UI
                BatchProgressContainer.IsVisible = true;
                BatchUploadButton.IsEnabled = false;
                ScanNowButton.IsEnabled = false;

                // Subscribe to progress updates
                _bankSlipViewModel.PropertyChanged += OnBatchProgressChanged;

                try
                {
                    var result = await _bankSlipViewModel.BatchUploadAsync();

                    await DisplayAlert(
                        "Batch Complete",
                        $"Uploaded: {result.UploadedCount}\nFailed: {result.FailedCount}",
                        "OK");
                }
                finally
                {
                    _bankSlipViewModel.PropertyChanged -= OnBatchProgressChanged;

                    // Hide progress UI
                    BatchProgressContainer.IsVisible = false;
                    BatchUploadButton.IsEnabled = true;
                    ScanNowButton.IsEnabled = true;
                }

                UpdatePendingCountDisplay();
                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in batch upload: {ex.Message}");
                await DisplayAlert("Error", ex.Message, "OK");

                BatchProgressContainer.IsVisible = false;
                BatchUploadButton.IsEnabled = true;
                ScanNowButton.IsEnabled = true;
            }
        }

        private void OnBatchProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_bankSlipViewModel.BatchProgress))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    BatchProgressBar.Progress = _bankSlipViewModel.BatchProgress / 100.0;
                });
            }
            else if (e.PropertyName == nameof(_bankSlipViewModel.BatchProgressText))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    BatchProgressLabel.Text = _bankSlipViewModel.BatchProgressText;
                });
            }
        }

        private async void OnScanNowClicked(object? sender, EventArgs e)
        {
            try
            {
                ScanNowButton.IsEnabled = false;
                BankSlipStatusLabel.Text = "Scanning...";

                var result = await _bankSlipViewModel.ScanNowAsync();

                UpdatePendingCountDisplay();
                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning: {ex.Message}");
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                ScanNowButton.IsEnabled = true;
            }
        }

        private async void OnLogoutClicked(object? sender, EventArgs e)
        {
            var confirm = await DisplayAlert("Sign Out", "Are you sure you want to sign out?", "Sign Out", "Cancel");
            if (confirm)
            {
                await _credentialStorage.ClearCredentialsAsync();
                await Shell.Current.GoToAsync("//LoginPage");
            }
        }

        private async void OnBackClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//CategoryBrowserPage");
        }
    }
}