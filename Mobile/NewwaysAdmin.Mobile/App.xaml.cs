// File: Mobile/NewwaysAdmin.Mobile/App.xaml.cs
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile
{
    public partial class App : Application
    {
        public App(ConnectionMonitor connectionMonitor)
        {
            InitializeComponent();
            MainPage = new AppShell();

            // Start background connection monitoring
            connectionMonitor.Start();
        }
    }
}