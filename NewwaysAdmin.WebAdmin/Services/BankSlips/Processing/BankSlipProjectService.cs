// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/BankSlipProjectService.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SharedModels.Services.Ocr;
using NewwaysAdmin.WebAdmin.Services.Documents;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Orchestrates bank slip processing: OCR → memo parsing → project creation → storage.
/// Uses hybrid approach:
/// - BankSlipsBin: Direct file access (raw binary files)  
/// - BankSlipJson: Storage system (JSON objects)
/// </summary>
public class BankSlipProjectService
{
    private readonly ILogger<BankSlipProjectService> _logger;
    private readonly DocumentParser _documentParser;
    private readonly BankSlipFilenameParser _filenameParser;
    private readonly MemoParserService _memoParser;
    private readonly EnhancedStorageFactory _storageFactory;
    private readonly PatternManagementService _patternManagement;

    // Storage folder names
    private const string BIN_FOLDER = "BankSlipsBin";
    private const string PROJECTS_FOLDER = "BankSlipJson";
    private const string PROJECTS_SUBFOLDER = "Projects";
    private const string INDEX_FILENAME = "ProjectIndex";
    private const string REVIEW_QUEUE_FILENAME = "ReviewQueue";

    public BankSlipProjectService(
        ILogger<BankSlipProjectService> logger,
        DocumentParser documentParser,
        BankSlipFilenameParser filenameParser,
        MemoParserService memoParser,
        EnhancedStorageFactory storageFactory,
        PatternManagementService patternManagement)  // <-- ADD THIS
    {
        _logger = logger;
        _documentParser = documentParser;
        _filenameParser = filenameParser;
        _memoParser = memoParser;
        _storageFactory = storageFactory;
        _patternManagement = patternManagement;  // <-- ADD THIS
    }

    #region Main Processing

    /// <summary>
    /// Process a single bank slip file and create a project
    /// </summary>
    /// <param name="binFilePath">Full path to the .bin file in BankSlipsBin</param>
    /// <returns>Created project or null if processing failed</returns>
    public async Task<BankSlipProject?> ProcessBankSlipAsync(string binFilePath)
    {
        var filename = Path.GetFileName(binFilePath);
        _logger.LogInformation("🔄 Processing bank slip: {Filename}", filename);

        try
        {
            // Step 1: Parse filename
            var filenameInfo = _filenameParser.Parse(filename);
            if (filenameInfo == null || !filenameInfo.IsValid)
            {
                _logger.LogWarning("❌ Failed to parse filename: {Filename}", filename);
                return null;
            }

            var projectId = filenameInfo.GetProjectId();

            // Step 2: Check if already processed
            if (await ProjectExistsAsync(projectId))
            {
                _logger.LogDebug("⏭️ Project already exists: {ProjectId}", projectId);
                return null;
            }

            // Step 3: Run OCR (DocumentParser uses the file path directly)
            var extractedFields = await RunOcrWithFallbackAsync(binFilePath, filenameInfo.PatternSetName);

            // Step 4: Create project (even if OCR failed partially)
            var project = CreateProject(filenameInfo, extractedFields);

            // Step 5: Parse memo/note field
            ParseMemoField(project);

            // Step 6: Determine review reason
            var reviewReason = DetermineReviewReason(project);

            // Step 7: Save project
            await SaveProjectAsync(project);

            // Step 8: Update index
            await UpdateIndexAsync(project);

            // Step 9: Add to review queue
            await AddToReviewQueueAsync(project.ProjectId, reviewReason);

            _logger.LogInformation("✅ Successfully processed: {ProjectId} (Review: {Reason})",
                projectId, reviewReason);

            return project;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing bank slip: {Filename}", filename);
            return null;
        }
    }

