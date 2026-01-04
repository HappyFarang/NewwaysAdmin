// File: NewwaysAdmin.Mobile/Platforms/Android/Services/FolderPickerService.cs
// Handles Android folder picker with proper async/await pattern

using Android.App;
using Android.Content;
using Android.Provider;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Platforms.Android.Services
{
    public interface IFolderPickerService
    {
        Task<string?> PickFolderAsync();
    }

    public class FolderPickerService : IFolderPickerService
    {
        private static TaskCompletionSource<string?>? _folderPickerTcs;
        private readonly ILogger<FolderPickerService>? _logger;

        public FolderPickerService(ILogger<FolderPickerService>? logger = null)
        {
            _logger = logger;
        }

        public async Task<string?> PickFolderAsync()
        {
            try
            {
                var activity = Platform.CurrentActivity;
                if (activity == null)
                {
                    _logger?.LogWarning("No current activity available");
                    return null;
                }

                _folderPickerTcs = new TaskCompletionSource<string?>();

                var intent = new Intent(Intent.ActionOpenDocumentTree);
                intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

                activity.StartActivityForResult(intent, FolderPickerRequestCode);

                // Wait for result (with timeout)
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2));
                var resultTask = _folderPickerTcs.Task;

                if (await Task.WhenAny(resultTask, timeoutTask) == timeoutTask)
                {
                    _logger?.LogWarning("Folder picker timed out");
                    _folderPickerTcs = null;
                    return null;
                }

                return await resultTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error picking folder");
                return null;
            }
        }

        // Called from MainActivity.OnActivityResult
        public static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            if (requestCode != FolderPickerRequestCode)
                return;

            if (_folderPickerTcs == null)
                return;

            if (resultCode != Result.Ok || data?.Data == null)
            {
                _folderPickerTcs.TrySetResult(null);
                return;
            }

            try
            {
                var uri = data.Data;
                var path = ConvertUriToPath(uri);
                _folderPickerTcs.TrySetResult(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting URI: {ex.Message}");
                _folderPickerTcs.TrySetResult(null);
            }
        }

        private static string? ConvertUriToPath(global::Android.Net.Uri uri)
        {
            // content://com.android.externalstorage.documents/tree/primary%3APictures%2FKBIZ
            // needs to become: /storage/emulated/0/Pictures/KBIZ

            var uriString = uri.ToString();

            if (uriString == null)
                return null;

            // Decode the URI
            var decoded = global::Android.Net.Uri.Decode(uriString);

            // Extract the path part after "tree/primary:"
            if (decoded.Contains("tree/primary:"))
            {
                var pathPart = decoded.Split("tree/primary:").LastOrDefault();
                if (!string.IsNullOrEmpty(pathPart))
                {
                    return $"/storage/emulated/0/{pathPart}";
                }
            }

            // Try document ID approach
            try
            {
                var docId = DocumentsContract.GetTreeDocumentId(uri);
                if (!string.IsNullOrEmpty(docId))
                {
                    // Format: "primary:Pictures/KBIZ"
                    if (docId.StartsWith("primary:"))
                    {
                        var relativePath = docId.Substring(8); // Remove "primary:"
                        return $"/storage/emulated/0/{relativePath}";
                    }
                }
            }
            catch
            {
                // Ignore and try fallback
            }

            System.Diagnostics.Debug.WriteLine($"Could not convert URI to path: {uriString}");
            return null;
        }

        public const int FolderPickerRequestCode = 1001;
    }
}