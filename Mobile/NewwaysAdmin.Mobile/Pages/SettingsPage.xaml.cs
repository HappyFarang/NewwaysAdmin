// File: Mobile/NewwaysAdmin.Mobile/Pages/SettingsPage.xaml.cs
using NewwaysAdmin.Mobile.Config;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.ViewModels.Settings;

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

                RefreshFolderList();
                ClearAddFolderForm();

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
            SelectedFolderLabel.TextColor = Colors.Gray;
            PatternNameEntry.Text = "";
        }

        private void RefreshFolderList()
        {
            // Clear existing folder items (keep the NoFoldersLabel)
            var toRemove = FolderListContainer.Children
                .Where(c => c is Frame)
                .ToList();

            foreach (var child in toRemove)
            {
                FolderListContainer.Children.Remove(child);
            }

            // Show/hide "no folders" label
            NoFoldersLabel.IsVisible = _bankSlipViewModel.MonitoredFolders.Count == 0;

            // Add folder items
            foreach (var folder in _bankSlipViewModel.MonitoredFolders)
            {
                var folderFrame = CreateFolderItem(folder);
                FolderListContainer.Children.Add(folderFrame);
            }
        }

        private Frame CreateFolderItem(FolderItemViewModel folder)
        {
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 10,
                RowDefinitions = new RowDefinitionCollection
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                },
                RowSpacing = 2
            };

            // Pattern name (main label)
            var patternLabel = new Label
            {
                Text = folder.PatternIdentifier,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center
            };
            grid.Add(patternLabel, 0, 0);

            // Folder path (subtitle)
            var pathLabel = new Label
            {
                Text = folder.FolderDisplayName,
                FontSize = 11,
                TextColor = Colors.Gray,
                VerticalOptions = LayoutOptions.Center
            };
            grid.Add(pathLabel, 0, 1);

            // Status
            var statusLabel = new Label
            {
                Text = folder.StatusText,
                FontSize = 12,
                TextColor = folder.Exists ? Colors.Green : Colors.Orange,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetRowSpan(statusLabel, 2);
            grid.Add(statusLabel, 1, 0);

            // Remove button
            var removeButton = new Button
            {
                Text = "✕",
                BackgroundColor = Colors.Transparent,
                TextColor = Colors.Red,
                FontSize = 16,
                WidthRequest = 40,
                HeightRequest = 40,
                VerticalOptions = LayoutOptions.Center
            };
            removeButton.Clicked += async (s, e) => await OnRemoveFolderClicked(folder);
            Grid.SetRowSpan(removeButton, 2);
            grid.Add(removeButton, 2, 0);

            return new Frame
            {
                BackgroundColor = Colors.White,
                Padding = new Thickness(10, 8),
                CornerRadius = 5,
                Content = grid
            };
        }

        // ===== FOLDER PICKER =====

        private async void OnBrowseFolderClicked(object sender, EventArgs e)
        {
#if ANDROID
            try
            {
                await ShowFolderPickerAsync();
            }
            catch (Exception ex)
            {
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
#else
            await DisplayAlert("Not Supported", "Folder browsing is only available on Android", "OK");
#endif
        }

        private async Task ShowFolderPickerAsync()
        {
            // Get common image folders and show as ActionSheet
            var commonFolders = GetCommonImageFolders();

            if (commonFolders.Count == 0)
            {
                await DisplayAlert("No Folders", "No image folders found on device", "OK");
                return;
            }

            var folderNames = commonFolders.Select(f => f.Name).ToArray();
            var selected = await DisplayActionSheet("Select Folder", "Cancel", null, folderNames);

            if (string.IsNullOrEmpty(selected) || selected == "Cancel")
                return;

            // Find the selected folder
            foreach (var folder in commonFolders)
            {
                if (folder.Name == selected)
                {
                    _selectedFolderPath = folder.Path;
                    SelectedFolderLabel.Text = folder.Path;
                    SelectedFolderLabel.TextColor = Colors.Black;

                    // Auto-suggest pattern name based on folder
                    if (string.IsNullOrEmpty(PatternNameEntry.Text))
                    {
                        var username = UsernameLabel.Text ?? "User";
                        // Get just the last folder name for the pattern
                        var lastFolderName = Path.GetFileName(folder.Path);
                        PatternNameEntry.Text = $"{lastFolderName}_{username}";
                    }
                    break;
                }
            }
        }

        private List<FolderInfo> GetCommonImageFolders()
        {
            var folders = new List<FolderInfo>();

#if ANDROID
            var basePaths = new[]
            {
                Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDcim)?.AbsolutePath,
                Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryPictures)?.AbsolutePath,
                Android.OS.Environment.GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
            };

            foreach (var basePath in basePaths)
            {
                if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
                    continue;

                // Add the base folder itself
                var baseName = Path.GetFileName(basePath);
                folders.Add(new FolderInfo(baseName, basePath));

                // Add subfolders
                try
                {
                    foreach (var subDir in Directory.GetDirectories(basePath))
                    {
                        var subName = Path.GetFileName(subDir);
                        folders.Add(new FolderInfo($"{baseName}/{subName}", subDir));
                    }
                }
                catch { /* Permission denied, skip */ }
            }
