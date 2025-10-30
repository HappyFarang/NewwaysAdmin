// File: Mobile/NewwaysAdmin.Mobile/ViewModels/MainMenuViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.ViewModels
{
    /// <summary>
    /// ViewModel for the main menu / module hub
    /// </summary>
    public partial class MainMenuViewModel : ObservableObject
    {
        private readonly ILogger<MainMenuViewModel> _logger;

        [ObservableProperty]
        private string userName = "User";

        [ObservableProperty]
        private bool isOnline = true;

        public MainMenuViewModel(ILogger<MainMenuViewModel> logger)
        {
            _logger = logger;
        }

        public void Initialize(string userName, bool isOnline)
        {
            UserName = userName;
            IsOnline = isOnline;
            _logger.LogInformation("MainMenuViewModel initialized for user: {UserName}, Online: {IsOnline}", userName, isOnline);
        }

        [RelayCommand]
        private async Task NavigateToCategoriesAsync()
        {
            _logger.LogInformation("Navigating to Categories module");
            await Shell.Current.GoToAsync("///CategoryListPage");
        }

        // Future modules will be added here
        // [RelayCommand]
        // private async Task NavigateToExpensesAsync() { ... }
        //
        // [RelayCommand]
        // private async Task NavigateToReceiptsAsync() { ... }
    }
}
