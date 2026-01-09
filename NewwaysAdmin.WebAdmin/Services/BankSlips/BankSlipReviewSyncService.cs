// NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipReviewSyncService.cs
// Handles sync operations between mobile and server for bank slip review
// Philosophy: Exchange actual files, minimal transformation

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SignalR.Contracts.Models;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;
using System.Text.Json;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips;

public class BankSlipReviewSyncService
{
    private readonly EnhancedStorageFactory _storageFactory;
    private readonly BillUploadService _billUploadService;
    private readonly BankSlipProjectService _projectService;
    private readonly ILogger<BankSlipReviewSyncService> _logger;

    private const string PROJECTS_FOLDER = "BankSlipJson";
    private const string PROJECTS_SUBFOLDER = "Projects";
    private const string BANKSLIPS_BIN_FOLDER = "BankSlipsBin";
    private const string BILLS_FOLDER = "BankSlipBill";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public BankSlipReviewSyncService(
    EnhancedStorageFactory storageFactory,
    BillUploadService billUploadService,
    BankSlipProjectService projectService,  // ADD
    ILogger<BankSlipReviewSyncService> logger)
    {
        _storageFactory = storageFactory;
        _billUploadService = billUploadService;
        _projectService = projectService;  // ADD
        _logger = logger;
    }

    #region Sync Metadata

