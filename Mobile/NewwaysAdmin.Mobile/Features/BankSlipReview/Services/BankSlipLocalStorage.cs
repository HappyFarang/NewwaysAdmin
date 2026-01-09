// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/Services/BankSlipLocalStorage.cs
// Handles local file storage for bank slip projects and bills
// Mirrors server structure for easy sync

using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SignalR.Contracts.Models;
using System.Text.Json;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.Services;

/// <summary>
/// Manages local storage for bank slip projects and bills.
/// 
/// Storage structure:
/// {AppData}/BankSlipReview/
///   ├── Projects/
///   │   ├── KBIZ_Amy_01_01_2026_19_13_27.json
///   │   └── ...
///   ├── Bills/
///   │   ├── KBIZ_Amy_01_01_2026_19_13_27_001.jpg
///   │   └── ...
///   ├── BankSlipImages/
///   │   └── {ProjectId}.jpg  (cached on demand)
///   ├── sync_status.json
///   ├── available_persons.json
///   ├── pending_push.json
///   └── pending_bill_uploads.json
/// </summary>
public class BankSlipLocalStorage
{
    private readonly ILogger<BankSlipLocalStorage> _logger;
    private readonly string _baseDirectory;

    private const string PROJECTS_FOLDER = "Projects";
    private const string BILLS_FOLDER = "Bills";
    private const string BANKSLIP_IMAGES_FOLDER = "BankSlipImages";
    private const string SYNC_STATUS_FILE = "sync_status.json";
    private const string AVAILABLE_PERSONS_FILE = "available_persons.json";
    private const string PENDING_PUSH_FILE = "pending_push.json";
    private const string PENDING_BILL_UPLOADS_FILE = "pending_bill_uploads.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public BankSlipLocalStorage(ILogger<BankSlipLocalStorage> logger)
    {
        _logger = logger;
        _baseDirectory = Path.Combine(FileSystem.AppDataDirectory, "BankSlipReview");
        EnsureDirectoriesExist();
    }

    #region Project Operations

