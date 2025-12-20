using Android.App;
using Android.Runtime;

namespace NewwaysAdmin.Mobile
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }
        public override void OnCreate()
        {
            base.OnCreate();

            // Start bank slip monitor if enabled
            Task.Run(async () =>
            {
                try
                {
                    var settingsService = new NewwaysAdmin.Mobile.Services.BankSlip.BankSlipSettingsService();
                    var settings = await settingsService.LoadSettingsAsync();

                    if (settings.IsEnabled)
                    {
                        NewwaysAdmin.Mobile.Platforms.Android.Services.BankSlipWorkerManager.EnqueueMonitorWorker(this);
                        Android.Util.Log.Info("MainApplication", "Bank slip monitor started");
                    }
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Error("MainApplication", $"Failed to start bank slip monitor: {ex.Message}");
                }
            });
        }
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
