// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/Services/BankSlipReviewSyncService.cs
// Handles sync of bank slip projects between mobile and server
// Philosophy: Mirror server files, minimal transformation

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SignalR.Contracts.Models;
using System.Text.Json;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.Services;

/// <summary>
/// Syncs bank slip projects with server and manages local storage.
/// Uses existing CategoryHubConnector for SignalR communication.
/// 
/// Storage layout (mirrors server):
/// - BankSlipProjects/Projects/{ProjectId}.json
/// - BankSlipBills/{BillId}.jpg
/// - BankSlipReview/sync_status.json
/// - BankSlipReview/local_index.json
/// </summary>
public class BankSlipReviewSyncService
{
    private readonly ILogger<BankSlipReviewSyncService> _logger;
    private readonly CategoryHubConnector _hubConnector;
    private readonly BankSlipLocalStorage _localStorage;

    private bool _isSyncing = false;
    private DateTime _lastSyncTime = DateTime.MinValue;

    public event EventHandler<SyncProgressEventArgs>? SyncProgress;
    public event EventHandler<SyncCompleteEventArgs>? SyncComplete;
    public event EventHandler<ProjectChangedEventArgs>? ProjectChanged;

    public BankSlipReviewSyncService(
        ILogger<BankSlipReviewSyncService> logger,
        CategoryHubConnector hubConnector,
        BankSlipLocalStorage localStorage)
    {
        _logger = logger;
        _hubConnector = hubConnector;
        _localStorage = localStorage;
    }

    #region Public Properties

    public bool IsSyncing => _isSyncing;
    public DateTime LastSyncTime => _lastSyncTime;
    public bool IsOnline => _hubConnector.IsConnected;

    #endregion

    #region Sync Operations

