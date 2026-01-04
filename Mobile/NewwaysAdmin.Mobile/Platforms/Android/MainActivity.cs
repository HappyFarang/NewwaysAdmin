// File: NewwaysAdmin.Mobile/Platforms/Android/MainActivity.cs

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NewwaysAdmin.Mobile.Platforms.Android.Services;

namespace NewwaysAdmin.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            // Handle folder picker result
            if (requestCode == FolderPickerService.FolderPickerRequestCode)
            {
                FolderPickerService.HandleActivityResult(requestCode, resultCode, data);
            }
        }
    }
}