    /// <summary>
    /// Compare mobile's project list with server and return sync instructions
    /// 
    /// MOBILE CLEANUP RULE:
    /// - Delete projects older than 3 months ONLY if IsClosed = true
    /// - Keep open projects regardless of age (avoid losing unfinished work)
    /// - Cleanup runs on 1st of month: e.g., April 1st deletes closed January projects
    /// </summary>
    public async Task<SyncProjectMetadataResponse> GetSyncMetadataAsync(SyncProjectMetadataRequest request)
    {
        try
        {
            var response = SyncProjectMetadataResponse.FromSuccess();
            var syncFromDate = request.SyncFromDate ?? DateTime.UtcNow.AddMonths(-3);

            // Build dictionary of local projects for quick lookup
            var localProjects = request.LocalProjects
                .ToDictionary(p => p.ProjectId, p => p);

            // Get all server projects - include ALL open projects regardless of date
            var serverProjects = await GetServerProjectsMetadataAsync(syncFromDate, includeAllOpen: true);
            var availablePersons = new HashSet<string>();

            // Track which server project IDs we've seen
            var serverProjectIds = new HashSet<string>();

            foreach (var serverProject in serverProjects)
            {
                serverProjectIds.Add(serverProject.ProjectId);

                // Track persons for filter dropdown
                if (!string.IsNullOrEmpty(serverProject.PersonName))
                {
                    availablePersons.Add(serverProject.PersonName);
                }

                if (localProjects.TryGetValue(serverProject.ProjectId, out var localProject))
                {
                    // Project exists on both - check which is newer
                    if (serverProject.LastModified > localProject.LastModified)
                    {
                        response.ProjectsToPull.Add(new ProjectSyncAction
                        {
                            ProjectId = serverProject.ProjectId,
                            LastModified = serverProject.LastModified,
                            Reason = SyncReason.ServerNewer
                        });
                    }
                    else if (localProject.LastModified > serverProject.LastModified)
                    {
                        response.ProjectsToPush.Add(new ProjectSyncAction
                        {
                            ProjectId = serverProject.ProjectId,
                            LastModified = localProject.LastModified,
                            Reason = SyncReason.LocalNewer
                        });
                    }

                    // Check for new bills
                    if (serverProject.BillCount > localProject.BillCount)
                    {
                        var billIds = await _billUploadService.GetBillReferencesAsync(serverProject.ProjectId);
                        foreach (var billId in billIds.Skip(localProject.BillCount))
                        {
                            response.BillsToPull.Add(new BillSyncInfo
                            {
                                ProjectId = serverProject.ProjectId,
                                BillId = billId,
                                AddedAt = DateTime.UtcNow // We don't track individual bill timestamps
                            });
                        }
                    }

                    // Remove from local list
                    localProjects.Remove(serverProject.ProjectId);
                }
                else
                {
                    // New on server
                    response.ProjectsToPull.Add(new ProjectSyncAction
                    {
                        ProjectId = serverProject.ProjectId,
                        LastModified = serverProject.LastModified,
                        Reason = SyncReason.NewOnServer
                    });
                }
            }

            // Remaining local projects: check if they truly don't exist on server
            // Only mark for deletion if project is genuinely gone from server
            // (Mobile handles age-based cleanup locally, respecting IsClosed status)
            foreach (var orphanedProjectId in localProjects.Keys)
            {
                // Check if this project actually exists on server (just outside our date range)
                var existsOnServer = await ProjectExistsOnServerAsync(orphanedProjectId);

                if (!existsOnServer)
                {
                    // Truly deleted from server - tell mobile to delete
                    response.ProjectsToDelete.Add(orphanedProjectId);
                }
                // If it exists but is old, mobile keeps it and handles cleanup locally
            }

            response.AvailablePersons = availablePersons.OrderBy(p => p).ToList();

            _logger.LogInformation(
                "Sync metadata: {Pull} to pull, {Push} to push, {Delete} to delete, {Bills} bills to pull",
                response.ProjectsToPull.Count,
                response.ProjectsToPush.Count,
                response.ProjectsToDelete.Count,
                response.BillsToPull.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sync metadata");
            return SyncProjectMetadataResponse.FromError($"Sync error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get lightweight metadata for projects
    /// </summary>
    /// <param name="fromDate">Only include closed projects from this date forward</param>
    /// <param name="includeAllOpen">If true, include ALL open projects regardless of date</param>
    private async Task<List<ServerProjectMetadata>> GetServerProjectsMetadataAsync(
    DateTime fromDate,
    bool includeAllOpen = true)
    {
        var result = new List<ServerProjectMetadata>();

        try
        {
            // Use the working BankSlipProjectService!
            var allProjects = await _projectService.GetAllProjectsAsync();
            _logger.LogInformation("Got {Count} projects from ProjectService", allProjects.Count);

            foreach (var project in allProjects)
            {
                // Include project if within date range OR open
                var isWithinDateRange = project.TransactionTimestamp >= fromDate;
                var isOpenProject = !project.IsClosed;

                if (!isWithinDateRange && !(includeAllOpen && isOpenProject))
                    continue;

                result.Add(new ServerProjectMetadata
                {
                    ProjectId = project.ProjectId,
                    LastModified = project.ProcessedAt,
                    PersonName = project.StructuredMemo?.PersonName,
                    BillCount = project.BillFileReferences?.Count ?? 0,
                    IsClosed = project.IsClosed
                });
            }

            _logger.LogInformation("Found {Count} projects for sync (after filtering)", result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing projects");
        }

        return result;
    }

    /// <summary>
    /// Check if a project exists on server (regardless of date range)
    /// </summary>
    private async Task<bool> ProjectExistsOnServerAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            return await storage.ExistsAsync($"{PROJECTS_SUBFOLDER}/{projectId}");
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Pull Projects

    /// <summary>
    /// Get a single project's JSON content
    /// </summary>
    public async Task<PullProjectResponse> PullProjectAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            var project = await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{projectId}");

            if (project == null)
            {
                return PullProjectResponse.FromError($"Project not found: {projectId}");
            }

            var json = JsonSerializer.Serialize(project, _jsonOptions);

            return PullProjectResponse.FromSuccess(json, project.ProcessedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling project {ProjectId}", projectId);
            return PullProjectResponse.FromError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get multiple projects' JSON content in one call
    /// </summary>
    public async Task<BatchPullProjectsResponse> BatchPullProjectsAsync(List<string> projectIds)
    {
        var projects = new Dictionary<string, string>();
        var times = new Dictionary<string, DateTime>();
        var failed = new Dictionary<string, string>();

        var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);

        foreach (var projectId in projectIds)
        {
            try
            {
                var project = await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{projectId}");

                if (project == null)
                {
                    failed[projectId] = "Not found";
                    continue;
                }

                var json = JsonSerializer.Serialize(project, _jsonOptions);
                projects[projectId] = json;
                times[projectId] = project.ProcessedAt;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading project {ProjectId} in batch", projectId);
                failed[projectId] = ex.Message;
            }
        }

        _logger.LogInformation("Batch pull: {Success} succeeded, {Failed} failed",
            projects.Count, failed.Count);

        var response = BatchPullProjectsResponse.FromSuccess(projects, times);
        response.FailedProjects = failed;
        return response;
    }

    #endregion

    #region Push Projects

    /// <summary>
    /// Save a project from mobile (with conflict detection)
    /// </summary>
    public async Task<PushProjectResponse> PushProjectAsync(PushProjectRequest request)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);

            // Deserialize incoming project
            var incomingProject = JsonSerializer.Deserialize<BankSlipProject>(
                request.ProjectJson, _jsonOptions);

            if (incomingProject == null)
            {
                return PushProjectResponse.FromError("Invalid project JSON");
            }

            // Check for conflict
            var existingProject = await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{request.ProjectId}");

            if (existingProject != null && existingProject.ProcessedAt > request.LocalLastModified)
            {
                // Server has newer version - conflict!
                _logger.LogInformation(
                    "Conflict detected for {ProjectId}: server={ServerTime}, local={LocalTime}",
                    request.ProjectId, existingProject.ProcessedAt, request.LocalLastModified);

                var serverJson = JsonSerializer.Serialize(existingProject, _jsonOptions);
                return PushProjectResponse.FromConflict(serverJson, existingProject.ProcessedAt);
            }

            // Save the incoming project
            incomingProject.ProcessedAt = DateTime.UtcNow; // Update timestamp
            await storage.SaveAsync($"{PROJECTS_SUBFOLDER}/{request.ProjectId}", incomingProject);

            // Update index
            await UpdateIndexAsync(incomingProject);

            var finalJson = JsonSerializer.Serialize(incomingProject, _jsonOptions);

            _logger.LogInformation("Project {ProjectId} pushed successfully by {User}",
                request.ProjectId, request.UpdatedBy ?? "unknown");

            return PushProjectResponse.FromSuccess(finalJson, incomingProject.ProcessedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pushing project {ProjectId}", request.ProjectId);
            return PushProjectResponse.FromError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Save multiple projects from mobile (with per-project conflict detection)
    /// </summary>
    public async Task<BatchPushProjectsResponse> BatchPushProjectsAsync(BatchPushProjectsRequest request)
    {
        var results = new List<ProjectPushResult>();

        foreach (var item in request.Projects)
        {
            try
            {
                var pushRequest = new PushProjectRequest
                {
                    ProjectId = item.ProjectId,
                    ProjectJson = item.ProjectJson,
                    LocalLastModified = item.LocalLastModified,
                    UpdatedBy = request.UpdatedBy
                };

                var pushResponse = await PushProjectAsync(pushRequest);

                if (pushResponse.Success)
                {
                    if (pushResponse.HadConflict)
                    {
                        results.Add(ProjectPushResult.FromConflict(
                            item.ProjectId,
                            pushResponse.FinalProjectJson!,
                            pushResponse.ServerLastModified));
                    }
                    else
                    {
                        results.Add(ProjectPushResult.FromSuccess(
                            item.ProjectId,
                            pushResponse.FinalProjectJson!,
                            pushResponse.ServerLastModified));
                    }
                }
                else
                {
                    results.Add(ProjectPushResult.FromError(
                        item.ProjectId,
                        pushResponse.ErrorMessage ?? "Unknown error"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in batch push for {ProjectId}", item.ProjectId);
                results.Add(ProjectPushResult.FromError(item.ProjectId, ex.Message));
            }
        }

        _logger.LogInformation(
            "Batch push complete: {Success} success, {Conflicts} conflicts, {Failed} failed",
            results.Count(r => r.Success && !r.HadConflict),
            results.Count(r => r.HadConflict),
            results.Count(r => !r.Success));

        return BatchPushProjectsResponse.FromSuccess(results);
    }

    #endregion

    #region Pull Images

    /// <summary>
    /// Get the original bank slip screenshot image
    /// </summary>
    public async Task<PullBankSlipImageResponse> PullBankSlipImageAsync(string projectId)
    {
        try
        {
            // Bank slip images are flat in BankSlipsBin/
            // Filename = ProjectId + extension
            // Example: KBIZ_Amy_01_01_2026_19_13_27.jpg

            var storage = _storageFactory.GetStorage<object>(BANKSLIPS_BIN_FOLDER);

            // Try common extensions
            string? foundFile = null;
            byte[]? imageBytes = null;

            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                try
                {
                    var filename = $"{projectId}{ext}";
                    var bytes = await storage.LoadRawAsync(filename);
                    if (bytes != null)
                    {
                        foundFile = filename;
                        imageBytes = bytes;
                        break;
                    }
                }
                catch
                {
                    // Try next extension
                }
            }

            if (imageBytes == null)
            {
                return PullBankSlipImageResponse.FromError($"Bank slip image not found for {projectId}");
            }

            var base64 = Convert.ToBase64String(imageBytes);
            var contentType = foundFile!.EndsWith(".png") ? "image/png" : "image/jpeg";

            return PullBankSlipImageResponse.FromSuccess(base64, foundFile, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling bank slip image for {ProjectId}", projectId);
            return PullBankSlipImageResponse.FromError($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a bill/receipt image
    /// </summary>
    public async Task<PullBillImageResponse> PullBillImageAsync(string billId)
    {
        try
        {
            var base64 = await _billUploadService.GetBillImageAsync(billId);

            if (base64 == null)
            {
                return PullBillImageResponse.FromError($"Bill not found: {billId}");
            }

            var contentType = billId.EndsWith(".png") ? "image/png" : "image/jpeg";
            return PullBillImageResponse.FromSuccess(base64, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pulling bill image {BillId}", billId);
            return PullBillImageResponse.FromError($"Error: {ex.Message}");
        }
    }

    #endregion

    #region Close Project

    /// <summary>
    /// Mark a project as closed (reviewed and complete)
    /// </summary>
    public async Task<CloseProjectResponse> CloseProjectAsync(CloseProjectRequest request)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            var project = await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{request.ProjectId}");

            if (project == null)
            {
                return CloseProjectResponse.FromError($"Project not found: {request.ProjectId}");
            }

            project.IsClosed = true;
            project.ProcessedAt = DateTime.UtcNow;

            await storage.SaveAsync($"{PROJECTS_SUBFOLDER}/{request.ProjectId}", project);
            await UpdateIndexAsync(project);

            var json = JsonSerializer.Serialize(project, _jsonOptions);

            _logger.LogInformation("Project {ProjectId} closed by {User}",
                request.ProjectId, request.ClosedBy ?? "unknown");

            return CloseProjectResponse.FromSuccess(json, project.ProcessedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing project {ProjectId}", request.ProjectId);
            return CloseProjectResponse.FromError($"Error: {ex.Message}");
        }
    }

    #endregion

    #region Index Management

    private async Task UpdateIndexAsync(BankSlipProject project)
    {
        try
        {
            var indexStorage = _storageFactory.GetStorage<List<BankSlipProjectIndexEntry>>(PROJECTS_FOLDER);
            var index = await indexStorage.LoadAsync("ProjectIndex") ?? new List<BankSlipProjectIndexEntry>();

            // Remove existing entry
            index.RemoveAll(e => e.ProjectId == project.ProjectId);

            // Add updated entry
            index.Add(BankSlipProjectIndexEntry.FromProject(project));

            await indexStorage.SaveAsync("ProjectIndex", index);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error updating index for {ProjectId}", project.ProjectId);
            // Don't fail the main operation
        }
    }

    #endregion

    #region Helper Classes

    private class ServerProjectMetadata
    {
        public string ProjectId { get; set; } = string.Empty;
        public DateTime LastModified { get; set; }
        public string? PersonName { get; set; }
        public int BillCount { get; set; }
        public bool IsClosed { get; set; }
    }

    #endregion
}