// File: Mobile/NewwaysAdmin.Mobile/Pages/Categories/CategoryBrowserPage.xaml.cs
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.ViewModels.Categories;

namespace NewwaysAdmin.Mobile.Pages.Categories
{
    public partial class CategoryBrowserPage : ContentPage
    {
        private readonly CategoryBrowserViewModel _viewModel;
        private readonly MobileSessionState _sessionState;
        private readonly IMauiAuthService _authService;

        public CategoryBrowserPage(
            CategoryBrowserViewModel viewModel,
            MobileSessionState sessionState,
            IMauiAuthService authService)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _sessionState = sessionState;
            _authService = authService;
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // If not logged in, try to load credentials
            if (!_sessionState.IsLoggedIn)
            {
                var result = await _authService.CheckSavedCredentialsAsync();

                if (result.Success && !string.IsNullOrEmpty(result.Username))
                {
                    _sessionState.SetSession(result.Username, result.Permissions);
                    NavHeader.RefreshMenu();  // ADD THIS - rebuild menu with permissions
                }
                else
                {
                    // No credentials - go to login
                    await Shell.Current.GoToAsync("//SimpleLoginPage");
                    return;
                }
            }

            // Permission gate
            if (!_sessionState.HasPermission("accounting"))
            {
                await Shell.Current.GoToAsync("//WelcomePage");
                return;
            }

            // All good - load categories
            await _viewModel.LoadCategoriesAsync();
        }
    }
}