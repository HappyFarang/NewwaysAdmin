// File: Mobile/NewwaysAdmin.Mobile/AppShell.xaml.cs
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.Pages.Categories;

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
            Routing.RegisterRoute("CategoryManagementPage", typeof(CategoryManagementPage));
            Routing.RegisterRoute(nameof(WelcomePage), typeof(WelcomePage));
            Routing.RegisterRoute("bankSlipReview", typeof(ProjectListPage));
            Routing.RegisterRoute("projectDetail", typeof(ProjectDetailPage)); // We'll create this next
        }
    }
}