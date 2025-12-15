// File: Mobile/NewwaysAdmin.Mobile/Pages/SettingsPage.xaml.cs
using NewwaysAdmin.Mobile.Config;
using NewwaysAdmin.Mobile.Services;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class SettingsPage : ContentPage
    {
        private readonly CredentialStorageService _credentialStorage;

        public SettingsPage(CredentialStorageService credentialStorage)
        {
            InitializeComponent();
            _credentialStorage = credentialStorage;
            ServerLabel.Text = AppConfig.ServerUrl;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadUserDataAsync();
        }

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
        private async void OnBackClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//CategoryBrowserPage");
        }
    }
}