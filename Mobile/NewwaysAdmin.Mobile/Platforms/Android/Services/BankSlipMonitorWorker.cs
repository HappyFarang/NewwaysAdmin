// File: NewwaysAdmin.Mobile/Platforms/Android/Services/BankSlipMonitorWorker.cs
// Android WorkManager worker that monitors for new bank slip images

#if ANDROID
using Android.Content;
using AndroidX.Work;

namespace NewwaysAdmin.Mobile.Platforms.Android.Services
{
    /// <summary>
    /// WorkManager worker that scans for new bank slip images and uploads them
    /// Triggered by MediaStore content changes
    /// </summary>
    public class BankSlipMonitorWorker : Worker
    {
        private const string TAG = "BankSlipMonitor";
        private readonly Context _context;

        public BankSlipMonitorWorker(Context context, WorkerParameters workerParams)
            : base(context, workerParams)
        {
            _context = context;
        }

        public override Result DoWork()
        {
            try
            {
                global::Android.Util.Log.Info(TAG, "BankSlipMonitorWorker started");

                // Get the service from the MAUI service provider
                var bankSlipService = GetBankSlipService();
                if (bankSlipService == null)
                {
                    global::Android.Util.Log.Warn(TAG, "BankSlipService not available");
                    ReEnqueueWorker();
                    return Result.InvokeSuccess();
                }

                // Run the scan synchronously (WorkManager handles threading)
                var scanTask = bankSlipService.ScanAndUploadAsync();
                scanTask.Wait();
                var result = scanTask.Result;

                global::Android.Util.Log.Info(TAG,
                    $"Scan complete: {result.NewFilesFound} new, {result.UploadedCount} uploaded, {result.FailedCount} failed");

                // Re-enqueue to keep monitoring
                ReEnqueueWorker();

                return Result.InvokeSuccess();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(TAG, $"Worker error: {ex.Message}");

                // Re-enqueue even on failure
                ReEnqueueWorker();

                return Result.InvokeFailure();
            }
        }

        private NewwaysAdmin.Mobile.Services.BankSlip.BankSlipService? GetBankSlipService()
        {
            try
            {
                // Access MAUI's service provider
                var app = Microsoft.Maui.Controls.Application.Current;
                if (app?.Handler?.MauiContext?.Services != null)
                {
                    return app.Handler.MauiContext.Services
                        .GetService<NewwaysAdmin.Mobile.Services.BankSlip.BankSlipService>();
                }
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(TAG, $"Failed to get service: {ex.Message}");
            }
            return null;
        }

        private void ReEnqueueWorker()
        {
            try
            {
                BankSlipWorkerManager.EnqueueMonitorWorker(_context);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error(TAG, $"Failed to re-enqueue worker: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Helper class to manage WorkManager operations
    /// </summary>
    public static class BankSlipWorkerManager
    {
        private const string WORKER_TAG = "bank_slip_monitor";
        private const string UNIQUE_WORK_NAME = "bank_slip_auto_sync";

        /// <summary>
        /// Enqueue the monitor worker with MediaStore content trigger
        /// </summary>
        public static void EnqueueMonitorWorker(Context context)
        {
            try
            {
                // Build constraints - trigger on MediaStore changes
                var constraints = new AndroidX.Work.Constraints.Builder()
                    .AddContentUriTrigger(
                        global::Android.Provider.MediaStore.Images.Media.ExternalContentUri,
                        triggerForDescendants: true)
                    .Build();

                // Create the work request
                var workRequest = new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(BankSlipMonitorWorker)))
                    .SetConstraints(constraints)
                    .AddTag(WORKER_TAG)
                    .Build();

                // Enqueue as unique work (replace if existing)
                WorkManager.GetInstance(context)
                    .EnqueueUniqueWork(
                        UNIQUE_WORK_NAME,
                        ExistingWorkPolicy.Replace,
                        workRequest);

                global::Android.Util.Log.Info("BankSlipWorkerManager", "Worker enqueued successfully");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("BankSlipWorkerManager", $"Failed to enqueue worker: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancel the monitor worker
        /// </summary>
        public static void CancelMonitorWorker(Context context)
        {
            try
            {
                WorkManager.GetInstance(context).CancelUniqueWork(UNIQUE_WORK_NAME);
                global::Android.Util.Log.Info("BankSlipWorkerManager", "Worker cancelled");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("BankSlipWorkerManager", $"Failed to cancel worker: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if worker is currently enqueued/running
        /// </summary>
        public static bool IsWorkerActive(Context context)
        {
            try
            {
                var workInfoList = WorkManager.GetInstance(context)
                    .GetWorkInfosForUniqueWork(UNIQUE_WORK_NAME)
                    .Get();

                if (workInfoList is Java.Util.IList list)
                {
                    for (int i = 0; i < list.Size(); i++)
                    {
                        if (list.Get(i) is WorkInfo workInfo)
                        {
                            if (workInfo.GetState() == WorkInfo.State.Enqueued ||
                                workInfo.GetState() == WorkInfo.State.Running)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Force an immediate scan (for manual trigger from UI)
        /// </summary>
        public static void TriggerImmediateScan(Context context)
        {
            try
            {
                var workRequest = new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(BankSlipMonitorWorker)))
                    .AddTag(WORKER_TAG + "_manual")
                    .Build();

                WorkManager.GetInstance(context).Enqueue(workRequest);
                global::Android.Util.Log.Info("BankSlipWorkerManager", "Manual scan triggered");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("BankSlipWorkerManager", $"Failed to trigger scan: {ex.Message}");
            }
        }
    }
}
#endif