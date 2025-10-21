// File: Mobile/NewwaysAdmin.Mobile/ViewModels/LoginViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NewwaysAdmin.Mobile.Services;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class LoginViewModel : INotifyPropertyChanged
    {
        private readonly MauiAuthService _authService;
        private readonly ILogger<LoginViewModel> _logger;

        private string _username = "";
        private string _password = "";
        private bool _isBusy = false;
        private string _statusMessage = "";
        private Color _statusColor = Colors.Black;
        private string _connectionStatus = "Checking connection...";
        private Color _connectionColor = Colors.Orange;

        public LoginViewModel(MauiAuthService authService, ILogger<LoginViewModel> logger)
        {
            _authService = authService;
            _logger = logger;
            LoginCommand = new Command(async () => await ExecuteLoginAsync(), () => !IsBusy);
        }

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

        public ICommand LoginCommand { get; }

        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                ConnectionStatus = "Checking connection...";
                ConnectionColor = Colors.Orange;

                var isConnected = await _authService.TestConnectionAsync();

                if (isConnected)
                {
                    ConnectionStatus = "✓ Connected to server";
                    ConnectionColor = Colors.Green;
                }
                else
                {
                    ConnectionStatus = "⚠ Cannot connect to server";
                    ConnectionColor = Colors.Red;
                }

                return isConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking connection");
                ConnectionStatus = "⚠ Connection error";
                ConnectionColor = Colors.Red;
                return false;
            }
        }

        public async Task<bool> TryAutoLoginAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Checking saved credentials...";
                StatusColor = Colors.Orange;

                var result = await _authService.TryAutoLoginAsync();

                if (result.RequiresManualLogin)
                {
                    StatusMessage = "Please enter your credentials";
                    StatusColor = Colors.Gray;
                    return false;
                }

                if (result.Success)
                {
                    StatusMessage = $"Welcome back! Logged in successfully.";
                    StatusColor = Colors.Green;
                    _logger.LogInformation("Auto-login successful");
                    return true;
                }

                StatusMessage = result.Message ?? "Auto-login failed";
                StatusColor = Colors.Red;
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auto-login");
                StatusMessage = "Auto-login error";
                StatusColor = Colors.Red;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExecuteLoginAsync()
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

                var result = await _authService.LoginAsync(Username, Password);

                if (result.Success)
                {
                    StatusMessage = "Login successful!";
                    StatusColor = Colors.Green;
                    _logger.LogInformation("Manual login successful for user: {Username}", Username);

                    // Navigate to main app (we'll implement this next)
                    await NavigateToMainAppAsync();
                }
                else
                {
                    StatusMessage = result.Message ?? "Login failed";
                    StatusColor = Colors.Red;
                    Password = ""; // Clear password on failed login
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                StatusMessage = "Connection error";
                StatusColor = Colors.Red;
                Password = "";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task NavigateToMainAppAsync()
        {
            // For now, just show a placeholder message
            // We'll replace this with actual navigation in the next step
            await Application.Current.MainPage.DisplayAlert("Success",
                "Login successful! Main app coming soon...", "OK");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}