    /// <summary>
    /// Full sync with server - call on app startup and periodically
    /// </summary>
    public async Task<SyncResult> SyncWithServerAsync()
    {
        if (_isSyncing)
        {
            _logger.LogDebug("Sync already in progress, skipping");
            return new SyncResult { Skipped = true };
        }

        if (!_hubConnector.IsConnected)
        {
            _logger.LogWarning("Cannot sync - not connected to server");
            return new SyncResult { Success = false, ErrorMessage = "Not connected" };
        }

        _isSyncing = true;
        var result = new SyncResult();

        try
        {
            _logger.LogInformation("Starting bank slip project sync");
            RaiseSyncProgress("Starting sync...", 0);

            // Step 1: Get local project metadata
            var localProjects = await _localStorage.GetLocalProjectMetadataAsync();
            RaiseSyncProgress("Checking local projects...", 10);

            // Step 2: Ask server what needs syncing
            var syncRequest = new SyncProjectMetadataRequest
            {
                LocalProjects = localProjects,
                SyncFromDate = DateTime.UtcNow.AddMonths(-3),
                DeviceId = GetDeviceId()
            };

            var syncResponse = await SendMessageAsync<SyncProjectMetadataRequest, SyncProjectMetadataResponse>(
                "SyncProjectMetadata", syncRequest);

            if (syncResponse == null || !syncResponse.Success)
            {
                throw new Exception(syncResponse?.ErrorMessage ?? "Failed to get sync metadata");
            }

            RaiseSyncProgress($"Found {syncResponse.ProjectsToPull.Count} to download, {syncResponse.ProjectsToPush.Count} to upload", 20);

            // Step 3: Pull new/updated projects from server
            if (syncResponse.ProjectsToPull.Count > 0)
            {
                await PullProjectsAsync(syncResponse.ProjectsToPull, result);
            }
            RaiseSyncProgress("Downloaded projects", 50);

            // Step 4: Push local changes to server
            if (syncResponse.ProjectsToPush.Count > 0)
            {
                await PushProjectsAsync(syncResponse.ProjectsToPush, result);
            }
            RaiseSyncProgress("Uploaded changes", 70);

            // Step 5: Pull new bills
            if (syncResponse.BillsToPull.Count > 0)
            {
                await PullBillsAsync(syncResponse.BillsToPull, result);
            }
            RaiseSyncProgress("Downloaded bills", 85);

            // Step 6: Delete old projects (only if closed and > 3 months old)
            if (syncResponse.ProjectsToDelete.Count > 0)
            {
                await DeleteProjectsAsync(syncResponse.ProjectsToDelete, result);
            }

            // Step 7: Update available persons for filter
            await _localStorage.SaveAvailablePersonsAsync(syncResponse.AvailablePersons);

            // Step 8: Run local cleanup (3-month rule for closed projects)
            await RunLocalCleanupAsync();

            _lastSyncTime = DateTime.UtcNow;
            await _localStorage.SaveSyncStatusAsync(_lastSyncTime);

            result.Success = true;
            RaiseSyncProgress("Sync complete!", 100);

            _logger.LogInformation(
                "Sync complete: {Pulled} pulled, {Pushed} pushed, {Conflicts} conflicts, {Deleted} deleted",
                result.ProjectsPulled, result.ProjectsPushed, result.Conflicts, result.ProjectsDeleted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            _isSyncing = false;
            SyncComplete?.Invoke(this, new SyncCompleteEventArgs(result));
        }

        return result;
    }

    /// <summary>
    /// Push a single project immediately (after local edit)
    /// </summary>
    public async Task<bool> PushProjectAsync(string projectId)
    {
        if (!_hubConnector.IsConnected)
        {
            _logger.LogWarning("Cannot push - offline. Will sync when reconnected.");
            await _localStorage.MarkProjectForPushAsync(projectId);
            return false;
        }

        try
        {
            var project = await _localStorage.LoadProjectAsync(projectId);
            if (project == null)
            {
                _logger.LogWarning("Project not found locally: {ProjectId}", projectId);
                return false;
            }

            var request = new PushProjectRequest
            {
                ProjectId = projectId,
                ProjectJson = JsonSerializer.Serialize(project, _jsonOptions),
                LocalLastModified = project.ProcessedAt,
                UpdatedBy = GetUsername()
            };

            var response = await SendMessageAsync<PushProjectRequest, PushProjectResponse>(
                "PushProject", request);

            if (response == null || !response.Success)
            {
                _logger.LogWarning("Push failed: {Error}", response?.ErrorMessage);
                return false;
            }

            if (response.HadConflict)
            {
                _logger.LogWarning("Conflict detected for {ProjectId} - server version wins", projectId);
                // Save server's version locally
                await _localStorage.SaveProjectJsonAsync(projectId, response.FinalProjectJson!, response.ServerLastModified);
                ProjectChanged?.Invoke(this, new ProjectChangedEventArgs(projectId, ChangeType.ConflictResolved));
            }
            else
            {
                // Update local with server's timestamp
                await _localStorage.UpdateProjectTimestampAsync(projectId, response.ServerLastModified);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing project {ProjectId}", projectId);
            return false;
        }
    }

    /// <summary>
    /// Upload a new bill for a project
    /// </summary>
    public async Task<BillUploadResult> UploadBillAsync(string projectId, byte[] imageData, string? filename = null)
    {
        if (!_hubConnector.IsConnected)
        {
            _logger.LogWarning("Cannot upload bill - offline. Queueing for later.");
            await _localStorage.QueueBillUploadAsync(projectId, imageData, filename);
            return new BillUploadResult { Success = false, Queued = true };
        }

        try
        {
            var request = new BillUploadRequest
            {
                ProjectId = projectId,
                ImageDataBase64 = Convert.ToBase64String(imageData),
                OriginalFilename = filename,
                Username = GetUsername()
            };

            var response = await SendMessageAsync<BillUploadRequest, BillUploadResponse>(
                "UploadBill", request);

            if (response == null || !response.Success)
            {
                return new BillUploadResult
                {
                    Success = false,
                    ErrorMessage = response?.ErrorMessage ?? "Upload failed"
                };
            }

            // Save bill locally
            await _localStorage.SaveBillAsync(response.BillId!, imageData);

            // Refresh project from server to get updated bill list
            await PullSingleProjectAsync(projectId);

            return new BillUploadResult
            {
                Success = true,
                BillId = response.BillId,
                BillNumber = response.BillNumber
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading bill for {ProjectId}", projectId);
            return new BillUploadResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Close a project (mark as reviewed)
    /// </summary>
    public async Task<bool> CloseProjectAsync(string projectId)
    {
        try
        {
            // Update locally first
            var project = await _localStorage.LoadProjectAsync(projectId);
            if (project == null) return false;

            project.IsClosed = true;
            project.ProcessedAt = DateTime.UtcNow;
            await _localStorage.SaveProjectAsync(project);

            // Push to server if online
            if (_hubConnector.IsConnected)
            {
                var request = new CloseProjectRequest
                {
                    ProjectId = projectId,
                    ClosedBy = GetUsername()
                };

                var response = await SendMessageAsync<CloseProjectRequest, CloseProjectResponse>(
                    "CloseProject", request);

                if (response?.Success == true && response.UpdatedProjectJson != null)
                {
                    await _localStorage.SaveProjectJsonAsync(projectId, response.UpdatedProjectJson, response.LastModified);
                }
            }
            else
            {
                await _localStorage.MarkProjectForPushAsync(projectId);
            }

            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs(projectId, ChangeType.Closed));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing project {ProjectId}", projectId);
            return false;
        }
    }

    #endregion

    #region Image Loading

    /// <summary>
    /// Get bank slip image (loads from server if not cached locally)
    /// </summary>
    public async Task<byte[]?> GetBankSlipImageAsync(string projectId)
    {
        // Try local first
        var localImage = await _localStorage.LoadBankSlipImageAsync(projectId);
        if (localImage != null) return localImage;

        // Fetch from server
        if (!_hubConnector.IsConnected) return null;

        try
        {
            var request = new PullBankSlipImageRequest { ProjectId = projectId };
            var response = await SendMessageAsync<PullBankSlipImageRequest, PullBankSlipImageResponse>(
                "PullBankSlipImage", request);

            if (response?.Success == true && response.ImageBase64 != null)
            {
                var imageBytes = Convert.FromBase64String(response.ImageBase64);
                await _localStorage.SaveBankSlipImageAsync(projectId, imageBytes, response.Filename);
                return imageBytes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching bank slip image for {ProjectId}", projectId);
        }

        return null;
    }

    /// <summary>
    /// Get bill image (loads from server if not cached locally)
    /// </summary>
    public async Task<byte[]?> GetBillImageAsync(string billId)
    {
        // Try local first
        var localImage = await _localStorage.LoadBillAsync(billId);
        if (localImage != null) return localImage;

        // Fetch from server
        if (!_hubConnector.IsConnected) return null;

        try
        {
            var request = new PullBillImageRequest { BillId = billId };
            var response = await SendMessageAsync<PullBillImageRequest, PullBillImageResponse>(
                "PullBillImage", request);

            if (response?.Success == true && response.ImageBase64 != null)
            {
                var imageBytes = Convert.FromBase64String(response.ImageBase64);
                await _localStorage.SaveBillAsync(billId, imageBytes);
                return imageBytes;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching bill image {BillId}", billId);
        }

        return null;
    }

    #endregion

    #region Private Sync Methods

    private async Task PullProjectsAsync(List<ProjectSyncAction> toPull, SyncResult result)
    {
        if (toPull.Count == 0) return;

        // Use batch pull for efficiency
        var projectIds = toPull.Select(p => p.ProjectId).ToList();
        var request = new BatchPullProjectsRequest { ProjectIds = projectIds };

        var response = await SendMessageAsync<BatchPullProjectsRequest, BatchPullProjectsResponse>(
            "BatchPullProjects", request);

        if (response?.Success != true)
        {
            _logger.LogWarning("Batch pull failed: {Error}", response?.ErrorMessage);
            return;
        }

        foreach (var kvp in response.Projects)
        {
            var projectId = kvp.Key;
            var json = kvp.Value;
            var lastModified = response.LastModifiedTimes.GetValueOrDefault(projectId, DateTime.UtcNow);

            await _localStorage.SaveProjectJsonAsync(projectId, json, lastModified);
            result.ProjectsPulled++;

            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs(projectId, ChangeType.Updated));
        }

        foreach (var failed in response.FailedProjects)
        {
            _logger.LogWarning("Failed to pull {ProjectId}: {Error}", failed.Key, failed.Value);
        }
    }

    private async Task PushProjectsAsync(List<ProjectSyncAction> toPush, SyncResult result)
    {
        var pushItems = new List<ProjectPushItem>();

        foreach (var action in toPush)
        {
            var project = await _localStorage.LoadProjectAsync(action.ProjectId);
            if (project == null) continue;

            pushItems.Add(new ProjectPushItem
            {
                ProjectId = action.ProjectId,
                ProjectJson = JsonSerializer.Serialize(project, _jsonOptions),
                LocalLastModified = action.LastModified
            });
        }

        if (pushItems.Count == 0) return;

        var request = new BatchPushProjectsRequest
        {
            Projects = pushItems,
            UpdatedBy = GetUsername()
        };

        var response = await SendMessageAsync<BatchPushProjectsRequest, BatchPushProjectsResponse>(
            "BatchPushProjects", request);

        if (response?.Success != true)
        {
            _logger.LogWarning("Batch push failed: {Error}", response?.ErrorMessage);
            return;
        }

        foreach (var pushResult in response.Results)
        {
            if (pushResult.Success)
            {
                if (pushResult.HadConflict)
                {
                    _logger.LogWarning("Conflict for {ProjectId} - using server version", pushResult.ProjectId);
                    await _localStorage.SaveProjectJsonAsync(
                        pushResult.ProjectId,
                        pushResult.FinalProjectJson!,
                        pushResult.ServerLastModified);
                    result.Conflicts++;
                }
                else
                {
                    await _localStorage.UpdateProjectTimestampAsync(
                        pushResult.ProjectId,
                        pushResult.ServerLastModified);
                    result.ProjectsPushed++;
                }

                await _localStorage.UnmarkProjectForPushAsync(pushResult.ProjectId);
            }
            else
            {
                _logger.LogWarning("Failed to push {ProjectId}: {Error}",
                    pushResult.ProjectId, pushResult.ErrorMessage);
            }
        }
    }

    private async Task PullBillsAsync(List<BillSyncInfo> billsToPull, SyncResult result)
    {
        foreach (var billInfo in billsToPull)
        {
            try
            {
                var request = new PullBillImageRequest { BillId = billInfo.BillId };
                var response = await SendMessageAsync<PullBillImageRequest, PullBillImageResponse>(
                    "PullBillImage", request);

                if (response?.Success == true && response.ImageBase64 != null)
                {
                    var imageBytes = Convert.FromBase64String(response.ImageBase64);
                    await _localStorage.SaveBillAsync(billInfo.BillId, imageBytes);
                    result.BillsPulled++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error pulling bill {BillId}", billInfo.BillId);
            }
        }
    }

    private async Task DeleteProjectsAsync(List<string> projectsToDelete, SyncResult result)
    {
        foreach (var projectId in projectsToDelete)
        {
            await _localStorage.DeleteProjectAsync(projectId);
            result.ProjectsDeleted++;
        }
    }

    private async Task PullSingleProjectAsync(string projectId)
    {
        var request = new PullProjectRequest { ProjectId = projectId };
        var response = await SendMessageAsync<PullProjectRequest, PullProjectResponse>(
            "PullProject", request);

        if (response?.Success == true && response.ProjectJson != null)
        {
            await _localStorage.SaveProjectJsonAsync(projectId, response.ProjectJson, response.LastModified);
            ProjectChanged?.Invoke(this, new ProjectChangedEventArgs(projectId, ChangeType.Updated));
        }
    }

    /// <summary>
    /// Local cleanup - delete closed projects older than 3 months
    /// Only runs on 1st of month (approximately)
    /// </summary>
    private async Task RunLocalCleanupAsync()
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddMonths(-3);
            var allProjects = await _localStorage.GetAllProjectsAsync();

            var toDelete = allProjects
                .Where(p => p.IsClosed && p.TransactionTimestamp < cutoffDate)
                .Select(p => p.ProjectId)
                .ToList();

            if (toDelete.Count > 0)
            {
                _logger.LogInformation("Cleaning up {Count} old closed projects", toDelete.Count);

                foreach (var projectId in toDelete)
                {
                    await _localStorage.DeleteProjectAsync(projectId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during local cleanup");
        }
    }

    #endregion

    #region SignalR Communication

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private async Task<TResponse?> SendMessageAsync<TRequest, TResponse>(string messageType, TRequest request)
        where TResponse : class
    {
        return await _hubConnector.SendMessageAsync<TRequest, TResponse>(messageType, request);
    }

    #endregion

    #region Helpers

    private string GetDeviceId()
    {
        // TODO: Get actual device ID
        return DeviceInfo.Current.Idiom.ToString() + "_" + DeviceInfo.Current.Name;
    }

    private string GetUsername()
    {
        // TODO: Get from auth service
        return "mobile_user";
    }

    private void RaiseSyncProgress(string message, int percentComplete)
    {
        SyncProgress?.Invoke(this, new SyncProgressEventArgs(message, percentComplete));
    }

    #endregion
}

#region Event Args & Result Classes

public class SyncProgressEventArgs : EventArgs
{
    public string Message { get; }
    public int PercentComplete { get; }

    public SyncProgressEventArgs(string message, int percentComplete)
    {
        Message = message;
        PercentComplete = percentComplete;
    }
}

public class SyncCompleteEventArgs : EventArgs
{
    public SyncResult Result { get; }

    public SyncCompleteEventArgs(SyncResult result)
    {
        Result = result;
    }
}

public class ProjectChangedEventArgs : EventArgs
{
    public string ProjectId { get; }
    public ChangeType ChangeType { get; }

    public ProjectChangedEventArgs(string projectId, ChangeType changeType)
    {
        ProjectId = projectId;
        ChangeType = changeType;
    }
}

public enum ChangeType
{
    Added,
    Updated,
    Deleted,
    Closed,
    ConflictResolved
}

public class SyncResult
{
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? ErrorMessage { get; set; }
    public int ProjectsPulled { get; set; }
    public int ProjectsPushed { get; set; }
    public int ProjectsDeleted { get; set; }
    public int BillsPulled { get; set; }
    public int Conflicts { get; set; }
}

public class BillUploadResult
{
    public bool Success { get; set; }
    public bool Queued { get; set; }
    public string? BillId { get; set; }
    public int BillNumber { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion