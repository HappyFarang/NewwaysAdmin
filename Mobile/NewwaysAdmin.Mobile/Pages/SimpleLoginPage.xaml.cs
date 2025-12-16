// File: Mobile/NewwaysAdmin.Mobile/Pages/SimpleLoginPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class SimpleLoginPage : ContentPage
    {
        private readonly SimpleLoginViewModel _viewModel;
        private bool _hasAttemptedAutoLogin = false;

        public SimpleLoginPage(SimpleLoginViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (!_hasAttemptedAutoLogin)
            {
                _hasAttemptedAutoLogin = true;
                await _viewModel.TryAutoLoginOnStartupAsync();
            }
        }
    }
}