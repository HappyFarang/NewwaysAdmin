// File: NewwaysAdmin.Mobile/Platforms/Android/Services/BankSlipMonitorService.cs
// Foreground service that keeps FileObservers running in background

using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.BankSlip;

namespace NewwaysAdmin.Mobile.Platforms.Android.Services
{
    [Service(
        Name = "com.companyname.newwaysadmin.mobile.BankSlipMonitorService",
        ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
    public class BankSlipMonitorService : Service
    {
        private const int NotificationId = 9001;
        private const string ChannelId = "bank_slip_monitor";
        private const string ChannelName = "Bank Slip Monitor";

        private BankSlipObserverManager? _observerManager;
        private BankSlipService? _bankSlipService;
        private BankSlipSettingsService? _settingsService;
        private ILogger<BankSlipMonitorService>? _logger;

        private readonly HashSet<string> _processingFiles = new();
        private readonly object _processingLock = new();

        public override void OnCreate()
        {
            base.OnCreate();

            // Get services from DI
            var services = MauiApplication.Current.Services;
            _logger = services.GetService<ILogger<BankSlipMonitorService>>();
            _bankSlipService = services.GetService<BankSlipService>();
            _settingsService = services.GetService<BankSlipSettingsService>();
            _observerManager = services.GetService<BankSlipObserverManager>();

            _logger?.LogInformation("[MonitorService] Service created");
        }

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            _logger?.LogInformation("[MonitorService] OnStartCommand received");

            // Create notification channel (required for Android 8+)
            CreateNotificationChannel();

            // Build and show foreground notification
            var notification = BuildNotification("Monitoring for bank slips...");

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                StartForeground(NotificationId, notification,
                    global::Android.Content.PM.ForegroundService.TypeDataSync);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }

            // Start watching folders
            StartWatchingFolders();

            return StartCommandResult.Sticky;
        }

        private async void StartWatchingFolders()
        {
            if (_observerManager == null || _settingsService == null)
            {
                _logger?.LogError("[MonitorService] Required services not available");
                return;
            }

            // Set the callback for new files
            _observerManager.SetNewFileCallback(OnNewFileDetected);

            // Get all configured folders and start watching
            var settings = await _settingsService.LoadSettingsAsync();
            var watchCount = 0;

            foreach (var folder in settings.MonitoredFolders)
            {
                if (!string.IsNullOrEmpty(folder.DeviceFolderPath) &&
                    Directory.Exists(folder.DeviceFolderPath))
                {
                    if (_observerManager.StartWatching(folder.PatternIdentifier, folder.DeviceFolderPath))
                    {
                        watchCount++;
                    }
                }
            }

            _logger?.LogInformation(
                "[MonitorService] Started watching {Count} folders", watchCount);

            // Update notification
            UpdateNotification($"Watching {watchCount} folder(s) for bank slips");
        }

        private async void OnNewFileDetected(string sourceType, string filePath)
        {
            _logger?.LogInformation(
                "[MonitorService] 📸 New file detected! Source: {Source}, Path: {Path}",
                sourceType, filePath);

            // Prevent duplicate processing
            lock (_processingLock)
            {
                if (_processingFiles.Contains(filePath))
                {
                    _logger?.LogDebug("[MonitorService] File already being processed: {Path}", filePath);
                    return;
                }
                _processingFiles.Add(filePath);
            }

            try
            {
                // Small delay to ensure file is fully written
                await Task.Delay(500);

                // Verify file still exists and is accessible
                if (!File.Exists(filePath))
                {
                    _logger?.LogWarning("[MonitorService] File no longer exists: {Path}", filePath);
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    _logger?.LogWarning("[MonitorService] File is empty: {Path}", filePath);
                    return;
                }

                // Upload the file
                UpdateNotification($"Uploading: {Path.GetFileName(filePath)}");

                var success = await _bankSlipService!.UploadFileAsync(filePath, sourceType);

                if (success)
                {
                    _logger?.LogInformation(
                        "[MonitorService] ✅ Successfully uploaded: {Path}", filePath);
                    UpdateNotification($"Uploaded: {Path.GetFileName(filePath)}");
                }
                else
                {
                    _logger?.LogWarning(
                        "[MonitorService] ❌ Failed to upload: {Path}", filePath);
                    UpdateNotification($"Failed: {Path.GetFileName(filePath)}");
                }

                // Reset notification after a delay
                await Task.Delay(3000);
                var watchCount = _observerManager?.WatcherCount ?? 0;
                UpdateNotification($"Watching {watchCount} folder(s) for bank slips");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[MonitorService] Error processing file: {Path}", filePath);
            }
            finally
            {
                lock (_processingLock)
                {
                    _processingFiles.Remove(filePath);
                }
            }
        }

        public override IBinder? OnBind(Intent? intent) => null;

        public override void OnDestroy()
        {
            _logger?.LogInformation("[MonitorService] Service destroying");

            _observerManager?.StopAll();

            base.OnDestroy();
        }

        private void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var channel = new NotificationChannel(
                ChannelId,
                ChannelName,
                NotificationImportance.Low)
            {
                Description = "Monitors folders for new bank slips to upload"
            };

            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            notificationManager?.CreateNotificationChannel(channel);
        }

        private Notification BuildNotification(string text)
        {
            // Intent to open app when notification is tapped
            var intent = new Intent(this, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.SingleTop);

            var pendingIntentFlags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                pendingIntentFlags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetActivity(
                this, 0, intent, pendingIntentFlags);

            return new NotificationCompat.Builder(this, ChannelId)
                .SetContentTitle("Bank Slip Sync")
                .SetContentText(text)
                .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCamera) // Built-in Android icon
                .SetOngoing(true)
                .SetContentIntent(pendingIntent)
                .SetCategory(NotificationCompat.CategoryService)
                .SetPriority(NotificationCompat.PriorityLow)
                .Build();
        }

        private void UpdateNotification(string text)
        {
            var notification = BuildNotification(text);
            var notificationManager = GetSystemService(NotificationService) as NotificationManager;
            notificationManager?.Notify(NotificationId, notification);
        }

        // Static helper to start/stop the service
        public static void Start(Context context)
        {
            var intent = new Intent(context, typeof(BankSlipMonitorService));

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }

        public static void Stop(Context context)
        {
            var intent = new Intent(context, typeof(BankSlipMonitorService));
            context.StopService(intent);
        }
    }
}