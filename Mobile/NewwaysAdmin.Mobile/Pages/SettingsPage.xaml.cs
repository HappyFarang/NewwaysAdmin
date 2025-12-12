// File: Mobile/NewwaysAdmin.Mobile/Pages/SettingsPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage(SettingsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            if (BindingContext is SettingsViewModel vm)
            {
                await vm.LoadDataAsync();
            }
        }
    }
}