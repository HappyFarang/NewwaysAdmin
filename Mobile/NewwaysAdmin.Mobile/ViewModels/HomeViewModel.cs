// File: Mobile/NewwaysAdmin.Mobile/ViewModels/HomeViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Auth;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class HomeViewModel : INotifyPropertyChanged
    {
        private readonly CredentialStorageService _credentialStorage;
        private readonly PermissionsCache _permissionsCache;
        private readonly ConnectionState _connectionState;
        private readonly ILogger<HomeViewModel> _logger;

        private string _username = "Not logged in";
        private string _savedAt = "-";
        private string _hasPassword = "-";
        private string _permissionCount = "0 permissions";
        private string _permissionsList = "No permissions cached";
        private string _modeText = "Checking...";
        private Color _modeBackgroundColor = Colors.Gray;
        private bool _isBusy = false;
        private string _statusMessage = "";
        private Color _statusColor = Colors.Black;
        private Color _connectionDotColor = Colors.Gray;
        private string _connectionText = "Checking...";

        public HomeViewModel(
            CredentialStorageService credentialStorage,
            PermissionsCache permissionsCache,
            ConnectionState connectionState,
            ILogger<HomeViewModel> logger)
        {
            _credentialStorage = credentialStorage;
            _permissionsCache = permissionsCache;
            _connectionState = connectionState;
            _logger = logger;

            RefreshCommand = new Command(async () => await LoadDataAsync());
            LogoutCommand = new Command(async () => await LogoutAsync());

            // Subscribe to connection changes
            _connectionState.OnConnectionChanged += OnConnectionChanged;

            // Set initial state
            UpdateConnectionDisplay();
        }

        #region Properties

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string SavedAt
        {
            get => _savedAt;
            set { _savedAt = value; OnPropertyChanged(); }
        }

        public string HasPassword
        {
            get => _hasPassword;
            set { _hasPassword = value; OnPropertyChanged(); }
        }

        public string PermissionCount
        {
            get => _permissionCount;
            set { _permissionCount = value; OnPropertyChanged(); }
        }

        public string PermissionsList
        {
            get => _permissionsList;
            set { _permissionsList = value; OnPropertyChanged(); }
        }

        public string ModeText
        {
            get => _modeText;
            set { _modeText = value; OnPropertyChanged(); }
        }

        public Color ModeBackgroundColor
        {
            get => _modeBackgroundColor;
            set { _modeBackgroundColor = value; OnPropertyChanged(); }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStatusMessage)); }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public Color StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public Color ConnectionDotColor
        {
            get => _connectionDotColor;
            set { _connectionDotColor = value; OnPropertyChanged(); }
        }

        public string ConnectionText
        {
            get => _connectionText;
            set { _connectionText = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand LogoutCommand { get; }

        #endregion

        #region Connection State

        private void OnConnectionChanged(object? sender, bool isOnline)
        {
            // Update UI on main thread
            MainThread.BeginInvokeOnMainThread(UpdateConnectionDisplay);
        }

        private void UpdateConnectionDisplay()
        {
            if (_connectionState.IsOnline)
            {
                ConnectionDotColor = Colors.Green;
                ConnectionText = "Online";
                ModeText = "✓ ONLINE - Connected to server";
                ModeBackgroundColor = Colors.Green;
            }
            else
            {
                ConnectionDotColor = Colors.Red;
                ConnectionText = "Offline";
                ModeText = "⚠️ OFFLINE - Using cached data";
                ModeBackgroundColor = Colors.Orange;
            }
        }

        #endregion

        #region Methods

        public async Task LoadDataAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Loading...";
                StatusColor = Colors.Orange;

                _logger.LogInformation("Loading stored auth data");

                // Load credentials
                var credentials = await _credentialStorage.GetSavedCredentialsAsync();

                if (credentials != null)
                {
                    Username = credentials.Username;
                    SavedAt = credentials.SavedAt.ToString("yyyy-MM-dd HH:mm:ss");
                    HasPassword = string.IsNullOrEmpty(credentials.Password) ? "No" : "Yes (hidden)";

                    // Load permissions for this user
                    var permissions = await _permissionsCache.GetCachedPermissionsAsync(credentials.Username);

                    if (permissions != null && permissions.Count > 0)
                    {
                        PermissionCount = $"{permissions.Count} permission(s)";
                        PermissionsList = string.Join("\n• ", permissions.Prepend(""));
                    }
                    else
                    {
                        PermissionCount = "0 permissions";
                        PermissionsList = "No permissions cached for this user";
                    }

                    StatusMessage = "✓ Data loaded successfully";
                    StatusColor = Colors.Green;
                }
                else
                {
                    Username = "Not logged in";
                    SavedAt = "-";
                    HasPassword = "-";
                    PermissionCount = "0 permissions";
                    PermissionsList = "No credentials saved - please login first";

                    StatusMessage = "No saved credentials found";
                    StatusColor = Colors.Orange;
                }

                _logger.LogInformation("Auth data loaded: User={Username}", Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading auth data");
                StatusMessage = $"Error: {ex.Message}";
                StatusColor = Colors.Red;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LogoutAsync()
        {
            try
            {
                var confirm = await Application.Current!.MainPage!.DisplayAlert(
                    "Logout",
                    "This will clear all saved credentials and permissions. You will need to login again.\n\nContinue?",
                    "Yes, Logout",
                    "Cancel");

                if (!confirm)
                    return;

                IsBusy = true;
                StatusMessage = "Clearing data...";
                StatusColor = Colors.Orange;

                _logger.LogInformation("Logging out - clearing stored credentials");

                // Clear credentials
                await _credentialStorage.ClearCredentialsAsync();

                StatusMessage = "✓ Logged out successfully";
                StatusColor = Colors.Green;

                // Reload to show empty state
                await LoadDataAsync();

                // Navigate back to login
                await Shell.Current.GoToAsync("//SimpleLoginPage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                StatusMessage = $"Error: {ex.Message}";
                StatusColor = Colors.Red;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}