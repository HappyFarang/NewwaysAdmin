// File: NewwaysAdmin.WebAdmin/Services/Documents/DocumentModels.cs
// Data models for document storage service

namespace NewwaysAdmin.WebAdmin.Services.Documents
{
    /// <summary>
    /// Wrapper for image byte data - required for IO Manager binary storage
    /// (GetStorage requires T : class, new())
    /// </summary>
    public class ImageData
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();

        public ImageData() { }

        public ImageData(byte[] bytes)
        {
            Bytes = bytes;
        }
    }

    /// <summary>
    /// Configuration for all source types - stored in IO Manager
    /// </summary>
    public class SourceTypeConfig
    {
        public List<SourceTypeInfo> SourceTypes { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Info about a source type (bank app, bill type, etc.)
    /// </summary>
    public class SourceTypeInfo
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string OcrDocumentType { get; set; } = "BankSlips";
        public string OcrFormatName { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SourceTypeInfo() { }

        public SourceTypeInfo(string key, string displayName, string ocrDocType, string ocrFormat)
        {
            Key = key;
            DisplayName = displayName;
            OcrDocumentType = ocrDocType;
            OcrFormatName = ocrFormat;
        }
    }

    /// <summary>
    /// Result of a document save operation
    /// </summary>
    public class DocumentSaveResult
    {
        public bool Success { get; set; }
        public string? DocumentId { get; set; }
        public string? StoragePath { get; set; }
        public string? StorageFolderName { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// True if file already existed (idempotent return)
        /// </summary>
        public bool IsDuplicate { get; set; }

        public static DocumentSaveResult CreateSuccess(string documentId, string storagePath, string storageFolderName, bool isDuplicate = false)
            => new() { Success = true, DocumentId = documentId, StoragePath = storagePath, StorageFolderName = storageFolderName, IsDuplicate = isDuplicate };

        public static DocumentSaveResult CreateError(string errorMessage)
            => new() { Success = false, ErrorMessage = errorMessage };
    }
}