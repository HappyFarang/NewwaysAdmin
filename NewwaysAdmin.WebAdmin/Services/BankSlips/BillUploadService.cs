// NewwaysAdmin.WebAdmin/Services/BankSlips/BillUploadService.cs
// UPDATED: Uses new raw storage methods from IDataStorage

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips;

/// <summary>
/// Handles uploading and managing bill/receipt images for bank slip projects.
/// Uses RawFileMode storage - files stored directly as JPG/PNG without wrapper.
/// Naming convention: {ProjectId}_001.jpg, {ProjectId}_002.png, etc.
/// </summary>
public class BillUploadService
{
    private readonly ILogger<BillUploadService> _logger;
    private readonly EnhancedStorageFactory _storageFactory;

    private const string BILL_FOLDER = "BankSlipBill";
    private const string PROJECTS_FOLDER = "BankSlipJson";

    public BillUploadService(
        ILogger<BillUploadService> logger,
        EnhancedStorageFactory storageFactory)
    {
        _logger = logger;
        _storageFactory = storageFactory;
    }

    /// <summary>
    /// Upload a bill image for a project
    /// </summary>
    public async Task<BillUploadResult> UploadBillAsync(string projectId, byte[] imageData, string? originalFilename = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return BillUploadResult.Failure("Project ID is required");

            if (imageData == null || imageData.Length == 0)
                return BillUploadResult.Failure("Image data is empty");

            _logger.LogInformation("📷 Uploading bill for project: {ProjectId} ({Size} bytes)",
                projectId, imageData.Length);

            // Load project to get current bill count
            var project = await LoadProjectAsync(projectId);
            if (project == null)
                return BillUploadResult.Failure($"Project not found: {projectId}");

            // Determine next bill number by counting existing files on disk
            var storage = _storageFactory.GetStorage<object>(BILL_FOLDER);
            var existingBills = await storage.ListRawFilesAsync($"{projectId}_*");
            var nextBillNumber = existingBills.Count() + 1;

            // Determine file extension from image data
            var extension = DetectImageExtension(imageData, originalFilename);
            var billFilename = $"{projectId}_{nextBillNumber}{extension}";

            // Save the bill
            await storage.SaveRawAsync(billFilename, imageData);

            _logger.LogInformation("✅ Bill saved: {BillFilename}", billFilename);

            // Update project with bill reference
            project.BillFileReferences.Add(billFilename);
            project.HasBill = true;
            await SaveProjectAsync(project);

            _logger.LogInformation("✅ Project updated: {ProjectId} -> {BillFile}", projectId, billFilename);

            return BillUploadResult.Success(billFilename, nextBillNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error uploading bill for project: {ProjectId}", projectId);
            return BillUploadResult.Failure($"Upload failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Delete a bill from a project
    /// </summary>
    public async Task<bool> DeleteBillAsync(string projectId, string billFilename)
    {
        try
        {
            _logger.LogInformation("🗑️ Deleting bill: {BillFile} from project: {ProjectId}",
                billFilename, projectId);

            var storage = _storageFactory.GetStorage<object>(BILL_FOLDER);

            // Check if file actually exists on disk
            var exists = await storage.ExistsRawAsync(billFilename);
            if (!exists)
            {
                _logger.LogWarning("Bill file not found on disk: {BillFile}", billFilename);
                return false;
            }

            // Delete the actual file
            await storage.DeleteRawAsync(billFilename);

            // Try to update project references (best effort)
            var project = await LoadProjectAsync(projectId);
            if (project != null)
            {
                project.BillFileReferences.Remove(billFilename);
                project.HasBill = (await storage.ListRawFilesAsync($"{projectId}_*")).Any();
                await SaveProjectAsync(project);
            }

            _logger.LogInformation("✅ Bill deleted: {BillFile}", billFilename);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error deleting bill: {BillFile}", billFilename);
            return false;
        }
    }

    /// <summary>
    /// Get bill image as base64 data URI
    /// </summary>
    public async Task<string?> GetBillImageAsync(string billFilename)
    {
        try
        {
            var storage = _storageFactory.GetStorage<object>(BILL_FOLDER);
            var bytes = await storage.LoadRawAsync(billFilename);

            if (bytes == null || bytes.Length == 0)
            {
                _logger.LogDebug("Bill not found: {BillFile}", billFilename);
                return null;
            }

            var mimeType = DetectMimeType(bytes);
            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mimeType};base64,{base64}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading bill: {BillFile}", billFilename);
            return null;
        }
    }

    /// <summary>
    /// Get all bill references for a project
    /// </summary>
    public async Task<List<string>> GetBillReferencesAsync(string projectId)
    {
        var project = await LoadProjectAsync(projectId);
        return project?.BillFileReferences ?? new List<string>();
    }

    /// <summary>
    /// List all bills for a project (scans storage)
    /// </summary>
    public async Task<List<string>> ListBillsForProjectAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<object>(BILL_FOLDER);
            var allFiles = await storage.ListRawFilesAsync($"{projectId}_*");
            return allFiles.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing bills for: {ProjectId}", projectId);
            return new List<string>();
        }
    }

    #region Private Helpers

    private async Task<BankSlipProject?> LoadProjectAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            return await storage.LoadAsync(projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project: {ProjectId}", projectId);
            return null;
        }
    }

    private async Task SaveProjectAsync(BankSlipProject project)
    {
        var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
        await storage.SaveAsync(project.ProjectId, project);
    }

    private string DetectImageExtension(byte[] bytes, string? originalFilename)
    {
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ".png";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return ".jpg";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return ".gif";
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
                return ".webp";
        }

        if (!string.IsNullOrEmpty(originalFilename))
        {
            var ext = Path.GetExtension(originalFilename).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp")
                return ext;
        }

        return ".jpg";
    }

    private string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length < 4) return "image/jpeg";

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            return "image/webp";

        return "image/jpeg";
    }

    #endregion
}

/// <summary>
/// Result of a bill upload operation
/// </summary>
public class BillUploadResult
{
    public bool IsSuccess { get; private set; }
    public string? BillFilename { get; private set; }
    public int BillNumber { get; private set; }
    public string? ErrorMessage { get; private set; }

    private BillUploadResult() { }

    public static BillUploadResult Success(string billFilename, int billNumber) => new()
    {
        IsSuccess = true,
        BillFilename = billFilename,
        BillNumber = billNumber
    };

    public static BillUploadResult Failure(string errorMessage) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage
    };
}