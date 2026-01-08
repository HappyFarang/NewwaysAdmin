// NewwaysAdmin.SignalR.Contracts/Models/BillModels.cs

namespace NewwaysAdmin.SignalR.Contracts.Models;

/// <summary>
/// Request to upload a bill image for a bank slip project
/// </summary>
public class BillUploadRequest
{
    /// <summary>
    /// The project ID to attach the bill to
    /// Example: "KBIZ_Amy_01_01_2026_19_13_27"
    /// </summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Raw image data as base64 encoded string
    /// (SignalR handles byte[] poorly, base64 is more reliable)
    /// </summary>
    public string ImageDataBase64 { get; set; } = string.Empty;

    /// <summary>
    /// Original filename (for extension detection)
    /// Example: "IMG_20260107_123456.jpg"
    /// </summary>
    public string? OriginalFilename { get; set; }

    /// <summary>
    /// Username of the person uploading (for logging)
    /// </summary>
    public string? Username { get; set; }
}

/// <summary>
/// Response after bill upload attempt
/// </summary>
public class BillUploadResponse
{
    /// <summary>
    /// Whether the upload was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The bill ID if successful (e.g., "KBIZ_Amy_01_01_2026_19_13_27_001")
    /// </summary>
    public string? BillId { get; set; }

    /// <summary>
    /// Bill number for this project (1, 2, 3, etc.)
    /// </summary>
    public int BillNumber { get; set; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    public static BillUploadResponse FromSuccess(string billId, int billNumber)
    {
        return new BillUploadResponse
        {
            Success = true,
            BillId = billId,
            BillNumber = billNumber
        };
    }

    public static BillUploadResponse FromError(string errorMessage)
    {
        return new BillUploadResponse
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Request to delete a bill from a project
/// </summary>
public class BillDeleteRequest
{
    public string ProjectId { get; set; } = string.Empty;
    public string BillId { get; set; } = string.Empty;
    public string? Username { get; set; }
}

/// <summary>
/// Response after bill deletion attempt
/// </summary>
public class BillDeleteResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Request to get bill list for a project
/// </summary>
public class GetBillsRequest
{
    public string ProjectId { get; set; } = string.Empty;
}

/// <summary>
/// Response with bill references
/// </summary>
public class GetBillsResponse
{
    public bool Success { get; set; }
    public List<string> BillIds { get; set; } = new();
    public string? ErrorMessage { get; set; }
}