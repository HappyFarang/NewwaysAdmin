// File: Mobile/NewwaysAdmin.Mobile/Pages/HomePage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    [QueryProperty(nameof(Username), "username")]
    [QueryProperty(nameof(IsOffline), "offline")]
    public partial class HomePage : ContentPage
    {
        private readonly HomeViewModel _viewModel;

        public HomePage(HomeViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = viewModel;
        }

        public string? Username
        {
            set => _viewModel.Username = value ?? "Unknown";
        }

        public string? IsOffline
        {
            set => _viewModel.IsOfflineMode = value?.ToLower() == "true";
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadDataAsync();
        }
    }
}