// NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipOcrService.cs
// Fixed implementation that matches the existing interface
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers;


namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class BankSlipOcrService : IBankSlipOcrService
    {
        private readonly ILogger<BankSlipOcrService> _logger;
        private readonly IOManager _ioManager;
        private readonly IAuthenticationService _authService;
        private readonly BankSlipParserFactory _parserFactory;
        private readonly BankSlipImageProcessor _imageProcessor;
        private readonly BankSlipValidator _validator;

        private readonly SemaphoreSlim _initLock = new(1, 1);
        private IDataStorage<List<SlipCollection>>? _collectionsStorage;
        private IDataStorage<List<BankSlipData>>? _slipsStorage;
        private ImageAnnotatorClient? _visionClient;

        public BankSlipOcrService(
            ILogger<BankSlipOcrService> logger,
            IOManager ioManager,
            IAuthenticationService authService,
            BankSlipParserFactory parserFactory,
            BankSlipImageProcessor imageProcessor,
            BankSlipValidator validator)
        {
            _logger = logger;
            _ioManager = ioManager;
            _authService = authService;
            _parserFactory = parserFactory;
            _imageProcessor = imageProcessor;
            _validator = validator;
        }

        #region Interface Implementation - Existing Methods

        public async Task<BankSlipProcessingResult> ProcessSlipCollectionAsync(
    string collectionId,
    DateTime startDate,
    DateTime endDate,
    string username,
    IProgressReporter? progressReporter = null)
        {
            var collection = await GetCollectionAsync(collectionId, username);
            if (collection == null)
            {
                throw new ArgumentException($"Collection not found: {collectionId}");
            }

            _logger.LogInformation("Starting enhanced bank slip processing for collection {CollectionName} from {StartDate} to {EndDate}",
                collection.Name, startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"));

            var result = new BankSlipProcessingResult
            {
                ProcessingStarted = DateTime.UtcNow
            };

            try
            {
                await EnsureStorageInitializedAsync();

                // Validate directories
                if (!Directory.Exists(collection.SourceDirectory))
                {
                    result.Errors.Add(new ProcessingError
                    {
                        FilePath = collection.SourceDirectory,
                        Reason = "Source directory does not exist",
                        ErrorTime = DateTime.UtcNow
                    });
                    return result;
                }

                // Ensure output directory exists
                Directory.CreateDirectory(collection.OutputDirectory);

                // Initialize Vision API client
                _logger.LogInformation("🔧 Setting up Vision API client...");
                await SetupVisionClientAsync(collection);
                _logger.LogInformation("✅ Vision API client setup completed");

                // Get image files filtered by date range
                _logger.LogInformation("📁 Getting image files in date range...");
                var imageFiles = GetImageFilesInDateRange(collection.SourceDirectory, startDate, endDate);
                result.Summary.TotalFiles = imageFiles.Count;
                _logger.LogInformation("📁 Found {Count} files to process", imageFiles.Count);

                _logger.LogInformation("Processing {Count} files from collection {CollectionName}",
                    imageFiles.Count, collection.Name);

                // Process each file with appropriate parser
                var processedSlips = new List<BankSlipData>();
                int processedCount = 0;

                _logger.LogInformation("🚀 Starting to process {Count} files...", imageFiles.Count);

                foreach (var imagePath in imageFiles)
                {
                    try
                    {
                        _logger.LogInformation("📄 Processing file {Index}/{Total}: {FileName}",
                            processedCount + 1, imageFiles.Count, Path.GetFileName(imagePath));

                        progressReporter?.ReportProgress(processedCount, imageFiles.Count, Path.GetFileName(imagePath));

                        _logger.LogInformation("🔄 Calling ProcessSingleFileAsync for {FileName}...", Path.GetFileName(imagePath));

                        var slipData = await ProcessSingleFileAsync(imagePath, collection, username);

                        _logger.LogInformation("✅ ProcessSingleFileAsync completed for {FileName}. Result: {HasData}",
                            Path.GetFileName(imagePath), slipData != null);

                        if (slipData != null)
                        {
                            processedSlips.Add(slipData);
                            result.Summary.ProcessedFiles++;
                            _logger.LogInformation("✅ Successfully processed {FileName} - Total successful: {Count}",
                                Path.GetFileName(imagePath), processedSlips.Count);
                        }
                        else
                        {
                            result.Summary.FailedFiles++;
                            result.Errors.Add(new ProcessingError
                            {
                                FilePath = imagePath,
                                Reason = "Failed to extract slip data",
                                ErrorTime = DateTime.UtcNow
                            });
                            _logger.LogWarning("❌ Failed to process {FileName} - no slip data returned", Path.GetFileName(imagePath));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "💥 Exception processing file {FilePath}: {Message}", imagePath, ex.Message);
                        result.Summary.FailedFiles++;
                        result.Errors.Add(new ProcessingError
                        {
                            FilePath = imagePath,
                            Reason = ex.Message,
                            ErrorTime = DateTime.UtcNow
                        });
                    }
                    finally
                    {
                        processedCount++;
                        _logger.LogInformation("📊 Progress: {Processed}/{Total} files completed", processedCount, imageFiles.Count);
                    }
                }

                _logger.LogInformation("🏁 Processing loop completed. Processed: {Processed}, Failed: {Failed}",
                    processedSlips.Count, result.Summary.FailedFiles);

                // Save processed slips
                if (processedSlips.Any())
                {
                    _logger.LogInformation("💾 Saving {Count} processed slips...", processedSlips.Count);
                    await SaveProcessedSlipsAsync(processedSlips, username);
                    _logger.LogInformation("✅ Slips saved successfully");
                }
                else
                {
                    _logger.LogWarning("⚠️ No slips to save - all processing failed");
                }

                result.ProcessedSlips = processedSlips;
                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;

                _logger.LogInformation("Completed processing collection {CollectionName}: {Processed}/{Total} files successful",
                    collection.Name, result.Summary.ProcessedFiles, result.Summary.TotalFiles);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 FATAL ERROR processing collection {CollectionName}: {Message}", collection.Name, ex.Message);
                result.Errors.Add(new ProcessingError
                {
                    FilePath = collection.SourceDirectory,
                    Reason = ex.Message,
                    ErrorTime = DateTime.UtcNow
                });

                result.ProcessingCompleted = DateTime.UtcNow;
                result.Summary.ProcessingDuration = result.ProcessingCompleted - result.ProcessingStarted;

                return result;
            }
        }

        public async Task<List<SlipCollection>> GetUserCollectionsAsync(string username)
        {
            await EnsureStorageInitializedAsync();

            var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();

            var userCollections = allCollections.Where(c => c.IsActive).ToList();

            _logger.LogInformation("Retrieved {Count} active collections for user {Username}",
                userCollections.Count, username);

            return userCollections;
        }

        public async Task<SlipCollection?> GetCollectionAsync(string collectionId, string username)
        {
            await EnsureStorageInitializedAsync();

            var allCollections = await _collectionsStorage!.LoadAsync("admin") ?? new List<SlipCollection>();
            var collection = allCollections.FirstOrDefault(c => c.Id == collectionId && c.IsActive);

            if (collection != null)
            {
                _logger.LogDebug("Retrieved collection {CollectionName} for user {Username}",
                    collection.Name, username);
            }
            else
            {
                _logger.LogWarning("Collection {CollectionId} not found or inactive for user {Username}",
                    collectionId, username);
            }

            return collection;
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
                    // Update existing collection
                    allCollections[existingIndex] = collection;
                    _logger.LogInformation("Updated collection {CollectionName} by {Username}",
                        collection.Name, username);
                }
                else
                {
                    // Create new collection
                    if (string.IsNullOrEmpty(collection.Id))
                    {
                        collection.Id = Guid.NewGuid().ToString();
                    }

                    collection.CreatedBy = username;
                    collection.CreatedAt = DateTime.UtcNow;
                    allCollections.Add(collection);

                    _logger.LogInformation("Created new collection {CollectionName} by {Username}",
                        collection.Name, username);
                }

                await _collectionsStorage.SaveAsync("admin", allCollections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving collection {CollectionName}", collection.Name);
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
                    // Mark as inactive instead of deleting
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

        public async Task<BankSlipData?> TestProcessSingleFileAsync(string filePath, SlipCollection collection)
        {
            try
            {
                _logger.LogDebug("Test processing single file: {FilePath}", Path.GetFileName(filePath));

                await EnsureStorageInitializedAsync();
                await SetupVisionClientAsync(collection);

                return await ProcessSingleFileAsync(filePath, collection, "test-user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error test processing file {FilePath}", filePath);
                return null;
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

        private async Task<BankSlipData?> ProcessSingleFileAsync(string imagePath, SlipCollection collection, string username)
        {
            try
            {
                _logger.LogInformation("🔍 ProcessSingleFileAsync START for: {FileName}", Path.GetFileName(imagePath));

                // Step 1: Process image if needed
                _logger.LogInformation("🖼️ Step 1: Processing image...");
                var processedImagePath = await _imageProcessor.ProcessImageAsync(imagePath, collection.ProcessingSettings);
                _logger.LogInformation("🖼️ Image processing completed. Processed path: {ProcessedPath}",
                    processedImagePath ?? "using original");

                // Step 2: Extract text via OCR
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

                // Step 3: Parse using appropriate parser
                _logger.LogInformation("🧠 Step 3: Getting parser for collection...");
                var parser = _parserFactory.GetParser(collection);
                _logger.LogInformation("🧠 Parser obtained: {ParserType}", parser.GetType().Name);

                _logger.LogInformation("🧠 Starting parsing...");
                var bankSlipData = parser.Parse(ocrText, imagePath, collection);

                if (bankSlipData == null)
                {
                    _logger.LogWarning("🧠 Parser returned null for {FileName}", Path.GetFileName(imagePath));
                    return null;
                }

                _logger.LogInformation("🧠 Parsing completed successfully");

                // Step 4: Validate the data
                _logger.LogInformation("✅ Step 4: Validating parsed data...");
                if (!_validator.ValidateSlipData(bankSlipData, collection))
                {
                    _logger.LogWarning("✅ Validation failed for {FileName}", Path.GetFileName(imagePath));
                    return null;
                }

                _logger.LogInformation("✅ Validation completed successfully");

                // Set processing metadata
                bankSlipData.ProcessedBy = username;
                bankSlipData.ProcessedAt = DateTime.UtcNow;
                bankSlipData.SlipCollectionName = collection.Name;

                _logger.LogInformation("🎉 ProcessSingleFileAsync SUCCESS for: {FileName}", Path.GetFileName(imagePath));
                return bankSlipData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 ProcessSingleFileAsync FAILED for {FileName}: {Message}",
                    Path.GetFileName(imagePath), ex.Message);
                return null;
            }
        }

        #endregion

        #region Helper Methods

        private async Task EnsureStorageInitializedAsync()
        {
            if (_collectionsStorage != null && _slipsStorage != null)
                return;

            await _initLock.WaitAsync();
            try
            {
                _collectionsStorage ??= await _ioManager.GetStorageAsync<List<SlipCollection>>("BankSlip_Collections");
                _slipsStorage ??= await _ioManager.GetStorageAsync<List<BankSlipData>>("BankSlip_Data");

                _logger.LogInformation("Storage initialized successfully");
            }
            finally
            {
                _initLock.Release();
            }
        }

        private async Task SetupVisionClientAsync(SlipCollection collection)
        {
            try
            {
                if (File.Exists(collection.CredentialsPath))
                {
                    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", collection.CredentialsPath);
                    _visionClient = ImageAnnotatorClient.Create();
                    _logger.LogDebug("Vision client configured with collection credentials: {CredentialsPath}",
                        collection.CredentialsPath);
                }
                else
                {
                    _logger.LogWarning("Collection credentials file not found: {CredentialsPath}, using default",
                        collection.CredentialsPath);
                    // Try default path
                    var defaultPath = @"C:\Keys\purrfectocr-db2d9d796b58.json";
                    if (File.Exists(defaultPath))
                    {
                        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", defaultPath);
                        _visionClient = ImageAnnotatorClient.Create();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up Vision client for collection {CollectionName}", collection.Name);
                throw;
            }
        }

        private async Task<string?> ExtractTextAsync(string imagePath)
        {
            try
            {
                _logger.LogInformation("🔤 ExtractTextAsync START for: {FileName}", Path.GetFileName(imagePath));

                if (_visionClient == null)
                {
                    _logger.LogError("🔤 Vision client is null!");
                    return null;
                }

                _logger.LogInformation("🔤 Creating image from file...");
                var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);

                _logger.LogInformation("🔤 Calling Google Vision API...");
                var response = await _visionClient.DetectTextAsync(image);

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

            // Filter by file creation/modification date if dates are specified
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

        private async Task SaveProcessedSlipsAsync(List<BankSlipData> slips, string username)
        {
            try
            {
                // FIXED: Handle first-time users gracefully
                List<BankSlipData> existingSlips;
                try
                {
                    existingSlips = await _slipsStorage!.LoadAsync(username) ?? new List<BankSlipData>();
                    _logger.LogDebug("Loaded {Count} existing slips for user {Username}", existingSlips.Count, username);
                }
                catch (Exception ex) when (ex.Message.Contains("Data not found") || ex is NewwaysAdmin.Shared.IO.StorageException)
                {
                    // First-time user - no existing data, start with empty list
                    existingSlips = new List<BankSlipData>();
                    _logger.LogInformation("First-time processing for user {Username}, starting with empty slip list", username);
                }

                // Add new slips (avoid duplicates based on file path)
                var newSlips = slips.Where(newSlip =>
                    !existingSlips.Any(existing => existing.OriginalFilePath == newSlip.OriginalFilePath))
                    .ToList();

                if (newSlips.Any())
                {
                    existingSlips.AddRange(newSlips);
                    await _slipsStorage.SaveAsync(username, existingSlips);

                    _logger.LogInformation("✅ Saved {Count} new processed slips for user {Username} (total: {Total})",
                        newSlips.Count, username, existingSlips.Count);
                }
                else
                {
                    _logger.LogInformation("No new slips to save for user {Username} - all {Count} slips already exist",
                        username, slips.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving processed slips for user {Username}", username);
                // IMPORTANT: Don't throw - let processing continue even if save fails
                _logger.LogWarning("Processing will continue but slips won't be persisted to storage");
            }
        }

        #endregion

        #region Additional Helper Methods (for compatibility)

        public async Task<List<BankSlipData>> GetProcessedSlipsAsync(string username)
        {
            try
            {
                await EnsureStorageInitializedAsync();
                return await _slipsStorage!.LoadAsync(username) ?? new List<BankSlipData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading processed slips for user {Username}", username);
                return new List<BankSlipData>();
            }
        }

        #endregion
    }
}