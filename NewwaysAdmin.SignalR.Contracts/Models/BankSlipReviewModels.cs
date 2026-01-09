// NewwaysAdmin.SignalR.Contracts/Models/BankSlipReviewModels.cs
// Contract models for bank slip project sync (mobile <-> server)
// Philosophy: Sync actual files, don't transform data

namespace NewwaysAdmin.SignalR.Contracts.Models;

#region Sync Metadata Exchange

/// <summary>
/// Request to sync project metadata between mobile and server.
/// Mobile sends what it has, server responds with what to do.
/// </summary>
public class SyncProjectMetadataRequest
{
    /// <summary>
    /// List of projects mobile currently has with their last modified times
    /// </summary>
    public List<ProjectSyncInfo> LocalProjects { get; set; } = new();

    /// <summary>
    /// Only sync projects from this date forward (for initial sync efficiency)
    /// Default: 3 months ago
    /// </summary>
    public DateTime? SyncFromDate { get; set; }

    /// <summary>
    /// Device identifier for logging
    /// </summary>
    public string? DeviceId { get; set; }
}

/// <summary>
/// Lightweight info about a project for sync comparison
/// </summary>
public class ProjectSyncInfo
{
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Last modified timestamp of the project file
    /// </summary>
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Number of bills attached (to detect bill changes)
    /// </summary>
    public int BillCount { get; set; }
}

