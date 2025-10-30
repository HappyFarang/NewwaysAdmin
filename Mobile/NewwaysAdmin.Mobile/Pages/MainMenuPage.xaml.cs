// File: Mobile/NewwaysAdmin.Mobile/Pages/MainMenuPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class MainMenuPage : ContentPage
    {
        private readonly MainMenuViewModel _viewModel;

        public MainMenuPage(MainMenuViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            // Initialize with default values (will be updated by navigation parameters)
            _viewModel.Initialize("User", true);
        }
    }
}
