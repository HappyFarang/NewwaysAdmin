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

#if ANDROID
        protected override void OnStart()
        {
            base.OnStart();

            // Auto-start bank slip monitoring if it was previously enabled
            Task.Run(async () =>
            {
                try
                {
                    var settingsService = Handler?.MauiContext?.Services
                        .GetService<NewwaysAdmin.Mobile.Services.BankSlip.BankSlipSettingsService>();
                    var monitorControl = Handler?.MauiContext?.Services
                        .GetService<NewwaysAdmin.Mobile.Services.BankSlip.IBankSlipMonitorControl>();

                    if (settingsService != null && monitorControl != null)
                    {
                        var settings = await settingsService.LoadSettingsAsync();
                        if (settings.IsEnabled)
                        {
                            monitorControl.StartMonitoring();
                            System.Diagnostics.Debug.WriteLine("Bank slip monitoring auto-started");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to auto-start bank slip monitor: {ex.Message}");
                }
            });
        }
#endif
    }
}