    /// <summary>
    /// Process multiple bank slip files (batch mode)
    /// </summary>
    public async Task<BatchProcessResult> ProcessBatchAsync(IEnumerable<string> binFilePaths)
    {
        var result = new BatchProcessResult();

        foreach (var filePath in binFilePaths)
        {
            try
            {
                var project = await ProcessBankSlipAsync(filePath);
                if (project != null)
                {
                    result.Succeeded++;
                    result.ProcessedProjectIds.Add(project.ProjectId);
                }
                else
                {
                    result.Skipped++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch processing: {FilePath}", filePath);
                result.Failed++;
                result.FailedFiles.Add(filePath);
            }
        }

        _logger.LogInformation("📊 Batch complete: {Succeeded} succeeded, {Skipped} skipped, {Failed} failed",
            result.Succeeded, result.Skipped, result.Failed);

        return result;
    }

    #endregion

    #region OCR Processing

    private async Task<Dictionary<string, string>> RunOcrAsync(string binFilePath, string patternSetName)
    {
        try
        {
            _logger.LogDebug("🔍 Running OCR with pattern set: {PatternSet}", patternSetName);

            // Step 1: Load image bytes using IOManager (same as DocumentStorageService)
            byte[] imageBytes;
            try
            {
                var identifier = Path.GetFileNameWithoutExtension(binFilePath);
                var storage = _storageFactory.GetStorage<ImageData>(BIN_FOLDER);
                var imageData = await storage.LoadAsync(identifier);

                if (imageData?.Bytes == null || imageData.Bytes.Length == 0)
                {
                    _logger.LogError("❌ ImageData is null or empty for: {Identifier}", identifier);
                    return CreateMissingPatternResult(binFilePath);
                }

                imageBytes = imageData.Bytes;
                _logger.LogDebug("📦 Loaded {Bytes} bytes via IOManager", imageBytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to load image via IOManager: {Path}", binFilePath);
                return CreateMissingPatternResult(binFilePath);
            }

            // Step 2: Detect image format and create temp file
            var extension = DetectImageFormat(imageBytes);
            var tempImagePath = Path.Combine(Path.GetTempPath(), $"bankslip_{Guid.NewGuid()}{extension}");

            try
            {
                await File.WriteAllBytesAsync(tempImagePath, imageBytes);
                _logger.LogDebug("📝 Created temp image file: {Path}", tempImagePath);

                // Step 3: Run OCR on the temp image file
                var result = await _documentParser.ParseAsync(
                    ocrText: "",  // Not used - DocumentParser extracts its own
                    imagePath: tempImagePath,
                    documentType: "BankSlips",
                    formatName: patternSetName
                );

                if (result != null && result.Count > 0)
                {
                    // Override FileName with original bin filename (not temp file name)
                    result["FileName"] = Path.GetFileName(binFilePath);

                    _logger.LogDebug("✅ OCR extracted {Count} fields", result.Count);
                    return result;
                }

                _logger.LogWarning("⚠️ OCR returned no fields for {PatternSet}", patternSetName);
                return CreateMissingPatternResult(binFilePath);
            }
            finally
            {
                // Step 4: Clean up temp file
                if (File.Exists(tempImagePath))
                {
                    try { File.Delete(tempImagePath); }
                    catch { /* ignore cleanup errors */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OCR failed for pattern set: {PatternSet}", patternSetName);
            return CreateMissingPatternResult(binFilePath);
        }
    }

    /// <summary>
    /// Detect image format from magic bytes
    /// </summary>
    private string DetectImageFormat(byte[] bytes)
    {
        if (bytes.Length >= 3)
        {
            // JPEG: FF D8 FF
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
                return ".jpg";

            // PNG: 89 50 4E 47
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ".png";

            // GIF: 47 49 46
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
                return ".gif";

            // BMP: 42 4D
            if (bytes[0] == 0x42 && bytes[1] == 0x4D)
                return ".bmp";
        }

        // Default to JPG (most common for bank slips)
        return ".jpg";
    }

    /// <summary>
    /// Create a result dictionary indicating OCR/pattern failure
    /// </summary>
    private Dictionary<string, string> CreateMissingPatternResult(string filePath)
    {
        return new Dictionary<string, string>
        {
            ["FileName"] = Path.GetFileName(filePath),
            ["_error"] = "OCR extraction failed"
        };
    }

    #endregion

    #region Project Creation

    private BankSlipProject CreateProject(BankSlipFilenameInfo filenameInfo, Dictionary<string, string> extractedFields)
    {
        var project = new BankSlipProject
        {
            ProjectId = filenameInfo.GetProjectId(),
            PatternSetName = filenameInfo.PatternSetName,
            Username = filenameInfo.Username,
            TransactionTimestamp = filenameInfo.Timestamp,
            ExtractedFields = extractedFields,
            ProcessedAt = DateTime.UtcNow
        };

        // Set processing error if critical fields missing
        if (!project.HasCriticalFields)
        {
            project.ProcessingError = "OCR failed to extract critical fields (To and/or Total)";
        }

        return project;
    }

    private void ParseMemoField(BankSlipProject project)
    {
        var noteText = project.GetNote();

        if (string.IsNullOrWhiteSpace(noteText))
        {
            project.HasStructuralNote = false;
            _logger.LogDebug("📝 No note field in OCR results");
            return;
        }

        var memoResult = _memoParser.Parse(noteText);

        project.HasStructuralNote = memoResult.IsStructuralNote;
        project.HasVat = memoResult.HasVat;
        project.StructuredMemo = memoResult.ParsedMemo;

        if (memoResult.IsStructuralNote)
        {
            _logger.LogDebug("📝 Parsed structural note: Category={Cat}, VAT={Vat}",
                memoResult.ParsedMemo?.CategoryName, memoResult.HasVat);
        }
        else
        {
            _logger.LogDebug("📝 Note is not structural format: {Reason}", memoResult.FailureReason);
        }
    }

    private ReviewReason DetermineReviewReason(BankSlipProject project)
    {
        // Priority 1: OCR failed (missing critical fields)
        if (!project.HasCriticalFields)
        {
            return ReviewReason.OcrFailed;
        }

        // Priority 2: Missing structural note
        if (!project.HasStructuralNote)
        {
            return ReviewReason.MissingStructuralNote;
        }

        // Default: Normal review (everything looks good, just needs verification)
        return ReviewReason.NormalReview;
    }

    #endregion

    #region Storage Operations

    private async Task<bool> ProjectExistsAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            return await storage.ExistsAsync($"{PROJECTS_SUBFOLDER}/{projectId}");
        }
        catch
        {
            return false;
        }
    }

    private async Task SaveProjectAsync(BankSlipProject project)
    {
        var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
        await storage.SaveAsync($"{PROJECTS_SUBFOLDER}/{project.ProjectId}", project);
        _logger.LogDebug("💾 Saved project: {ProjectId}", project.ProjectId);
    }

    private async Task UpdateIndexAsync(BankSlipProject project)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProjectIndex>(PROJECTS_FOLDER);
            var index = await LoadOrCreateAsync(storage, INDEX_FILENAME, () => new BankSlipProjectIndex());

            var entry = BankSlipProjectIndexEntry.FromProject(project);
            index.UpdateEntry(entry);

            await storage.SaveAsync(INDEX_FILENAME, index);
            _logger.LogDebug("📋 Updated project index");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to update index for: {ProjectId}", project.ProjectId);
        }
    }

    private async Task AddToReviewQueueAsync(string projectId, ReviewReason reason)
    {
        try
        {
            var storage = _storageFactory.GetStorage<ReviewQueue>(PROJECTS_FOLDER);
            var queue = await LoadOrCreateAsync(storage, REVIEW_QUEUE_FILENAME, () => new ReviewQueue());

            queue.AddEntry(new ReviewQueueEntry
            {
                ProjectId = projectId,
                AddedAt = DateTime.UtcNow,
                Reason = reason
            });

            await storage.SaveAsync(REVIEW_QUEUE_FILENAME, queue);
            _logger.LogDebug("📝 Added to review queue: {ProjectId} ({Reason})", projectId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to add to review queue: {ProjectId}", projectId);
        }
    }

    /// <summary>
    /// Helper to load existing or create new object
    /// </summary>
    private async Task<T> LoadOrCreateAsync<T>(IDataStorage<T> storage, string identifier, Func<T> createNew)
        where T : class, new()
    {
        try
        {
            if (await storage.ExistsAsync(identifier))
            {
                var existing = await storage.LoadAsync(identifier);
                if (existing != null) return existing;
            }
        }
        catch { /* ignore load errors, create new */ }

        return createNew();
    }

    #endregion

    #region Query Methods (for UI)

    /// <summary>
    /// Get all projects (for list view)
    /// </summary>
    public async Task<List<BankSlipProject>> GetAllProjectsAsync()
    {
        var projects = new List<BankSlipProject>();

        try
        {
            // ListIdentifiersAsync doesn't search subfolders, so we need to list directly
            var projectsPath = Path.Combine(
                StorageConfiguration.DEFAULT_BASE_DIRECTORY,
                PROJECTS_FOLDER,
                PROJECTS_SUBFOLDER);

            _logger.LogDebug("📂 Looking for projects in: {Path}", projectsPath);

            if (!Directory.Exists(projectsPath))
            {
                _logger.LogWarning("⚠️ Projects folder doesn't exist: {Path}", projectsPath);
                return projects;
            }

            var projectFiles = Directory.GetFiles(projectsPath, "*.json");
            _logger.LogInformation("📋 Found {Count} project files", projectFiles.Length);

            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);

            foreach (var filePath in projectFiles)
            {
                try
                {
                    var projectId = Path.GetFileNameWithoutExtension(filePath);
                    var identifier = $"{PROJECTS_SUBFOLDER}/{projectId}";

                    var project = await storage.LoadAsync(identifier);
                    if (project != null)
                    {
                        projects.Add(project);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading project: {File}", filePath);
                }
            }

            _logger.LogInformation("✅ Loaded {Count} projects", projects.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading all projects");
        }

        return projects;
    }

    /// <summary>
    /// Get all projects pending review
    /// </summary>
    public async Task<List<ReviewQueueEntry>> GetReviewQueueAsync()
    {
        try
        {
            var storage = _storageFactory.GetStorage<ReviewQueue>(PROJECTS_FOLDER);
            if (await storage.ExistsAsync(REVIEW_QUEUE_FILENAME))
            {
                var queue = await storage.LoadAsync(REVIEW_QUEUE_FILENAME);
                return queue?.Entries ?? new List<ReviewQueueEntry>();
            }
            return new List<ReviewQueueEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading review queue");
            return new List<ReviewQueueEntry>();
        }
    }

    /// <summary>
    /// Get a specific project by ID
    /// </summary>
    public async Task<BankSlipProject?> GetProjectAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            if (await storage.ExistsAsync($"{PROJECTS_SUBFOLDER}/{projectId}"))
            {
                return await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{projectId}");
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project: {ProjectId}", projectId);
            return null;
        }
    }

    /// <summary>
    /// Update an existing project
    /// </summary>
    public async Task UpdateProjectAsync(BankSlipProject project)
    {
        await SaveProjectAsync(project);
        await UpdateIndexAsync(project);
    }

    /// <summary>
    /// Close a project (remove from review queue)
    /// </summary>
    public async Task CloseProjectAsync(string projectId)
    {
        try
        {
            // Update project
            var project = await GetProjectAsync(projectId);
            if (project != null)
            {
                project.IsClosed = true;
                await SaveProjectAsync(project);
                await UpdateIndexAsync(project);
            }

            // Remove from review queue
            var storage = _storageFactory.GetStorage<ReviewQueue>(PROJECTS_FOLDER);
            if (await storage.ExistsAsync(REVIEW_QUEUE_FILENAME))
            {
                var queue = await storage.LoadAsync(REVIEW_QUEUE_FILENAME);
                if (queue != null)
                {
                    queue.RemoveEntry(projectId);
                    await storage.SaveAsync(REVIEW_QUEUE_FILENAME, queue);
                }
            }

            _logger.LogInformation("✅ Closed project: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing project: {ProjectId}", projectId);
            throw;
        }
    }

    #endregion

    #region Fallback Pattern Logic

    /// <summary>
    /// Run OCR with automatic fallback pattern discovery
    /// Tries primary pattern first, then KBIZ_Fallback01, KBIZ_Fallback02, etc.
    /// </summary>
    private async Task<Dictionary<string, string>> RunOcrWithFallbackAsync(string imagePath, string patternSetName)
    {
        // Try primary pattern first
        var result = await RunOcrAsync(imagePath, patternSetName);

        if (HasCriticalFields(result))
        {
            _logger.LogDebug("✅ Primary pattern {Pattern} succeeded", patternSetName);
            return result;
        }

        _logger.LogWarning("⚠️ Primary pattern {Pattern} failed to extract critical fields, trying fallbacks...", patternSetName);

        // Try fallback patterns: PatternName_Fallback01, PatternName_Fallback02, etc.
        var fallbackIndex = 1;
        while (fallbackIndex <= 10) // Max 10 fallbacks
        {
            var fallbackPattern = $"{patternSetName}_Fallback{fallbackIndex:D2}";

            // Check if fallback pattern exists
            if (!await _patternManagement.HasPatternsAsync("BankSlips", fallbackPattern))
            {
                _logger.LogDebug("🔚 No more fallback patterns found (stopped at {Pattern})", fallbackPattern);
                break;
            }

            _logger.LogInformation("🔄 Trying fallback pattern: {Pattern}", fallbackPattern);
            result = await RunOcrAsync(imagePath, fallbackPattern);

            if (HasCriticalFields(result))
            {
                _logger.LogInformation("✅ Fallback pattern {Pattern} succeeded!", fallbackPattern);
                result["_UsedPattern"] = fallbackPattern; // Track which pattern worked
                return result;
            }

            fallbackIndex++;
        }

        _logger.LogWarning("❌ All patterns failed for {PatternSet}", patternSetName);
        return result; // Return last attempt (may have partial data)
    }

    /// <summary>
    /// Check if result has critical fields (Total is the key one)
    /// </summary>
    private bool HasCriticalFields(Dictionary<string, string> result)
    {
        if (result == null || result.Count == 0)
            return false;

        // Must have Total - this is the critical field
        if (!result.TryGetValue("Total", out var total))
            return false;

        if (string.IsNullOrWhiteSpace(total))
            return false;

        // Check it's not an error placeholder
        if (total.StartsWith("Missing") || total == "Error")
            return false;

        return true;
    }

    #endregion

    #region Rescan

    /// <summary>
    /// Rescan an existing project - re-runs OCR with fallback logic
    /// </summary>
    public async Task<BankSlipProject?> RescanProjectAsync(string projectId)
    {
        _logger.LogInformation("🔄 Rescanning project: {ProjectId}", projectId);

        try
        {
            // Step 1: Load existing project
            var project = await GetProjectAsync(projectId);
            if (project == null)
            {
                _logger.LogWarning("❌ Project not found: {ProjectId}", projectId);
                return null;
            }

            // Step 2: Find the .bin file
            var binFilePath = GetBinFilePath(projectId);
            if (!File.Exists(binFilePath))
            {
                _logger.LogWarning("❌ Binary file not found: {FilePath}", binFilePath);
                return null;
            }

            // Step 3: Re-run OCR with fallback logic
            var extractedFields = await RunOcrWithFallbackAsync(binFilePath, project.PatternSetName);

            // Step 4: Update project with new fields
            project.ExtractedFields = extractedFields;
            project.ProcessedAt = DateTime.UtcNow;

            // Track if fallback was used
            if (extractedFields.TryGetValue("_UsedPattern", out var usedPattern))
            {
                project.ExtractedFields["_FallbackPattern"] = usedPattern;
                extractedFields.Remove("_UsedPattern");
            }

            // Step 5: Update processing error status
            if (HasCriticalFields(extractedFields))
            {
                project.ProcessingError = null; // Clear error - we got data!
            }
            else
            {
                project.ProcessingError = "OCR failed to extract critical fields after all fallback attempts";
            }

            // Step 6: Re-parse memo field
            ParseMemoField(project);

            // Step 7: Save updated project
            await SaveProjectAsync(project);

            // Step 8: Update review queue
            var reviewReason = DetermineReviewReason(project);
            await AddToReviewQueueAsync(project.ProjectId, reviewReason);

            _logger.LogInformation("✅ Rescan complete for {ProjectId}: {FieldCount} fields extracted",
                projectId, extractedFields.Count);

            return project;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error rescanning project: {ProjectId}", projectId);
            return null;
        }
    }

    /// <summary>
    /// Get the binary file path for a project
    /// </summary>
    private string GetBinFilePath(string projectId)
    {
        return Path.Combine(
            StorageConfiguration.DEFAULT_BASE_DIRECTORY,
            "BankSlipsBin",
            $"{projectId}.bin");
    }

    #endregion
}

/// <summary>
/// Result of batch processing operation
/// </summary>
public class BatchProcessResult
{
    public int Succeeded { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> ProcessedProjectIds { get; set; } = new();
    public List<string> FailedFiles { get; set; } = new();
}

/// <summary>
/// Container for the project index - stored as ProjectIndex.json
/// </summary>
public class BankSlipProjectIndex
{
    public List<BankSlipProjectIndexEntry> Entries { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public void UpdateEntry(BankSlipProjectIndexEntry entry)
    {
        // Remove old entry if exists, add new
        Entries.RemoveAll(e => e.ProjectId == entry.ProjectId);
        Entries.Add(entry);
        LastUpdated = DateTime.UtcNow;
    }
}

/// <summary>
/// Container for the review queue - stored as ReviewQueue.json
/// </summary>
public class ReviewQueue
{
    public List<ReviewQueueEntry> Entries { get; set; } = new();

    public void AddEntry(ReviewQueueEntry entry)
    {
        // Don't add duplicates
        if (!Entries.Any(e => e.ProjectId == entry.ProjectId))
        {
            Entries.Add(entry);
        }
    }

    public void RemoveEntry(string projectId)
    {
        Entries.RemoveAll(e => e.ProjectId == projectId);
    }
}