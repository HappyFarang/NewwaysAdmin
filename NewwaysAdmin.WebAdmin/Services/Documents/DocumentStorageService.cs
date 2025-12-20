// File: NewwaysAdmin.WebAdmin/Services/Documents/DocumentStorageService.cs
// Handles storage of uploaded documents using IO Manager
// 3 registered folders, fully dynamic users
//
// Storage structure:
// BankSlipJson/                    ← Registered (Json) - config
// └── source-types.json
//
// BankSlipBill/                    ← Registered (Binary) - shared bills
// ├── Amy_Bills_07_12_2025_18_00.bin
// └── Thomas_Bills_08_12_2025_12_30.bin
//
// BankSlipsBin/                    ← Registered (Binary) - all bank slips
// ├── KBIZ_Amy/                    ← Dynamic subfolder via key
// │   └── Amy_KBIZ_07_12_2025_16_45.bin
// ├── KPlus_Amy/
// │   └── Amy_KPlus_08_12_2025_09_15.bin
// ├── KBIZ_Thomas/
// │   └── Thomas_KBIZ_07_12_2025_16_45.bin
// └── KPlus_Thomas/
//     └── Thomas_KPlus_08_12_2025_09_15.bin

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

        // Configuration storage
        private IDataStorage<SourceTypeConfig>? _configStorage;
        private SourceTypeConfig _config = new();

        private readonly SemaphoreSlim _lock = new(1, 1);

        // Folder names
        private const string CONFIG_FOLDER = "BankSlipJson";
        private const string BILLS_FOLDER = "BankSlipBill";
        private const string BANKSLIPS_FOLDER = "BankSlipsBin";
        private const string CONFIG_KEY = "source-types";

        public DocumentStorageService(
            ILogger<DocumentStorageService> logger,
            EnhancedStorageFactory storageFactory)
        {
            _logger = logger;
            _storageFactory = storageFactory;

            // Initialize storage
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                // Folders are registered in StorageFolderDefinitions.cs
                // Just get the config storage and load config
                _configStorage = _storageFactory.GetStorage<SourceTypeConfig>(CONFIG_FOLDER);

                await LoadOrCreateConfigAsync();

                _logger.LogInformation("DocumentStorageService initialized with {TypeCount} source types",
                    _config.SourceTypes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize DocumentStorageService");
            }
        }

        private async Task LoadOrCreateConfigAsync()
        {
            try
            {
                if (await _configStorage!.ExistsAsync(CONFIG_KEY))
                {
                    _config = await _configStorage.LoadAsync(CONFIG_KEY);
                    _logger.LogInformation("Loaded {Count} source types from config", _config.SourceTypes.Count);
                }
                else
                {
                    _config = CreateDefaultConfig();
                    await _configStorage.SaveAsync(CONFIG_KEY, _config);
                    _logger.LogInformation("Created default source type config");
                }
            }
            catch (StorageException)
            {
                _config = CreateDefaultConfig();
                await _configStorage!.SaveAsync(CONFIG_KEY, _config);
                _logger.LogInformation("Created default source type config");
            }
        }

        private SourceTypeConfig CreateDefaultConfig()
        {
            return new SourceTypeConfig
            {
                SourceTypes = new List<SourceTypeInfo>
                {
                    new("kbiz", "KBIZ", "BankSlips", "KBIZ"),
                    new("kplus", "KPlus", "BankSlips", "KPlus"),
                    new("bangkokbank", "BangkokBank", "BankSlips", "BangkokBank"),
                    new("scb", "SCB", "BankSlips", "SCB"),
                    new("bills", "Bills", "Bills", "Receipt")
                }
            };
        }

        // ===== DOCUMENT STORAGE =====

        /// <summary>
        /// Save an uploaded document
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

                // Get source type
                var sourceType = GetSourceType(request.SourceFolder);
                if (sourceType == null)
                {
                    _logger.LogWarning("Unknown source folder: {SourceFolder}", request.SourceFolder);
                    return DocumentSaveResult.CreateError($"Unknown source folder: {request.SourceFolder}");
                }

                var username = SanitizeName(request.Username);
                var displayUsername = char.ToUpper(username[0]) + username[1..];
                var timestamp = DateTime.UtcNow;

                string binFolderName;
                string documentKey;

                // Bills go to shared folder, bank slips go to BankSlipsBin with dynamic subfolders
                if (sourceType.Key.Equals("bills", StringComparison.OrdinalIgnoreCase))
                {
                    // Bills: shared folder, flat structure
                    binFolderName = BILLS_FOLDER;
                    documentKey = $"{displayUsername}_Bills_{timestamp:dd_MM_yyyy_HH_mm}";
                }
                else
                {
                    // Bank slips: dynamic subfolder {Bank}_{User}/filename
                    binFolderName = BANKSLIPS_FOLDER;
                    var subFolder = $"{sourceType.DisplayName}_{displayUsername}";
                    var fileName = $"{displayUsername}_{sourceType.DisplayName}_{timestamp:dd_MM_yyyy_HH_mm}";
                    documentKey = $"{subFolder}/{fileName}";
                }

                // Get storage and check for duplicates
                var storage = _storageFactory.GetStorage<ImageData>(binFolderName);
                if (await storage.ExistsAsync(documentKey))
                {
                    // Add seconds for uniqueness
                    if (sourceType.Key.Equals("bills", StringComparison.OrdinalIgnoreCase))
                    {
                        documentKey = $"{displayUsername}_Bills_{timestamp:dd_MM_yyyy_HH_mm_ss}";
                    }
                    else
                    {
                        var subFolder = $"{sourceType.DisplayName}_{displayUsername}";
                        var fileName = $"{displayUsername}_{sourceType.DisplayName}_{timestamp:dd_MM_yyyy_HH_mm_ss}";
                        documentKey = $"{subFolder}/{fileName}";
                    }
                }

                // Decode image bytes
                byte[] imageBytes;
                try
                {
                    imageBytes = Convert.FromBase64String(request.ImageBase64);
                }
                catch (FormatException)
                {
                    return DocumentSaveResult.CreateError("Invalid base64 image data");
                }

                // Save wrapped in ImageData (IO Manager requires class with parameterless constructor)
                await storage.SaveAsync(documentKey, new ImageData(imageBytes));

                var storagePath = $"{binFolderName}/{documentKey}.bin";

                _logger.LogInformation(
                    "Document saved: {DocumentKey} for {Username} via {SourceFolder} ({Size} bytes)",
                    documentKey, username, request.SourceFolder, imageBytes.Length);

                return DocumentSaveResult.CreateSuccess(documentKey, storagePath, binFolderName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving document from {Username} via {SourceFolder}",
                    request.Username, request.SourceFolder);
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
                _logger.LogError(ex, "Error loading document {DocumentKey} from {Folder}", documentKey, binFolderName);
                return null;
            }
        }

        /// <summary>
        /// Load bank slip by source type, username, and document ID
        /// </summary>
        public async Task<byte[]?> LoadBankSlipAsync(string sourceType, string username, string documentId)
        {
            var displayUsername = char.ToUpper(username[0]) + username[1..];
            var documentKey = $"{sourceType}_{displayUsername}/{documentId}";
            return await LoadDocumentAsync(BANKSLIPS_FOLDER, documentKey);
        }

        /// <summary>
        /// Load bill by document ID
        /// </summary>
        public async Task<byte[]?> LoadBillAsync(string documentId)
        {
            return await LoadDocumentAsync(BILLS_FOLDER, documentId);
        }

        // ===== SOURCE TYPE MANAGEMENT =====

        /// <summary>
        /// Get a source type by key
        /// </summary>
        public SourceTypeInfo? GetSourceType(string key)
        {
            return _config.SourceTypes.FirstOrDefault(s =>
                s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get all source types
        /// </summary>
        public IReadOnlyList<SourceTypeInfo> GetAllSourceTypes()
        {
            return _config.SourceTypes.AsReadOnly();
        }

        /// <summary>
        /// Get active source types for mobile app
        /// </summary>
        public IEnumerable<DocumentSourceMapping> GetSourceMappingsForMobile()
        {
            return _config.SourceTypes
                .Where(s => s.IsActive)
                .Select(s => new DocumentSourceMapping
                {
                    FolderName = s.Key,
                    DisplayName = s.DisplayName,
                    StorageFolderName = "", // Dynamic per user
                    OcrDocumentType = s.OcrDocumentType,
                    OcrFormatName = s.OcrFormatName,
                    IsActive = s.IsActive
                });
        }

        /// <summary>
        /// Add a new source type (bank)
        /// </summary>
        public async Task<bool> AddSourceTypeAsync(string key, string displayName, string ocrDocType, string ocrFormat)
        {
            await _lock.WaitAsync();
            try
            {
                var normalizedKey = key.ToLowerInvariant();

                if (_config.SourceTypes.Any(s => s.Key.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Source type already exists: {Key}", key);
                    return false;
                }

                _config.SourceTypes.Add(new SourceTypeInfo(normalizedKey, displayName, ocrDocType, ocrFormat));
                await SaveConfigAsync();

                _logger.LogInformation("Added source type: {Key} -> {DisplayName}", key, displayName);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Update an existing source type
        /// </summary>
        public async Task<bool> UpdateSourceTypeAsync(string key, string? displayName = null,
            string? ocrDocType = null, string? ocrFormat = null, bool? isActive = null)
        {
            await _lock.WaitAsync();
            try
            {
                var sourceType = _config.SourceTypes.FirstOrDefault(s =>
                    s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (sourceType == null)
                {
                    _logger.LogWarning("Source type not found: {Key}", key);
                    return false;
                }

                if (displayName != null) sourceType.DisplayName = displayName;
                if (ocrDocType != null) sourceType.OcrDocumentType = ocrDocType;
                if (ocrFormat != null) sourceType.OcrFormatName = ocrFormat;
                if (isActive.HasValue) sourceType.IsActive = isActive.Value;

                await SaveConfigAsync();

                _logger.LogInformation("Updated source type: {Key}", key);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Remove a source type (keeps files)
        /// </summary>
        public async Task<bool> RemoveSourceTypeAsync(string key)
        {
            await _lock.WaitAsync();
            try
            {
                var removed = _config.SourceTypes.RemoveAll(s =>
                    s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

                if (removed == 0)
                {
                    _logger.LogWarning("Source type not found: {Key}", key);
                    return false;
                }

                await SaveConfigAsync();
                _logger.LogInformation("Removed source type: {Key}", key);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }

        // ===== HELPERS =====

        private async Task SaveConfigAsync()
        {
            if (_configStorage != null)
            {
                _config.LastModified = DateTime.UtcNow;
                await _configStorage.SaveAsync(CONFIG_KEY, _config);
            }
        }

        private (bool IsValid, string? ErrorMessage) ValidateRequest(DocumentUploadRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.SourceFolder))
                return (false, "Source folder is required");

            if (string.IsNullOrWhiteSpace(request.FileName))
                return (false, "File name is required");

            if (string.IsNullOrWhiteSpace(request.ImageBase64))
                return (false, "Image data is required");

            if (string.IsNullOrWhiteSpace(request.Username))
                return (false, "Username is required");

            var estimatedSize = request.ImageBase64.Length * 3 / 4;
            if (estimatedSize > 10 * 1024 * 1024)
                return (false, "File too large (max 10MB)");

            return (true, null);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "unknown";

            var sanitized = string.Join("_", name.Split(Path.GetInvalidFileNameChars(),
                StringSplitOptions.RemoveEmptyEntries));

            while (sanitized.Contains("__"))
                sanitized = sanitized.Replace("__", "_");

            sanitized = sanitized.Trim('_');

            if (sanitized.Length > 30)
                sanitized = sanitized[..30];

            return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
        }
    }
}