#endif

            return folders.OrderBy(f => f.Name).ToList();
        }

        // Simple class instead of tuple to avoid null comparison issues
        private class FolderInfo
        {
            public string Name { get; }
            public string Path { get; }

            public FolderInfo(string name, string path)
            {
                Name = name;
                Path = path;
            }
        }

        private async void OnEnabledToggled(object sender, ToggledEventArgs e)
        {
            try
            {
                _bankSlipViewModel.IsEnabled = e.Value;
                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private async void OnAddFolderClicked(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedFolderPath))
            {
                BankSlipStatusLabel.Text = "Please select a folder first";
                return;
            }

            var patternName = PatternNameEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(patternName))
            {
                BankSlipStatusLabel.Text = "Please enter a pattern name";
                return;
            }

            try
            {
                AddFolderButton.IsEnabled = false;

                await _bankSlipViewModel.AddFolderAsync(_selectedFolderPath, patternName);

                ClearAddFolderForm();
                RefreshFolderList();
                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                AddFolderButton.IsEnabled = true;
            }
        }

        private async Task OnRemoveFolderClicked(FolderItemViewModel folder)
        {
            bool confirm = await DisplayAlert(
                "Remove Folder",
                $"Remove '{folder.PatternIdentifier}' from monitored folders?",
                "Remove",
                "Cancel");

            if (!confirm)
                return;

            try
            {
                await _bankSlipViewModel.RemoveFolderAsync(folder);
                RefreshFolderList();
                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
            }
            catch (Exception ex)
            {
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private async void OnSyncFromDateChanged(object sender, DateChangedEventArgs e)
        {
            try
            {
                _bankSlipViewModel.SyncFromDate = e.NewDate;
            }
            catch (Exception ex)
            {
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
        }

        private async void OnScanNowClicked(object sender, EventArgs e)
        {
            try
            {
                ScanNowButton.IsEnabled = false;
                ScanNowButton.Text = "🔄 Scanning...";
                BankSlipStatusLabel.Text = "Scanning for new images...";

                await _bankSlipViewModel.ScanNowAsync();

                BankSlipStatusLabel.Text = _bankSlipViewModel.StatusMessage;
                RefreshFolderList(); // Update pending counts
            }
            catch (Exception ex)
            {
                BankSlipStatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                ScanNowButton.IsEnabled = true;
                ScanNowButton.Text = "🔍 Scan Now";
            }
        }

        // ===== NAVIGATION =====

        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//CategoryBrowserPage");
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert(
                "Sign Out",
                "Are you sure you want to sign out?",
                "Yes, Sign Out",
                "Cancel");

            if (!confirm)
                return;

            try
            {
                await _credentialStorage.ClearCredentialsAsync();
                await Shell.Current.GoToAsync("//SimpleLoginPage");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Logout failed: {ex.Message}", "OK");
            }
        }
    }
}