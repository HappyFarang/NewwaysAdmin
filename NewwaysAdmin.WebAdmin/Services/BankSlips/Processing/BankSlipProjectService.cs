// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/BankSlipProjectService.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Orchestrates bank slip processing: OCR → memo parsing → project creation → storage.
/// Called when new files arrive via SignalR or during batch processing.
/// </summary>
public class BankSlipProjectService
{
    private readonly ILogger<BankSlipProjectService> _logger;
    private readonly DocumentParser _documentParser;
    private readonly BankSlipFilenameParser _filenameParser;
    private readonly MemoParserService _memoParser;
    private readonly EnhancedStorageFactory _storageFactory;
    private readonly IOManager _ioManager;

    // Storage identifiers
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
        IOManager ioManager)
    {
        _logger = logger;
        _documentParser = documentParser;
        _filenameParser = filenameParser;
        _memoParser = memoParser;
        _storageFactory = storageFactory;
        _ioManager = ioManager;
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

            // Step 3: Run OCR
            var extractedFields = await RunOcrAsync(binFilePath, filenameInfo.PatternSetName);

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

    private async Task<Dictionary<string, string>> RunOcrAsync(string imagePath, string patternSetName)
    {
        try
        {
            _logger.LogDebug("🔍 Running OCR with pattern set: {PatternSet}", patternSetName);

            var result = await _documentParser.ParseAsync(
                ocrText: "",  // Not used - DocumentParser extracts its own
                imagePath: imagePath,
                documentType: "BankSlips",
                formatName: patternSetName
            );

            if (result != null && result.Count > 0)
            {
                _logger.LogDebug("✅ OCR extracted {Count} fields", result.Count);
                return result;
            }

            _logger.LogWarning("⚠️ OCR returned no fields for {PatternSet}", patternSetName);
            return new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OCR failed for pattern set: {PatternSet}", patternSetName);
            return new Dictionary<string, string>();
        }
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
            var existing = await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{projectId}");
            return existing != null;
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
            // Load existing index
            var storage = _storageFactory.GetStorage<BankSlipProjectIndex>(PROJECTS_FOLDER);
            var index = await storage.LoadAsync(INDEX_FILENAME) ?? new BankSlipProjectIndex();

            // Create/update entry
            var entry = BankSlipProjectIndexEntry.FromProject(project);

            // Remove old entry if exists, add new
            index.Entries.RemoveAll(e => e.ProjectId == project.ProjectId);
            index.Entries.Add(entry);
            index.LastUpdated = DateTime.UtcNow;

            // Save
            await storage.SaveAsync(INDEX_FILENAME, index);
            _logger.LogDebug("📇 Updated index for: {ProjectId}", project.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update index for {ProjectId}", project.ProjectId);
            // Don't throw - project was saved successfully
        }
    }

    private async Task AddToReviewQueueAsync(string projectId, ReviewReason reason)
    {
        try
        {
            var storage = _storageFactory.GetStorage<ReviewQueue>(PROJECTS_FOLDER);
            var queue = await storage.LoadAsync(REVIEW_QUEUE_FILENAME) ?? new ReviewQueue();

            // Don't add duplicates
            if (queue.Entries.Any(e => e.ProjectId == projectId))
            {
                _logger.LogDebug("⏭️ Project already in review queue: {ProjectId}", projectId);
                return;
            }

            queue.Entries.Add(new ReviewQueueEntry
            {
                ProjectId = projectId,
                Reason = reason,
                AddedAt = DateTime.UtcNow
            });

            await storage.SaveAsync(REVIEW_QUEUE_FILENAME, queue);
            _logger.LogDebug("📋 Added to review queue: {ProjectId} ({Reason})", projectId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add to review queue: {ProjectId}", projectId);
            // Don't throw - project was saved successfully
        }
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
            var projectsPath = Path.Combine(
                _ioManager.LocalBaseFolder,
                "BankSlipJson",
                "Projects");

            if (!Directory.Exists(projectsPath))
            {
                _logger.LogDebug("Projects folder does not exist yet");
                return projects;
            }

            var projectFiles = Directory.GetFiles(projectsPath, "*.json");
            _logger.LogDebug("Found {Count} project files", projectFiles.Length);

            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);

            foreach (var file in projectFiles)
            {
                try
                {
                    var projectId = Path.GetFileNameWithoutExtension(file);
                    var project = await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{projectId}");
                    if (project != null)
                    {
                        projects.Add(project);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error loading project file: {File}", file);
                }
            }

            _logger.LogInformation("Loaded {Count} projects", projects.Count);
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
            var queue = await storage.LoadAsync(REVIEW_QUEUE_FILENAME);
            return queue?.Entries ?? new List<ReviewQueueEntry>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading review queue");
            return new List<ReviewQueueEntry>();
        }
    }

    /// <summary>
    /// Load a specific project by ID
    /// </summary>
    public async Task<BankSlipProject?> GetProjectAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(PROJECTS_FOLDER);
            return await storage.LoadAsync($"{PROJECTS_SUBFOLDER}/{projectId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project: {ProjectId}", projectId);
            return null;
        }
    }

    /// <summary>
    /// Close a project (mark as reviewed, remove from queue)
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
            await RemoveFromReviewQueueAsync(projectId);

            _logger.LogInformation("✅ Closed project: {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing project: {ProjectId}", projectId);
            throw;
        }
    }

    /// <summary>
    /// Update a project (after review edits)
    /// </summary>
    public async Task UpdateProjectAsync(BankSlipProject project)
    {
        await SaveProjectAsync(project);
        await UpdateIndexAsync(project);
        _logger.LogInformation("✅ Updated project: {ProjectId}", project.ProjectId);
    }

    private async Task RemoveFromReviewQueueAsync(string projectId)
    {
        try
        {
            var storage = _storageFactory.GetStorage<ReviewQueue>(PROJECTS_FOLDER);
            var queue = await storage.LoadAsync(REVIEW_QUEUE_FILENAME);

            if (queue != null)
            {
                queue.Entries.RemoveAll(e => e.ProjectId == projectId);
                await storage.SaveAsync(REVIEW_QUEUE_FILENAME, queue);
                _logger.LogDebug("📋 Removed from review queue: {ProjectId}", projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing from review queue: {ProjectId}", projectId);
        }
    }

    #endregion
}

#region Supporting Types

/// <summary>
/// Container for the project index
/// </summary>
public class BankSlipProjectIndex
{
    public List<BankSlipProjectIndexEntry> Entries { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Container for the review queue
/// </summary>
public class ReviewQueue
{
    public List<ReviewQueueEntry> Entries { get; set; } = new();
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

    public int Total => Succeeded + Skipped + Failed;
}

#endregion