// File: Mobile/NewwaysAdmin.Mobile/App.xaml.cs
using NewwaysAdmin.Mobile.Infrastructure.Storage;

namespace NewwaysAdmin.Mobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            MainPage = new AppShell();

            // Initialize storage in the background
            Task.Run(async () => await InitializeStorageAsync());
        }
        protected override void OnStart()
        {
            System.Diagnostics.Debug.WriteLine("=== APP ONSTART CALLED ===");
            base.OnStart();
            // Initialize storage after the app has fully started and DI is available
            Task.Run(async () => await InitializeStorageAsync());
        }
        private async Task InitializeStorageAsync()
        {
            try
            {
                // Get the storage manager from DI
                var storageManager = Handler?.MauiContext?.Services?.GetService<MobileStorageManager>();

                if (storageManager != null)
                {
                    // Initialize essential folders first for quick startup
                    await storageManager.InitializeAsync();

                    // Then initialize remaining folders in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await storageManager.InitializeAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Background storage initialization error: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't crash the app
                System.Diagnostics.Debug.WriteLine($"Essential storage initialization error: {ex.Message}");
            }
        }
    }
}