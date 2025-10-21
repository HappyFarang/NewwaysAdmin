// File: Mobile/NewwaysAdmin.Mobile/ViewModels/SimpleLoginViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class SimpleLoginViewModel : INotifyPropertyChanged
    {
        private readonly IMauiAuthService _authService;
        private readonly IConnectionService _connectionService;
        private readonly ILogger<SimpleLoginViewModel> _logger;

        private string _username = "";
        private string _password = "";
        private bool _isBusy = false;
        private string _statusMessage = "";
        private Color _statusColor = Colors.Black;
        private string _connectionStatus = "Click 'Test Connection' to check server";
        private Color _connectionColor = Colors.Gray;
        private string _serverUrl = "";

        public SimpleLoginViewModel(
            IMauiAuthService authService,
            IConnectionService connectionService,
            ILogger<SimpleLoginViewModel> logger)
        {
            _authService = authService;
            _connectionService = connectionService;
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

        public Color StatusColor
        {
            get => _statusColor;
            set
            {
                _statusColor = value;
                OnPropertyChanged();
            }
        }

        public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

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

        #region Private Methods

        private async Task TestConnectionAsync()
        {
            IsBusy = true;
            try
            {
                _logger.LogInformation("Testing connection to server...");
                ConnectionStatus = "Testing connection...";
                ConnectionColor = Colors.Orange;

                var result = await _connectionService.TestConnectionAsync();

                if (result.Success)
                {
                    ConnectionStatus = $"✓ Connected! ({result.StatusCode})";
                    ConnectionColor = Colors.Green;
                    _logger.LogInformation("Connection test successful");
                }
                else
                {
                    ConnectionStatus = $"✗ Failed: {result.Message}";
                    ConnectionColor = Colors.Red;
                    _logger.LogWarning("Connection test failed: {Message}", result.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during connection test");
                ConnectionStatus = $"✗ Error: {ex.Message}";
                ConnectionColor = Colors.Red;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "Please enter username and password";
                StatusColor = Colors.Red;
                return;
            }

            IsBusy = true;
            try
            {
                _logger.LogInformation("Attempting login for user: {Username}", Username);
                StatusMessage = "Logging in...";
                StatusColor = Colors.Orange;

                var result = await _authService.LoginAsync(Username, Password);

                if (result.Success)
                {
                    StatusMessage = "✓ Login successful!";
                    StatusColor = Colors.Green;
                    _logger.LogInformation("Login successful for user: {Username}", Username);

                    // Show success message
                    await Application.Current.MainPage.DisplayAlert("Success",
                        $"Login successful!\nPermissions: {string.Join(", ", result.Permissions ?? new List<string>())}",
                        "OK");
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

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}