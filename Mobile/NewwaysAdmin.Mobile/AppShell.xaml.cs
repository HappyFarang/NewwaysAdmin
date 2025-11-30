// File: Mobile/NewwaysAdmin.Mobile/AppShell.xaml.cs
using NewwaysAdmin.Mobile.Pages;

namespace NewwaysAdmin.Mobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Register routes for navigation
            Routing.RegisterRoute(nameof(SimpleLoginPage), typeof(SimpleLoginPage));
            Routing.RegisterRoute(nameof(CategoryBrowserPage), typeof(CategoryBrowserPage));
            Routing.RegisterRoute(nameof(HomePage), typeof(HomePage));
        }
    }
}