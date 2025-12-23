// File: NewwaysAdmin.WebAdmin/Services/Documents/DocumentStorageService.cs
// Handles storage of uploaded documents using IO Manager
// 
// SIMPLIFIED: No predefined mappings - pattern name from mobile IS the pattern name
// OCR processor parses filename to get pattern: KPLUS_Superfox75_17_11_2025_10_04_36.bin → "KPLUS"
//
// Storage structure:
// BankSlipBill/                    ← Shared bills folder
// └── Superfox75_Bills_07_12_2025_18_00_30.bin
//
// BankSlipsBin/                    ← All bank slips (flat, pattern in filename)
// └── KPLUS_Superfox75_17_11_2025_10_04_36.bin

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.WebAdmin.Services.Documents
{
    public class DocumentStorageService
    {
        private readonly ILogger<DocumentStorageService> _logger;
        private readonly EnhancedStorageFactory _storageFactory;

        // Folder names
        private const string BILLS_FOLDER = "BankSlipBill";
        private const string BANKSLIPS_FOLDER = "BankSlipsBin";

        public DocumentStorageService(
            ILogger<DocumentStorageService> logger,
            EnhancedStorageFactory storageFactory)
        {
            _logger = logger;
            _storageFactory = storageFactory;

            _logger.LogInformation("DocumentStorageService initialized (simplified - no mapping layer)");
        }

        // ===== DOCUMENT STORAGE =====

        /// <summary>
        /// Save an uploaded document - IDEMPOTENT
        /// If same file (by device timestamp) already exists, returns success with existing path
        /// </summary>
        public async Task<DocumentSaveResult> SaveDocumentAsync(DocumentUploadRequest request)
        {
            try
            {
                // Validate request
                var validation = ValidateRequest(request);
                if (!validation.IsValid)
                {
                    return DocumentSaveResult.CreateError(validation.ErrorMessage!);
                }

                // Use pattern name EXACTLY as sent from mobile (just sanitized for filesystem safety)
                var patternName = SanitizeName(request.SourceFolder);
                var username = SanitizeName(request.Username);

                // Capitalize first letter of username for display
                var displayUsername = char.ToUpper(username[0]) + username.Substring(1);

                // Use DEVICE timestamp for idempotency - same file = same key
                var fileTimestamp = request.DeviceTimestamp;
                if (fileTimestamp == default || fileTimestamp == DateTime.MinValue)
                {
                    fileTimestamp = DateTime.UtcNow;
                    _logger.LogWarning("No device timestamp provided, using server time");
                }

                string binFolderName;
                string documentKey;

                // Bills go to shared folder, everything else goes to BankSlipsBin
                if (patternName.Contains("bill", StringComparison.OrdinalIgnoreCase))
                {
                    // Bills: shared folder
                    binFolderName = BILLS_FOLDER;
                    documentKey = $"{displayUsername}_Bills_{fileTimestamp:dd_MM_yyyy_HH_mm_ss}";
                }
                else
                {
                    // Bank slips: Pattern_User_Timestamp format
                    // e.g., KPLUS_Superfox75_17_11_2025_10_04_36
                    binFolderName = BANKSLIPS_FOLDER;
                    documentKey = $"{patternName}_{displayUsername}_{fileTimestamp:dd_MM_yyyy_HH_mm_ss}";
                }

                // Get storage
                var storage = _storageFactory.GetStorage<ImageData>(binFolderName);

                // ===== IDEMPOTENT CHECK =====
                // If file with this key already exists, return success with existing path
                // This prevents duplicates when mobile retries due to network issues
                if (await storage.ExistsAsync(documentKey))
                {
                    _logger.LogInformation(
                        "📋 DUPLICATE DETECTED - File already exists: {DocumentKey}. Returning existing path.",
                        documentKey);

                    var existingPath = $"{binFolderName}/{documentKey}";
                    return DocumentSaveResult.CreateSuccess(documentKey, existingPath, binFolderName, isDuplicate: true);
                }

                // Decode and save
                var imageBytes = Convert.FromBase64String(request.ImageBase64);
                var imageData = new ImageData { Bytes = imageBytes };

                await storage.SaveAsync(documentKey, imageData);

                var storagePath = $"{binFolderName}/{documentKey}";
                _logger.LogInformation("✅ Document saved: {DocumentKey} -> {Path} ({Size} bytes)",
                    documentKey, storagePath, imageBytes.Length);

                return DocumentSaveResult.CreateSuccess(documentKey, storagePath, binFolderName);
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Invalid base64 image data");
                return DocumentSaveResult.CreateError("Invalid image data format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving document");
                return DocumentSaveResult.CreateError($"Storage error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load document by folder and key
        /// </summary>
        public async Task<byte[]?> LoadDocumentAsync(string binFolderName, string documentKey)
        {
            try
            {
                var storage = _storageFactory.GetStorage<ImageData>(binFolderName);

                if (!await storage.ExistsAsync(documentKey))
                {
                    _logger.LogWarning("Document not found: {DocumentKey} in {Folder}", documentKey, binFolderName);
                    return null;
                }

                var imageData = await storage.LoadAsync(documentKey);
                return imageData.Bytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading document {DocumentKey}", documentKey);
                return null;
            }
        }

        /// <summary>
        /// Check if document exists
        /// </summary>
        public async Task<bool> DocumentExistsAsync(string binFolderName, string documentKey)
        {
            try
            {
                var storage = _storageFactory.GetStorage<ImageData>(binFolderName);
                return await storage.ExistsAsync(documentKey);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// List all documents in a folder
        /// </summary>
        public async Task<IEnumerable<string>> ListDocumentsAsync(string binFolderName)
        {
            try
            {
                var storage = _storageFactory.GetStorage<ImageData>(binFolderName);
                return await storage.ListIdentifiersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing documents in {Folder}", binFolderName);
                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// List bank slip documents, optionally filtered by pattern
        /// </summary>
        public async Task<IEnumerable<string>> ListBankSlipsAsync(string? patternFilter = null)
        {
            var allKeys = await ListDocumentsAsync(BANKSLIPS_FOLDER);

            if (string.IsNullOrEmpty(patternFilter))
                return allKeys;

            // Filter by pattern (first part before underscore)
            return allKeys.Where(k => k.StartsWith(patternFilter + "_", StringComparison.OrdinalIgnoreCase));
        }

        // ===== HELPER: Parse filename to get components =====

        /// <summary>
        /// Parse a bank slip filename to extract pattern, username, and timestamp
        /// Format: PATTERN_Username_dd_MM_yyyy_HH_mm_ss
        /// Example: KPLUS_Superfox75_17_11_2025_10_04_36 
        /// </summary>
        public static (string Pattern, string Username, DateTime? Timestamp) ParseBankSlipFilename(string filename)
        {
            // Remove extension if present
            var name = Path.GetFileNameWithoutExtension(filename);
            var parts = name.Split('_');

            if (parts.Length < 8)
                return (parts.Length > 0 ? parts[0] : "Unknown", "Unknown", null);

            var pattern = parts[0];
            var username = parts[1];

            // Try parse timestamp from remaining parts: dd_MM_yyyy_HH_mm_ss
            if (parts.Length >= 8 &&
                int.TryParse(parts[2], out int day) &&
                int.TryParse(parts[3], out int month) &&
                int.TryParse(parts[4], out int year) &&
                int.TryParse(parts[5], out int hour) &&
                int.TryParse(parts[6], out int minute) &&
                int.TryParse(parts[7], out int second))
            {
                try
                {
                    var timestamp = new DateTime(year, month, day, hour, minute, second);
                    return (pattern, username, timestamp);
                }
                catch { }
            }

            return (pattern, username, null);
        }

        // ===== VALIDATION =====

        private (bool IsValid, string? ErrorMessage) ValidateRequest(DocumentUploadRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SourceFolder))
                return (false, "SourceFolder is required");

            if (string.IsNullOrWhiteSpace(request.Username))
                return (false, "Username is required");

            if (string.IsNullOrWhiteSpace(request.ImageBase64))
                return (false, "ImageBase64 is required");

            if (string.IsNullOrWhiteSpace(request.FileName))
                return (false, "FileName is required");

            return (true, null);
        }

        /// <summary>
        /// Sanitize name for filesystem safety - preserves case
        /// </summary>
        private string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Unknown";

            // Remove invalid characters, keep letters, digits, underscore, dash
            var sanitized = new string(name
                .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
        }
    }
}