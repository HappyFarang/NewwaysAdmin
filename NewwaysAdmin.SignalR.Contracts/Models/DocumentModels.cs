// File: NewwaysAdmin.SignalR.Contracts/Models/DocumentModels.cs
// Models for document upload and sync via SignalR

namespace NewwaysAdmin.SignalR.Contracts.Models
{
    /// <summary>
    /// Request to upload a document (bank slip, receipt, etc.) from mobile device
    /// </summary>
    public class DocumentUploadRequest
    {
        /// <summary>
        /// Source folder name that identifies the document type/bank
        /// e.g., "kbiz", "kplus", "bangkokbank"
        /// </summary>
        public string SourceFolder { get; set; } = string.Empty;

        /// <summary>
        /// Original filename on the device
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Image data as base64 encoded string
        /// </summary>
        public string ImageBase64 { get; set; } = string.Empty;

        /// <summary>
        /// When the file was detected on the device
        /// </summary>
        public DateTime DeviceTimestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Unique identifier for the device uploading
        /// </summary>
        public string DeviceId { get; set; } = string.Empty;

        /// <summary>
        /// Username of the person uploading (for folder routing)
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// File size in bytes (for validation)
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// MIME type if known (e.g., "image/jpeg")
        /// </summary>
        public string? ContentType { get; set; }
    }

    /// <summary>
    /// Response after document upload attempt
    /// </summary>
    public class DocumentUploadResponse
    {
        /// <summary>
        /// Whether the upload was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Server-assigned unique document ID
        /// </summary>
        public string? DocumentId { get; set; }

        /// <summary>
        /// Human-readable status message
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Server timestamp when processed
        /// </summary>
        public DateTime ServerTimestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Path where document was stored (for debugging)
        /// </summary>
        public string? StoragePath { get; set; }

        /// <summary>
        /// Error details if upload failed
        /// </summary>
        public string? ErrorDetails { get; set; }

        // Factory methods for clean responses
        public static DocumentUploadResponse CreateSuccess(string documentId, string storagePath)
        {
            return new DocumentUploadResponse
            {
                Success = true,
                DocumentId = documentId,
                Message = "Document uploaded successfully",
                StoragePath = storagePath
            };
        }

        public static DocumentUploadResponse CreateError(string message, string? details = null)
        {
            return new DocumentUploadResponse
            {
                Success = false,
                Message = message,
                ErrorDetails = details
            };
        }
    }

    /// <summary>
    /// Mapping from source folder to OCR processing settings and storage location
    /// </summary>
    public class DocumentSourceMapping
    {
        /// <summary>
        /// Folder name as sent from mobile (e.g., "kbiz")
        /// </summary>
        public string FolderName { get; set; } = string.Empty;

        /// <summary>
        /// Display name for UI and folder organization (e.g., "KBIZ")
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// IO Manager storage folder name (e.g., "BankSlips_KBIZ")
        /// </summary>
        public string StorageFolderName { get; set; } = string.Empty;

        /// <summary>
        /// OCR document type for pattern matching (e.g., "BankSlips")
        /// </summary>
        public string OcrDocumentType { get; set; } = "BankSlips";

        /// <summary>
        /// OCR format name for pattern matching (e.g., "KBIZ")
        /// </summary>
        public string OcrFormatName { get; set; } = string.Empty;

        /// <summary>
        /// Is this mapping active?
        /// </summary>
        public bool IsActive { get; set; } = true;
    }
}