using NewwaysAdmin.Mobile.Pages;

namespace NewwaysAdmin.Mobile
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Register routes for programmatic navigation
            Routing.RegisterRoute("SubCategoryListPage", typeof(SubCategoryListPage));
        }
    }
}