/// <summary>
/// Response with sync instructions
/// </summary>
public class SyncProjectMetadataResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Projects that need to be pulled from server (newer on server or missing locally)
    /// </summary>
    public List<ProjectSyncAction> ProjectsToPull { get; set; } = new();

    /// <summary>
    /// Projects that server wants from mobile (newer locally - rare, but possible)
    /// </summary>
    public List<ProjectSyncAction> ProjectsToPush { get; set; } = new();

    /// <summary>
    /// Projects to delete locally (deleted on server or too old)
    /// </summary>
    public List<string> ProjectsToDelete { get; set; } = new();

    /// <summary>
    /// Bill files that need to be pulled (attached since last sync)
    /// </summary>
    public List<BillSyncInfo> BillsToPull { get; set; } = new();

    /// <summary>
    /// Server timestamp for this sync (save for next sync request)
    /// </summary>
    public DateTime ServerTimestamp { get; set; }

    /// <summary>
    /// Distinct person names for filter dropdown
    /// </summary>
    public List<string> AvailablePersons { get; set; } = new();

    public static SyncProjectMetadataResponse FromSuccess()
    {
        return new SyncProjectMetadataResponse
        {
            Success = true,
            ServerTimestamp = DateTime.UtcNow
        };
    }

    public static SyncProjectMetadataResponse FromError(string error)
    {
        return new SyncProjectMetadataResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

/// <summary>
/// Sync action for a project
/// </summary>
public class ProjectSyncAction
{
    public string ProjectId { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }

    /// <summary>
    /// Why this action is needed
    /// </summary>
    public SyncReason Reason { get; set; }
}

/// <summary>
/// Info about a bill file to sync
/// </summary>
public class BillSyncInfo
{
    public string ProjectId { get; set; } = string.Empty;
    public string BillId { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

public enum SyncReason
{
    /// <summary>New on server, doesn't exist locally</summary>
    NewOnServer,

    /// <summary>Server version is newer</summary>
    ServerNewer,

    /// <summary>Local version is newer (push to server)</summary>
    LocalNewer,

    /// <summary>New bill attached</summary>
    NewBill
}

#endregion

#region Pull Project File

/// <summary>
/// Request to download a project JSON file from server
/// </summary>
public class PullProjectRequest
{
    public string ProjectId { get; set; } = string.Empty;
}

/// <summary>
/// Response with the actual project JSON
/// </summary>
public class PullProjectResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The project JSON content (as string, not transformed)
    /// Mobile saves this directly to local storage
    /// </summary>
    public string? ProjectJson { get; set; }

    /// <summary>
    /// Server's last modified time for this file
    /// </summary>
    public DateTime LastModified { get; set; }

    public static PullProjectResponse FromSuccess(string json, DateTime lastModified)
    {
        return new PullProjectResponse
        {
            Success = true,
            ProjectJson = json,
            LastModified = lastModified
        };
    }

    public static PullProjectResponse FromError(string error)
    {
        return new PullProjectResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion

#region Push Project File

/// <summary>
/// Request to upload a project JSON file to server
/// Used when mobile has edits to sync back
/// </summary>
public class PushProjectRequest
{
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// The project JSON content
    /// </summary>
    public string ProjectJson { get; set; } = string.Empty;

    /// <summary>
    /// Mobile's last modified time
    /// Server uses this for conflict detection
    /// </summary>
    public DateTime LocalLastModified { get; set; }

    /// <summary>
    /// Device/user making the change
    /// </summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Response after pushing project to server
/// </summary>
public class PushProjectResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Server's final version after save
    /// (May differ if server applied additional processing)
    /// </summary>
    public string? FinalProjectJson { get; set; }

    /// <summary>
    /// Server's timestamp for the saved file
    /// </summary>
    public DateTime ServerLastModified { get; set; }

    /// <summary>
    /// True if there was a conflict (server had newer changes)
    /// In this case, FinalProjectJson contains server's version
    /// </summary>
    public bool HadConflict { get; set; }

    public static PushProjectResponse FromSuccess(string finalJson, DateTime lastModified)
    {
        return new PushProjectResponse
        {
            Success = true,
            FinalProjectJson = finalJson,
            ServerLastModified = lastModified,
            HadConflict = false
        };
    }

    public static PushProjectResponse FromConflict(string serverJson, DateTime serverModified)
    {
        return new PushProjectResponse
        {
            Success = true, // Not an error, just a conflict
            FinalProjectJson = serverJson,
            ServerLastModified = serverModified,
            HadConflict = true
        };
    }

    public static PushProjectResponse FromError(string error)
    {
        return new PushProjectResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion

#region Pull Bank Slip Image

/// <summary>
/// Request to get the original bank slip screenshot
/// </summary>
public class PullBankSlipImageRequest
{
    public string ProjectId { get; set; } = string.Empty;
}

/// <summary>
/// Response with bank slip image
/// </summary>
public class PullBankSlipImageResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Image as base64 encoded string
    /// </summary>
    public string? ImageBase64 { get; set; }

    /// <summary>
    /// Original filename
    /// </summary>
    public string? Filename { get; set; }

    /// <summary>
    /// Content type (e.g., "image/jpeg", "image/png")
    /// </summary>
    public string? ContentType { get; set; }

    public static PullBankSlipImageResponse FromSuccess(string base64, string filename, string contentType)
    {
        return new PullBankSlipImageResponse
        {
            Success = true,
            ImageBase64 = base64,
            Filename = filename,
            ContentType = contentType
        };
    }

    public static PullBankSlipImageResponse FromError(string error)
    {
        return new PullBankSlipImageResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion

#region Pull Bill Image

/// <summary>
/// Request to get a bill/receipt image
/// </summary>
public class PullBillImageRequest
{
    public string BillId { get; set; } = string.Empty;
}

/// <summary>
/// Response with bill image
/// </summary>
public class PullBillImageResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Image as base64 encoded string
    /// </summary>
    public string? ImageBase64 { get; set; }

    /// <summary>
    /// Content type (e.g., "image/jpeg", "image/png")
    /// </summary>
    public string? ContentType { get; set; }

    public static PullBillImageResponse FromSuccess(string base64, string contentType)
    {
        return new PullBillImageResponse
        {
            Success = true,
            ImageBase64 = base64,
            ContentType = contentType
        };
    }

    public static PullBillImageResponse FromError(string error)
    {
        return new PullBillImageResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion

#region Batch Pull (Efficiency)

/// <summary>
/// Request to pull multiple projects at once
/// More efficient than individual requests during initial sync
/// </summary>
public class BatchPullProjectsRequest
{
    /// <summary>
    /// List of project IDs to pull
    /// </summary>
    public List<string> ProjectIds { get; set; } = new();
}

/// <summary>
/// Response with multiple project files
/// </summary>
public class BatchPullProjectsResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Map of ProjectId -> JSON content
    /// </summary>
    public Dictionary<string, string> Projects { get; set; } = new();

    /// <summary>
    /// Map of ProjectId -> LastModified
    /// </summary>
    public Dictionary<string, DateTime> LastModifiedTimes { get; set; } = new();

    /// <summary>
    /// Projects that failed to load (with error messages)
    /// </summary>
    public Dictionary<string, string> FailedProjects { get; set; } = new();

    public static BatchPullProjectsResponse FromSuccess(
        Dictionary<string, string> projects,
        Dictionary<string, DateTime> times)
    {
        return new BatchPullProjectsResponse
        {
            Success = true,
            Projects = projects,
            LastModifiedTimes = times
        };
    }

    public static BatchPullProjectsResponse FromError(string error)
    {
        return new BatchPullProjectsResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion

#region Batch Push (Offline Sync)

/// <summary>
/// Request to push multiple edited projects at once
/// Used when mobile comes back online with queued edits
/// </summary>
public class BatchPushProjectsRequest
{
    /// <summary>
    /// Projects to push with their JSON content
    /// </summary>
    public List<ProjectPushItem> Projects { get; set; } = new();

    /// <summary>
    /// Device/user making the changes
    /// </summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Single project to push in a batch
/// </summary>
public class ProjectPushItem
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectJson { get; set; } = string.Empty;
    public DateTime LocalLastModified { get; set; }
}

/// <summary>
/// Response after batch push
/// </summary>
public class BatchPushProjectsResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Results for each project pushed
    /// </summary>
    public List<ProjectPushResult> Results { get; set; } = new();

    /// <summary>
    /// Count of successful pushes
    /// </summary>
    public int SuccessCount => Results.Count(r => r.Success);

    /// <summary>
    /// Count of conflicts (server had newer)
    /// </summary>
    public int ConflictCount => Results.Count(r => r.HadConflict);

    /// <summary>
    /// Count of failures
    /// </summary>
    public int FailureCount => Results.Count(r => !r.Success);

    public static BatchPushProjectsResponse FromSuccess(List<ProjectPushResult> results)
    {
        return new BatchPushProjectsResponse
        {
            Success = true,
            Results = results
        };
    }

    public static BatchPushProjectsResponse FromError(string error)
    {
        return new BatchPushProjectsResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

/// <summary>
/// Result for a single project in batch push
/// </summary>
public class ProjectPushResult
{
    public string ProjectId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Server's final version (use this to update local copy)
    /// </summary>
    public string? FinalProjectJson { get; set; }

    /// <summary>
    /// Server's timestamp
    /// </summary>
    public DateTime ServerLastModified { get; set; }

    /// <summary>
    /// True if server had newer version - local changes lost
    /// Mobile should notify user and use server version
    /// </summary>
    public bool HadConflict { get; set; }

    public static ProjectPushResult FromSuccess(string projectId, string json, DateTime lastModified)
    {
        return new ProjectPushResult
        {
            ProjectId = projectId,
            Success = true,
            FinalProjectJson = json,
            ServerLastModified = lastModified,
            HadConflict = false
        };
    }

    public static ProjectPushResult FromConflict(string projectId, string serverJson, DateTime serverModified)
    {
        return new ProjectPushResult
        {
            ProjectId = projectId,
            Success = true, // Not an error, just conflict
            FinalProjectJson = serverJson,
            ServerLastModified = serverModified,
            HadConflict = true
        };
    }

    public static ProjectPushResult FromError(string projectId, string error)
    {
        return new ProjectPushResult
        {
            ProjectId = projectId,
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion

#region Close Project

/// <summary>
/// Request to mark project as closed (reviewed and complete)
/// Convenience method - sets IsClosed=true and syncs back
/// </summary>
public class CloseProjectRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string? ClosedBy { get; set; }
}

/// <summary>
/// Response after closing project
/// </summary>
public class CloseProjectResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Updated project JSON (with IsClosed = true)
    /// </summary>
    public string? UpdatedProjectJson { get; set; }

    /// <summary>
    /// Server timestamp after update
    /// </summary>
    public DateTime LastModified { get; set; }

    public static CloseProjectResponse FromSuccess(string updatedJson, DateTime lastModified)
    {
        return new CloseProjectResponse
        {
            Success = true,
            UpdatedProjectJson = updatedJson,
            LastModified = lastModified
        };
    }

    public static CloseProjectResponse FromError(string error)
    {
        return new CloseProjectResponse
        {
            Success = false,
            ErrorMessage = error
        };
    }
}

#endregion