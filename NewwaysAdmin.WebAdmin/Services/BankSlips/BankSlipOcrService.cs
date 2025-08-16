// NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipOcrService.cs
// 🚀 COMPLETELY REWRITTEN: Modern implementation using DocumentParser
// NO MORE BankSlipData! Dictionary<string, string> results only!

using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class BankSlipOcrService
    {
        private readonly ILogger<BankSlipOcrService> _logger;
        private readonly IOManager _ioManager;
        private readonly IAuthenticationService _authService;
        private readonly BankSlipImageProcessor _imageProcessor;
        private readonly DocumentParser _documentParser;

        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<List<SlipCollection>>? _collectionsStorage;
        private IDataStorage<List<Dictionary<string, string>>>? _resultsStorage;
        private ImageAnnotatorClient? _visionClient;

        public BankSlipOcrService(
            ILogger<BankSlipOcrService> logger,
            IOManager ioManager,
            IAuthenticationService authService,
            BankSlipImageProcessor imageProcessor,
            DocumentParser documentParser)
        {
            _logger = logger;
            _ioManager = ioManager;
            _authService = authService;
            _imageProcessor = imageProcessor;
            _documentParser = documentParser;
        }

        #region Modern Processing Methods

        /// <summary>
        /// 🚀 MODERN: Process collection and return dictionary results
        /// </summary>
        public async Task<List<Dictionary<string, string>>> ProcessSlipCollectionAsync(
            string collectionId,
            DateTime startDate,
            DateTime endDate,
            string username,
            SimpleProgressReporter? progressReporter = null)
        {
            var collection = await GetCollectionAsync(collectionId, username);
            if (collection == null)
            {
                throw new ArgumentException($"Collection not found: {collectionId}");
            }

            _logger.LogInformation("🚀 MODERN: Starting dictionary-based processing for collection {CollectionName} from {StartDate} to {EndDate}",
                collection.Name, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            var results = new List<Dictionary<string, string>>();

            try
            {
                await EnsureStorageInitializedAsync();
                await SetupVisionClientAsync(collection);

                collection.MigrateToPatternBased();

                if (!Directory.Exists(collection.SourceDirectory))
                {
                    _logger.LogError("Source directory does not exist: {Directory}", collection.SourceDirectory);
                    throw new DirectoryNotFoundException($"Source directory not found: {collection.SourceDirectory}");
                }

                var imageFiles = GetImageFilesInDateRange(collection.SourceDirectory, startDate, endDate);
                var totalFiles = imageFiles.Count;

                _logger.LogInformation("Found {TotalFiles} image files to process", totalFiles);

                if (totalFiles == 0)
                {
                    _logger.LogInformation("No image files found in the specified date range");
                    return results;
                }

                for (int i = 0; i < imageFiles.Count; i++)
                {
                    var imagePath = imageFiles[i];
                    var fileName = Path.GetFileName(imagePath);

                    try
                    {
                        _logger.LogInformation("🔄 Processing file {CurrentFile}/{TotalFiles}: {FileName}",
                            i + 1, totalFiles, fileName);

                        progressReporter?.ReportProgress(i, totalFiles, fileName);

                        var result = await ProcessSingleFileModernAsync(imagePath, collection, username);

                        if (result != null)
                        {
                            results.Add(result);
                            _logger.LogInformation("✅ Successfully processed: {FileName}", fileName);
                        }
                        else
                        {
                            _logger.LogWarning("❌ Failed to process: {FileName}", fileName);

                            results.Add(new Dictionary<string, string>
                            {
                                ["FileName"] = fileName,
                                ["Error"] = "Processing failed",
                                ["ProcessedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "💥 Error processing file {FileName}: {Message}", fileName, ex.Message);

                        results.Add(new Dictionary<string, string>
                        {
                            ["FileName"] = fileName,
                            ["Error"] = ex.Message,
                            ["ProcessedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                }

                progressReporter?.ReportProgress(totalFiles, totalFiles, "Complete");

                await SaveProcessedResultsAsync(results, username);

                _logger.LogInformation("🎉 MODERN: Processing completed. {SuccessCount}/{TotalFiles} files successful",
                    results.Count(r => !r.ContainsKey("Error")), totalFiles);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 MODERN: Error during collection processing: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 🚀 MODERN: Process single file and return dictionary result
        /// </summary>
        public async Task<Dictionary<string, string>?> ProcessSingleFileAsync(
            string filePath,
            SlipCollection collection,
            string username)
        {
            return await ProcessSingleFileModernAsync(filePath, collection, username);
        }

        /// <summary>
        /// 🚀 MODERN: Test processing single file without saving
        /// </summary>
        public async Task<Dictionary<string, string>?> TestProcessSingleFileAsync(string filePath, SlipCollection collection)
        {
            try
            {
                _logger.LogDebug("🧪 Test processing single file: {FilePath}", Path.GetFileName(filePath));

                await EnsureStorageInitializedAsync();
                await SetupVisionClientAsync(collection);

                return await ProcessSingleFileModernAsync(filePath, collection, "test-user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error test processing file {FilePath}", filePath);
                return null;
            }
        }

        #endregion

        #region Collection Management

        public async Task<List<SlipCollection>> GetUserCollectionsAsync(string username)
        {
            await EnsureStorageInitializedAsync();

            var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
            return allCollections.Where(c => c.IsActive && (c.CreatedBy == username || username == "admin")).ToList();
        }

        public async Task<SlipCollection?> GetCollectionAsync(string collectionId, string username)
        {
            var collections = await GetUserCollectionsAsync(username);
            return collections.FirstOrDefault(c => c.Id == collectionId);
        }

        public async Task SaveCollectionAsync(SlipCollection collection, string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();

                var existingIndex = allCollections.FindIndex(c => c.Id == collection.Id);
                if (existingIndex >= 0)
                {
                    allCollections[existingIndex] = collection;
                    _logger.LogInformation("Updated existing collection {CollectionId} for user {Username}",
                        collection.Id, username);
                }
                else
                {
                    collection.CreatedBy = username;
                    collection.CreatedAt = DateTime.UtcNow;
                    allCollections.Add(collection);
                    _logger.LogInformation("Created new collection {CollectionId} for user {Username}",
                        collection.Id, username);
                }

                await _collectionsStorage.SaveAsync("admin", allCollections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving collection {CollectionId}", collection.Id);
                throw;
            }
        }

        public async Task DeleteCollectionAsync(string collectionId, string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
                var collection = allCollections.FirstOrDefault(c => c.Id == collectionId);

                if (collection != null)
                {
                    collection.IsActive = false;
                    await _collectionsStorage.SaveAsync("admin", allCollections);

                    _logger.LogInformation("Collection {CollectionId} marked as inactive by {Username}",
                        collectionId, username);
                }
                else
                {
                    _logger.LogWarning("Collection {CollectionId} not found for deletion by {Username}",
                        collectionId, username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting collection {CollectionId}", collectionId);
                throw;
            }
        }

        public async Task<List<SlipCollection>> GetAllCollectionsAsync()
        {
            await EnsureStorageInitializedAsync();

            var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
            return allCollections.Where(c => c.IsActive).ToList();
        }

        #endregion

        #region Core Processing Logic

        /// <summary>
        /// 🚀 MODERN: Process single file using DocumentParser
        /// </summary>
        private async Task<Dictionary<string, string>?> ProcessSingleFileModernAsync(
            string imagePath,
            SlipCollection collection,
            string username)
        {
            try
            {
                _logger.LogInformation("🚀 MODERN: ProcessSingleFileModernAsync START for: {FileName}", Path.GetFileName(imagePath));

                _logger.LogInformation("🖼️ Step 1: Processing image...");
                var processedImagePath = await _imageProcessor.ProcessImageAsync(imagePath, collection.ProcessingSettings);
                _logger.LogInformation("🖼️ Image processing completed. Processed path: {ProcessedPath}",
                    processedImagePath ?? "using original");

                _logger.LogInformation("🔤 Step 2: Starting OCR text extraction...");
                var ocrText = await ExtractTextAsync(processedImagePath ?? imagePath);

                if (string.IsNullOrEmpty(ocrText))
                {
                    _logger.LogWarning("🔤 OCR returned no text for {FileName}", Path.GetFileName(imagePath));
                    return null;
                }

                _logger.LogInformation("🔤 OCR completed. Extracted {CharCount} characters", ocrText.Length);
                _logger.LogDebug("🔤 OCR text preview: {TextPreview}...",
                    ocrText.Length > 200 ? ocrText.Substring(0, 200) : ocrText);

                _logger.LogInformation("🧠 Step 3: Starting modern document parsing...");

                collection.MigrateToPatternBased();

                var result = await _documentParser.ParseAsync(
                    ocrText,
                    imagePath,
                    collection.DocumentType,
                    collection.FormatName);

                if (result == null || result.Count == 0)
                {
                    _logger.LogWarning("🧠 DocumentParser returned empty result for {FileName}", Path.GetFileName(imagePath));
                    return null;
                }

                _logger.LogInformation("🧠 Modern parsing completed successfully. Extracted {FieldCount} fields", result.Count);

                result["ProcessedBy"] = username;
                result["ProcessedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                result["CollectionName"] = collection.Name;
                result["DocumentType"] = collection.DocumentType;
                result["FormatName"] = collection.FormatName;

                _logger.LogInformation("🎉 MODERN: ProcessSingleFileModernAsync SUCCESS for: {FileName}", Path.GetFileName(imagePath));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 MODERN: ProcessSingleFileModernAsync FAILED for {FileName}: {Message}",
                    Path.GetFileName(imagePath), ex.Message);
                return null;
            }
        }

        #endregion

        #region Helper Methods

        private async Task EnsureStorageInitializedAsync()
        {
            if (_collectionsStorage != null && _resultsStorage != null)
                return;

            await _initLock.WaitAsync();
            try
            {
                _collectionsStorage ??= await _ioManager.GetStorageAsync<List<SlipCollection>>("BankSlip_Collections");
                _resultsStorage ??= await _ioManager.GetStorageAsync<List<Dictionary<string, string>>>("BankSlip_Results");
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task SetupVisionClientAsync(SlipCollection collection)
        {
            if (_visionClient != null)
                return;

            try
            {
                _logger.LogDebug("Setting up Google Vision client with credentials: {CredentialsPath}",
                    collection.CredentialsPath);

                if (!File.Exists(collection.CredentialsPath))
                {
                    throw new FileNotFoundException($"Google Vision credentials not found: {collection.CredentialsPath}");
                }

                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", collection.CredentialsPath);
                _visionClient = ImageAnnotatorClient.Create();

                _logger.LogInformation("Google Vision client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Google Vision client");
                throw;
            }
        }

        private async Task<string?> ExtractTextAsync(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    _logger.LogError("Image file not found: {ImagePath}", imagePath);
                    return null;
                }

                _logger.LogInformation("🔤 Creating image from file...");
                var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);

                _logger.LogInformation("🔤 Calling Google Vision API...");
                var response = await _visionClient!.DetectTextAsync(image);

                if (!response.Any())
                {
                    _logger.LogDebug("🔤 OCR returned no text for {ImagePath}", Path.GetFileName(imagePath));
                    return null;
                }

                var text = string.Join("\n", response.Select(r => r.Description));
                _logger.LogInformation("🔤 OCR extracted {CharCount} characters from {ImagePath}",
                    text.Length, Path.GetFileName(imagePath));

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error during OCR extraction for {ImagePath}: {Message}",
                    Path.GetFileName(imagePath), ex.Message);
                return null;
            }
        }

        private List<string> GetImageFilesInDateRange(string directory, DateTime startDate, DateTime endDate)
        {
            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };

            var allFiles = Directory.GetFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();

            if (startDate != default && endDate != default)
            {
                allFiles = allFiles.Where(file =>
                {
                    var fileInfo = new FileInfo(file);
                    var fileDate = fileInfo.LastWriteTime.Date;
                    return fileDate >= startDate.Date && fileDate <= endDate.Date;
                }).ToList();
            }

            return allFiles.OrderBy(file => File.GetCreationTime(file)).ToList();
        }

        private async Task SaveProcessedResultsAsync(List<Dictionary<string, string>> results, string username)
        {
            try
            {
                List<Dictionary<string, string>> existingResults;
                try
                {
                    existingResults = await _resultsStorage!.LoadAsync(username) ?? new List<Dictionary<string, string>>();
                }
                catch (FileNotFoundException)
                {
                    existingResults = new List<Dictionary<string, string>>();
                }

                existingResults.AddRange(results);

                await _resultsStorage!.SaveAsync(username, existingResults);

                _logger.LogInformation("💾 Saved {ResultCount} processing results for user {Username}",
                    results.Count, username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error saving processing results for user {Username}", username);
            }
        }

        #endregion
    }

    /// <summary>
    /// Simple progress reporter for UI updates
    /// </summary>
    public class SimpleProgressReporter
    {
        private readonly Action<int, int, string> _onProgress;

        public SimpleProgressReporter(Action<int, int, string> onProgress)
        {
            _onProgress = onProgress;
        }

        public void ReportProgress(int processedCount, int totalCount, string currentFileName = "")
        {
            _onProgress(processedCount, totalCount, currentFileName);
        }
    }
}