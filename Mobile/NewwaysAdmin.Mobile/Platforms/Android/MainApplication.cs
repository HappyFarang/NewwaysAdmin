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

            // Bank slip monitoring is now started via Settings page
            // using the new FileObserver-based service (IBankSlipMonitorControl)
            Android.Util.Log.Info("MainApplication", "Application started");
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}