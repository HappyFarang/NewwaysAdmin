// File: Mobile/NewwaysAdmin.Mobile/Pages/SimpleLoginPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class SimpleLoginPage : ContentPage
    {
        public SimpleLoginPage(SimpleLoginViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}