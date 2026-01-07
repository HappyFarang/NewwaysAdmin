// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/BankSlipImageService.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.WebAdmin.Services.Documents;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Loads bank slip and bill images from storage.
/// Returns base64 data URIs for direct use in img src.
/// </summary>
public class BankSlipImageService
{
    private readonly ILogger<BankSlipImageService> _logger;
    private readonly EnhancedStorageFactory _storageFactory;

    private const string BIN_FOLDER = "BankSlipsBin";
    private const string BILL_FOLDER = "BankSlipBill";

    public BankSlipImageService(
        ILogger<BankSlipImageService> logger,
        EnhancedStorageFactory storageFactory)
    {
        _logger = logger;
        _storageFactory = storageFactory;
    }

    /// <summary>
    /// Load original bank slip image as base64 data URI
    /// </summary>
    public async Task<string?> GetBankSlipImageAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<ImageData>(BIN_FOLDER);
            var imageData = await storage.LoadAsync(projectId);

            if (imageData?.Bytes == null || imageData.Bytes.Length == 0)
            {
                _logger.LogWarning("No image data found for project: {ProjectId}", projectId);
                return null;
            }

            return ToDataUri(imageData.Bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading bank slip image: {ProjectId}", projectId);
            return null;
        }
    }

    /// <summary>
    /// Load bill image by project ID and bill index (0-based)
    /// </summary>
    public async Task<string?> GetBillImageAsync(string projectId, int billIndex)
    {
        try
        {
            // Bill naming convention: {ProjectId}_1, {ProjectId}_2, etc. (1-based in filename)
            var billId = $"{projectId}_{billIndex + 1}";

            var storage = _storageFactory.GetStorage<ImageData>(BILL_FOLDER);
            var imageData = await storage.LoadAsync(billId);

            if (imageData?.Bytes == null || imageData.Bytes.Length == 0)
            {
                _logger.LogDebug("No bill image found: {BillId}", billId);
                return null;
            }

            return ToDataUri(imageData.Bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading bill image: {ProjectId} index {Index}", projectId, billIndex);
            return null;
        }
    }

    /// <summary>
    /// Get count of bills for a project
    /// </summary>
    public async Task<int> GetBillCountAsync(string projectId)
    {
        try
        {
            var billsPath = Path.Combine(
                StorageConfiguration.DEFAULT_BASE_DIRECTORY,
                BILL_FOLDER);

            if (!Directory.Exists(billsPath))
                return 0;

            // Count files matching {ProjectId}_*.bin pattern
            var pattern = $"{projectId}_*.bin";
            var files = Directory.GetFiles(billsPath, pattern);

            return files.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting bills for: {ProjectId}", projectId);
            return 0;
        }
    }

    /// <summary>
    /// Convert raw bytes to data URI for img src
    /// </summary>
    private string ToDataUri(byte[] bytes)
    {
        var mimeType = DetectMimeType(bytes);
        var base64 = Convert.ToBase64String(bytes);
        return $"data:{mimeType};base64,{base64}";
    }

    /// <summary>
    /// Detect MIME type from magic bytes
    /// </summary>
    private string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length < 4)
            return "image/jpeg"; // default

        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        // GIF: 47 49 46
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";

        // WebP: 52 49 46 46 ... 57 45 42 50
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            return "image/webp";

        return "image/jpeg"; // default
    }
}