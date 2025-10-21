// File: Mobile/NewwaysAdmin.Mobile/ViewModels/ConnectionTestViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class ConnectionTestViewModel : INotifyPropertyChanged
    {
        private readonly IConnectionService _connectionService;
        private readonly ILogger<ConnectionTestViewModel> _logger;

        private bool _isBusy = false;
        private string _serverUrl = "";
        private string _statusTitle = "";
        private string _statusMessage = "";
        private Color _statusBackgroundColor = Colors.Transparent;
        private Color _statusTextColor = Colors.Black;
        private string _detailedResults = "";

        public ConnectionTestViewModel(IConnectionService connectionService, ILogger<ConnectionTestViewModel> logger)
        {
            _connectionService = connectionService;
            _logger = logger;

            TestConnectionCommand = new Command(async () => await TestConnectionAsync(), () => !IsBusy);
            TestAuthCommand = new Command(async () => await TestAuthAsync(), () => !IsBusy);
            GoToLoginCommand = new Command(async () => await GoToLoginAsync(), () => !IsBusy);

            // Initialize server URL
            ServerUrl = _connectionService.GetBaseUrl();
        }

        #region Properties

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBusy));
                ((Command)TestConnectionCommand).ChangeCanExecute();
                ((Command)TestAuthCommand).ChangeCanExecute();
                ((Command)GoToLoginCommand).ChangeCanExecute();
            }
        }

        public bool IsNotBusy => !IsBusy;

        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                _serverUrl = value;
                OnPropertyChanged();
            }
        }

        public string StatusTitle
        {
            get => _statusTitle;
            set
            {
                _statusTitle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        public Color StatusBackgroundColor
        {
            get => _statusBackgroundColor;
            set
            {
                _statusBackgroundColor = value;
                OnPropertyChanged();
            }
        }

        public Color StatusTextColor
        {
            get => _statusTextColor;
            set
            {
                _statusTextColor = value;
                OnPropertyChanged();
            }
        }

        public string DetailedResults
        {
            get => _detailedResults;
            set
            {
                _detailedResults = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDetails));
            }
        }

        public bool HasStatus => !string.IsNullOrEmpty(StatusTitle) || !string.IsNullOrEmpty(StatusMessage);
        public bool HasDetails => !string.IsNullOrEmpty(DetailedResults);

        #endregion

        #region Commands

        public ICommand TestConnectionCommand { get; }
        public ICommand TestAuthCommand { get; }
        public ICommand GoToLoginCommand { get; }

        #endregion

        #region Private Methods

        private async Task TestConnectionAsync()
        {
            IsBusy = true;
            try
            {
                _logger.LogInformation("Testing basic connection...");

                var result = await _connectionService.TestConnectionAsync();

                if (result.Success)
                {
                    SetSuccessStatus("Connection Test", result.Message);
                    DetailedResults = $"Status Code: {result.StatusCode}\nResponse: {result.ResponseContent}";
                }
                else
                {
                    SetErrorStatus("Connection Failed", result.Message);
                    DetailedResults = result.Exception?.ToString() ?? "No additional details";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during connection test");
                SetErrorStatus("Test Error", "Unexpected error during connection test");
                DetailedResults = ex.ToString();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task TestAuthAsync()
        {
            IsBusy = true;
            try
            {
                _logger.LogInformation("Testing auth endpoint...");

                var result = await _connectionService.TestAuthEndpointAsync();

                if (result.Success)
                {
                    SetSuccessStatus("Auth Endpoint Test", result.Message);
                    DetailedResults = $"Status Code: {result.StatusCode}\nResponse: {result.ResponseContent}";
                }
                else
                {
                    SetErrorStatus("Auth Endpoint Failed", result.Message);
                    DetailedResults = result.Exception?.ToString() ?? "No additional details";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during auth endpoint test");
                SetErrorStatus("Test Error", "Unexpected error during auth test");
                DetailedResults = ex.ToString();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task GoToLoginAsync()
        {
            try
            {
                await Shell.Current.GoToAsync("//SimpleLoginPage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error navigating to login page");
                SetErrorStatus("Navigation Error", "Could not navigate to login page");
            }
        }

        private void SetSuccessStatus(string title, string message)
        {
            StatusTitle = title;
            StatusMessage = message;
            StatusBackgroundColor = Colors.LightGreen;
            StatusTextColor = Colors.DarkGreen;
        }

        private void SetErrorStatus(string title, string message)
        {
            StatusTitle = title;
            StatusMessage = message;
            StatusBackgroundColor = Colors.LightPink;
            StatusTextColor = Colors.DarkRed;
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