// File: Mobile/NewwaysAdmin.Mobile/ViewModels/SettingsViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Config;
using NewwaysAdmin.Mobile.Services;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly CredentialStorageService _credentialStorage;
        private readonly ILogger<SettingsViewModel> _logger;

        private string _username = "Unknown";
        private string _serverUrl = "";

        public SettingsViewModel(
            CredentialStorageService credentialStorage,
            ILogger<SettingsViewModel> logger)
        {
            _credentialStorage = credentialStorage;
            _logger = logger;

            _serverUrl = AppConfig.ServerUrl;
            LogoutCommand = new Command(async () => await LogoutAsync());
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string ServerUrl
        {
            get => _serverUrl;
            set { _serverUrl = value; OnPropertyChanged(); }
        }

        public ICommand LogoutCommand { get; }

        public async Task LoadDataAsync()
        {
            try
            {
                var creds = await _credentialStorage.GetSavedCredentialsAsync();
                if (creds != null && !string.IsNullOrEmpty(creds.Username))
                {
                    Username = creds.Username;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading settings data");
            }
        }

        private async Task LogoutAsync()
        {
            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Sign Out",
                "Are you sure you want to sign out? You will need to log in again.",
                "Yes, Sign Out",
                "Cancel");

            if (confirm)
            {
                _logger.LogInformation("User logging out");
                await _credentialStorage.ClearCredentialsAsync();
                await Shell.Current.GoToAsync("//SimpleLoginPage");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}