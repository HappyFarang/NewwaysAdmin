// File: Mobile/NewwaysAdmin.Mobile/ViewModels/SimpleLoginViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Categories;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class SimpleLoginViewModel : INotifyPropertyChanged
    {
        private readonly IMauiAuthService _authService;
        private readonly IConnectionService _connectionService;
        private readonly ILogger<SimpleLoginViewModel> _logger;
        private readonly MobileSessionState _sessionState;

        private string _username = "";
        private string _password = "";
        private bool _isBusy = false;
        private string _statusMessage = "";
        private Color _statusColor = Colors.Black;
        private string _connectionStatus = "Click 'Test Connection' to check server";
        private Color _connectionColor = Colors.Gray;
        private string _serverUrl = "";

        private bool _showLoginForm = false;

        public bool ShowLoginForm
        {
            get => _showLoginForm;
            set
            {
                _showLoginForm = value;
                OnPropertyChanged();
            }
        }

        public SimpleLoginViewModel(
            IMauiAuthService authService,
            IConnectionService connectionService,
            MobileSessionState sessionState,  // ADD THIS
            ILogger<SimpleLoginViewModel> logger)
        {
            _authService = authService;
            _connectionService = connectionService;
            _sessionState = sessionState;  // ADD THIS
            _logger = logger;

            TestConnectionCommand = new Command(async () => await TestConnectionAsync(), () => !IsBusy);
            LoginCommand = new Command(async () => await LoginAsync(), () => !IsBusy);

            // Initialize server URL
            ServerUrl = _connectionService.GetBaseUrl();
        }

        #region Properties

        public string Username
        {
            get => _username;
            set
            {
                _username = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                ((Command)TestConnectionCommand).ChangeCanExecute();
                ((Command)LoginCommand).ChangeCanExecute();
            }
        }

        public bool IsNotBusy => !IsBusy;

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

        public Color StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                OnPropertyChanged();
            }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                OnPropertyChanged();
            }
        }

        public Color ConnectionColor
        {
            get => _connectionColor;
            set
            {
                _connectionColor = value;
                OnPropertyChanged();
            }
        }

        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                _serverUrl = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Commands

        public ICommand TestConnectionCommand { get; }
        public ICommand LoginCommand { get; }

        #endregion

        #region Auto-Login on Startup

        /// <summary>
        /// Called from OnAppearing - checks for saved credentials and navigates immediately
        /// No server contact - ConnectionMonitor handles that in background
        /// </summary>
        public async Task TryAutoLoginOnStartupAsync()
        {
            try
            {
                IsBusy = true;
                _logger.LogInformation("Checking for saved credentials...");

                // Local only - no server contact!
                var result = await _authService.CheckSavedCredentialsAsync();

                if (result.Success)
                {
                    _logger.LogInformation("Found saved credentials for user: {Username} - navigating immediately",
                        result.Username);

                    // Populate session state
                    _sessionState.SetSession(result.Username!, result.Permissions);

                    // Go straight to main app
                    await NavigateToMainAppAsync(result.Username);
                    return;
                }

                // No saved credentials - show login form
                _logger.LogInformation("No saved credentials found - manual login required");
                StatusMessage = "Please enter your credentials";
                StatusColor = Colors.Gray;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking saved credentials");
                StatusMessage = "Please enter your credentials";
                StatusColor = Colors.Gray;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Manual Login

        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "Please enter username and password";
                StatusColor = Colors.Red;
                return;
            }

            try
            {
                IsBusy = true;
                StatusMessage = "Logging in...";
                StatusColor = Colors.Orange;

                _logger.LogInformation("Attempting login for user: {Username}", Username);

                var result = await _authService.LoginAsync(Username, Password, saveCredentials: true);

                if (result.Success)
                {
                    StatusMessage = $"✓ Login successful!";
                    StatusColor = Colors.Green;
                    _logger.LogInformation("Login successful for user: {Username}", Username);

                    // Populate session state
                    _sessionState.SetSession(result.Username ?? Username, result.Permissions);

                    // Navigate to main app
                    await NavigateToMainAppAsync(result.Username ?? Username);
                }
                else
                {
                    StatusMessage = $"✗ {result.Message}";
                    StatusColor = Colors.Red;
                    Password = ""; // Clear password on failed login
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                StatusMessage = $"✗ Error: {ex.Message}";
                StatusColor = Colors.Red;
                Password = "";
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Connection Test

        private async Task TestConnectionAsync()
        {
            try
            {
                IsBusy = true;
                ConnectionStatus = "Testing connection...";
                ConnectionColor = Colors.Orange;

                _logger.LogInformation("Testing connection to server");

                var result = await _connectionService.TestConnectionAsync();

                if (result.Success)
                {
                    ConnectionStatus = "✓ Server is reachable";
                    ConnectionColor = Colors.Green;
                    _logger.LogInformation("Connection test successful");
                }
                else
                {
                    ConnectionStatus = $"✗ {result.Message}";
                    ConnectionColor = Colors.Red;
                    _logger.LogWarning("Connection test failed: {Message}", result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection");
                ConnectionStatus = $"✗ Error: {ex.Message}";
                ConnectionColor = Colors.Red;
            }
            finally
            {
                IsBusy = false;
            }
        }

        #endregion

        #region Navigation

        private async Task NavigateToMainAppAsync(string? username)
        {
            _logger.LogInformation("Navigating to CategoryBrowserPage for user: {Username}", username);

            // Navigate to category browser
            await Shell.Current.GoToAsync("//CategoryBrowserPage");
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