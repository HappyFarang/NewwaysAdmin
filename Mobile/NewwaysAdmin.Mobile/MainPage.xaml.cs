// File: Mobile/NewwaysAdmin.Mobile/MainPage.xaml.cs
using NewwaysAdmin.Mobile.Services.Startup;

namespace NewwaysAdmin.Mobile
{
    public partial class MainPage : ContentPage
    {
        private readonly StartupCoordinator _startupCoordinator;
        private const string SERVER_URL = "https://your-server.com"; // TODO: Make configurable

        public MainPage(StartupCoordinator startupCoordinator)
        {
            InitializeComponent();
            _startupCoordinator = startupCoordinator;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // Show loading while startup happens
            await ShowLoadingAsync();

            // Run startup sequence
            var result = await _startupCoordinator.StartupAsync(SERVER_URL);

            if (result.GoToMainApp)
            {
                // Go to main app
                await Shell.Current.GoToAsync("//mainapp");
            }
            else if (result.ShowLoginPage)
            {
                // Go to login page
                await Shell.Current.GoToAsync("//login");
            }

            await HideLoadingAsync();
        }

        private async Task ShowLoadingAsync()
        {
            // TODO: Show your loading UI
            await Task.Delay(100);
        }

        private async Task HideLoadingAsync()
        {
            // TODO: Hide your loading UI
            await Task.Delay(100);
        }
    }
}