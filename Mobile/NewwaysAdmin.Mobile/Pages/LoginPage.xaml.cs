using NewwaysAdmin.Mobile.ViewModels;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage()
        {
            InitializeComponent();

            try
            {
                var viewModel = App.Current.Handler.MauiContext.Services.GetService<LoginViewModel>();
                if (viewModel != null)
                {
                    Content = new Label { Text = "ViewModel Created Successfully!", FontSize = 20 };
                    BindingContext = viewModel;
                }
                else
                {
                    Content = new Label { Text = "ViewModel is NULL from DI", FontSize = 20 };
                }
            }
            catch (Exception ex)
            {
                Content = new Label { Text = $"DI Error: {ex.Message}", FontSize = 16 };
            }
        }
    }
}