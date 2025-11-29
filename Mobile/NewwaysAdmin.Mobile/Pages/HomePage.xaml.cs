// File: Mobile/NewwaysAdmin.Mobile/Pages/HomePage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class HomePage : ContentPage
    {
        private readonly HomeViewModel _viewModel;

        public HomePage(HomeViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadDataAsync();
        }
    }
}