    /// <summary>
    /// Get metadata for all local projects (for sync comparison)
    /// </summary>
    public async Task<List<ProjectSyncInfo>> GetLocalProjectMetadataAsync()
    {
        var result = new List<ProjectSyncInfo>();
        var projectsPath = Path.Combine(_baseDirectory, PROJECTS_FOLDER);

        if (!Directory.Exists(projectsPath))
            return result;

        foreach (var file in Directory.GetFiles(projectsPath, "*.json"))
        {
            try
            {
                var projectId = Path.GetFileNameWithoutExtension(file);
                var lastModified = File.GetLastWriteTimeUtc(file);

                // Count bills for this project
                var billCount = CountBillsForProject(projectId);

                result.Add(new ProjectSyncInfo
                {
                    ProjectId = projectId,
                    LastModified = lastModified,
                    BillCount = billCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading project metadata: {File}", file);
            }
        }

        return result;
    }

    /// <summary>
    /// Load a project by ID
    /// </summary>
    public async Task<BankSlipProject?> LoadProjectAsync(string projectId)
    {
        try
        {
            var filePath = GetProjectFilePath(projectId);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<BankSlipProject>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project {ProjectId}", projectId);
            return null;
        }
    }

    /// <summary>
    /// Save a project
    /// </summary>
    public async Task SaveProjectAsync(BankSlipProject project)
    {
        try
        {
            var filePath = GetProjectFilePath(project.ProjectId);
            var json = JsonSerializer.Serialize(project, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);

            // Update file timestamp to match ProcessedAt
            File.SetLastWriteTimeUtc(filePath, project.ProcessedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving project {ProjectId}", project.ProjectId);
            throw;
        }
    }

    /// <summary>
    /// Save project from raw JSON (from server)
    /// </summary>
    public async Task SaveProjectJsonAsync(string projectId, string json, DateTime lastModified)
    {
        try
        {
            var filePath = GetProjectFilePath(projectId);
            await File.WriteAllTextAsync(filePath, json);
            File.SetLastWriteTimeUtc(filePath, lastModified);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving project JSON {ProjectId}", projectId);
            throw;
        }
    }

    /// <summary>
    /// Update project's file timestamp (after successful push)
    /// </summary>
    public async Task UpdateProjectTimestampAsync(string projectId, DateTime timestamp)
    {
        var filePath = GetProjectFilePath(projectId);
        if (File.Exists(filePath))
        {
            File.SetLastWriteTimeUtc(filePath, timestamp);
        }
    }

    /// <summary>
    /// Delete a project and its associated bills
    /// </summary>
    public async Task DeleteProjectAsync(string projectId)
    {
        try
        {
            // Delete project file
            var projectPath = GetProjectFilePath(projectId);
            if (File.Exists(projectPath))
            {
                File.Delete(projectPath);
            }

            // Delete associated bills
            var billsPath = Path.Combine(_baseDirectory, BILLS_FOLDER);
            if (Directory.Exists(billsPath))
            {
                foreach (var billFile in Directory.GetFiles(billsPath, $"{projectId}_*"))
                {
                    File.Delete(billFile);
                }
            }

            // Delete cached bank slip image
            var imagePath = GetBankSlipImagePath(projectId);
            if (File.Exists(imagePath))
            {
                File.Delete(imagePath);
            }

            _logger.LogDebug("Deleted project and associated files: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// Get all projects (for local operations)
    /// </summary>
    public async Task<List<BankSlipProject>> GetAllProjectsAsync()
    {
        var result = new List<BankSlipProject>();
        var projectsPath = Path.Combine(_baseDirectory, PROJECTS_FOLDER);

        if (!Directory.Exists(projectsPath))
            return result;

        foreach (var file in Directory.GetFiles(projectsPath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var project = JsonSerializer.Deserialize<BankSlipProject>(json, _jsonOptions);
                if (project != null)
                {
                    result.Add(project);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading project: {File}", file);
            }
        }

        return result;
    }

    #endregion

    #region Bill Operations

    /// <summary>
    /// Save a bill image
    /// </summary>
    public async Task SaveBillAsync(string billId, byte[] imageData)
    {
        try
        {
            var filePath = GetBillFilePath(billId);
            await File.WriteAllBytesAsync(filePath, imageData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving bill {BillId}", billId);
            throw;
        }
    }

    /// <summary>
    /// Load a bill image
    /// </summary>
    public async Task<byte[]?> LoadBillAsync(string billId)
    {
        try
        {
            var filePath = GetBillFilePath(billId);
            if (!File.Exists(filePath))
                return null;

            return await File.ReadAllBytesAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading bill {BillId}", billId);
            return null;
        }
    }

    /// <summary>
    /// Get all bill IDs for a project
    /// </summary>
    public List<string> GetBillIdsForProject(string projectId)
    {
        var billsPath = Path.Combine(_baseDirectory, BILLS_FOLDER);
        if (!Directory.Exists(billsPath))
            return new List<string>();

        return Directory.GetFiles(billsPath, $"{projectId}_*")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(f => f != null)
            .Cast<string>()
            .ToList();
    }

    private int CountBillsForProject(string projectId)
    {
        var billsPath = Path.Combine(_baseDirectory, BILLS_FOLDER);
        if (!Directory.Exists(billsPath))
            return 0;

        return Directory.GetFiles(billsPath, $"{projectId}_*").Length;
    }

    #endregion

    #region Bank Slip Image Cache

    /// <summary>
    /// Save bank slip image to local cache
    /// </summary>
    public async Task SaveBankSlipImageAsync(string projectId, byte[] imageData, string? filename = null)
    {
        try
        {
            var ext = Path.GetExtension(filename ?? ".jpg");
            var filePath = Path.Combine(_baseDirectory, BANKSLIP_IMAGES_FOLDER, $"{projectId}{ext}");
            await File.WriteAllBytesAsync(filePath, imageData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving bank slip image {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// Load bank slip image from local cache
    /// </summary>
    public async Task<byte[]?> LoadBankSlipImageAsync(string projectId)
    {
        try
        {
            // Try common extensions
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var filePath = Path.Combine(_baseDirectory, BANKSLIP_IMAGES_FOLDER, $"{projectId}{ext}");
                if (File.Exists(filePath))
                {
                    return await File.ReadAllBytesAsync(filePath);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading bank slip image {ProjectId}", projectId);
            return null;
        }
    }

    private string GetBankSlipImagePath(string projectId)
    {
        // Return base path - actual extension varies
        return Path.Combine(_baseDirectory, BANKSLIP_IMAGES_FOLDER, projectId);
    }

    #endregion

    #region Sync Status & Metadata

    /// <summary>
    /// Save sync status
    /// </summary>
    public async Task SaveSyncStatusAsync(DateTime lastSyncTime)
    {
        var status = new SyncStatus { LastSyncTime = lastSyncTime };
        var json = JsonSerializer.Serialize(status, _jsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(_baseDirectory, SYNC_STATUS_FILE), json);
    }

    /// <summary>
    /// Load sync status
    /// </summary>
    public async Task<DateTime?> LoadLastSyncTimeAsync()
    {
        try
        {
            var filePath = Path.Combine(_baseDirectory, SYNC_STATUS_FILE);
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            var status = JsonSerializer.Deserialize<SyncStatus>(json, _jsonOptions);
            return status?.LastSyncTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Save available persons list (for filter dropdown)
    /// </summary>
    public async Task SaveAvailablePersonsAsync(List<string> persons)
    {
        var json = JsonSerializer.Serialize(persons, _jsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(_baseDirectory, AVAILABLE_PERSONS_FILE), json);
    }

    /// <summary>
    /// Load available persons list
    /// </summary>
    public async Task<List<string>> LoadAvailablePersonsAsync()
    {
        try
        {
            var filePath = Path.Combine(_baseDirectory, AVAILABLE_PERSONS_FILE);
            if (!File.Exists(filePath))
                return new List<string>();

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    #endregion

    #region Pending Operations (Offline Queue)

    /// <summary>
    /// Mark a project as needing push to server
    /// </summary>
    public async Task MarkProjectForPushAsync(string projectId)
    {
        var pending = await LoadPendingPushListAsync();
        if (!pending.Contains(projectId))
        {
            pending.Add(projectId);
            await SavePendingPushListAsync(pending);
        }
    }

    /// <summary>
    /// Unmark a project after successful push
    /// </summary>
    public async Task UnmarkProjectForPushAsync(string projectId)
    {
        var pending = await LoadPendingPushListAsync();
        if (pending.Remove(projectId))
        {
            await SavePendingPushListAsync(pending);
        }
    }

    /// <summary>
    /// Get all projects pending push
    /// </summary>
    public async Task<List<string>> GetPendingPushProjectsAsync()
    {
        return await LoadPendingPushListAsync();
    }

    private async Task<List<string>> LoadPendingPushListAsync()
    {
        try
        {
            var filePath = Path.Combine(_baseDirectory, PENDING_PUSH_FILE);
            if (!File.Exists(filePath))
                return new List<string>();

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task SavePendingPushListAsync(List<string> pending)
    {
        var json = JsonSerializer.Serialize(pending, _jsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(_baseDirectory, PENDING_PUSH_FILE), json);
    }

    /// <summary>
    /// Queue a bill upload for when we're back online
    /// </summary>
    public async Task QueueBillUploadAsync(string projectId, byte[] imageData, string? filename)
    {
        try
        {
            // Save image to temp location
            var tempId = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(filename ?? ".jpg");
            var tempPath = Path.Combine(_baseDirectory, "PendingBills", $"{tempId}{ext}");

            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            await File.WriteAllBytesAsync(tempPath, imageData);

            // Add to queue
            var queue = await LoadPendingBillUploadsAsync();
            queue.Add(new PendingBillUpload
            {
                TempId = tempId,
                ProjectId = projectId,
                TempFilePath = tempPath,
                OriginalFilename = filename,
                QueuedAt = DateTime.UtcNow
            });
            await SavePendingBillUploadsAsync(queue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queueing bill upload for {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// Get pending bill uploads
    /// </summary>
    public async Task<List<PendingBillUpload>> GetPendingBillUploadsAsync()
    {
        return await LoadPendingBillUploadsAsync();
    }

    /// <summary>
    /// Remove a bill from the upload queue
    /// </summary>
    public async Task RemovePendingBillUploadAsync(string tempId)
    {
        var queue = await LoadPendingBillUploadsAsync();
        var item = queue.FirstOrDefault(b => b.TempId == tempId);
        if (item != null)
        {
            queue.Remove(item);
            await SavePendingBillUploadsAsync(queue);

            // Delete temp file
            if (File.Exists(item.TempFilePath))
            {
                File.Delete(item.TempFilePath);
            }
        }
    }

    private async Task<List<PendingBillUpload>> LoadPendingBillUploadsAsync()
    {
        try
        {
            var filePath = Path.Combine(_baseDirectory, PENDING_BILL_UPLOADS_FILE);
            if (!File.Exists(filePath))
                return new List<PendingBillUpload>();

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<List<PendingBillUpload>>(json, _jsonOptions)
                ?? new List<PendingBillUpload>();
        }
        catch
        {
            return new List<PendingBillUpload>();
        }
    }

    private async Task SavePendingBillUploadsAsync(List<PendingBillUpload> queue)
    {
        var json = JsonSerializer.Serialize(queue, _jsonOptions);
        await File.WriteAllTextAsync(
            Path.Combine(_baseDirectory, PENDING_BILL_UPLOADS_FILE), json);
    }

    #endregion

    #region Helpers

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Path.Combine(_baseDirectory, PROJECTS_FOLDER));
        Directory.CreateDirectory(Path.Combine(_baseDirectory, BILLS_FOLDER));
        Directory.CreateDirectory(Path.Combine(_baseDirectory, BANKSLIP_IMAGES_FOLDER));
    }

    private string GetProjectFilePath(string projectId)
    {
        return Path.Combine(_baseDirectory, PROJECTS_FOLDER, $"{projectId}.json");
    }

    private string GetBillFilePath(string billId)
    {
        // Bill ID already includes extension or we default to .jpg
        var ext = Path.GetExtension(billId);
        if (string.IsNullOrEmpty(ext))
        {
            billId += ".jpg";
        }
        return Path.Combine(_baseDirectory, BILLS_FOLDER, billId);
    }

    #endregion
}

#region Helper Classes

public class SyncStatus
{
    public DateTime LastSyncTime { get; set; }
}

public class PendingBillUpload
{
    public string TempId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string TempFilePath { get; set; } = string.Empty;
    public string? OriginalFilename { get; set; }
    public DateTime QueuedAt { get; set; }
}

#endregion