// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/BankSlipStartupScanner.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Scans BankSlipsBin folder for unprocessed files on startup.
/// Also provides manual scan functionality for admin use.
/// </summary>
public class BankSlipStartupScanner
{
    private readonly ILogger<BankSlipStartupScanner> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOManager _ioManager;

    // Folder paths (matching StorageFolderDefinitions)
    private const string BANK_SLIPS_BIN_PATH = "BankSlipsBin";
    private const string BANK_SLIPS_JSON_PATH = "BankSlipJson";

    public BankSlipStartupScanner(
        ILogger<BankSlipStartupScanner> logger,
        IServiceProvider serviceProvider,
        IOManager ioManager)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _ioManager = ioManager;
    }

    /// <summary>
    /// Get the full path to a storage folder
    /// </summary>
    private string GetFolderPath(string folderPath)
    {
        return Path.Combine(_ioManager.LocalBaseFolder, folderPath);
    }

    /// <summary>
    /// Scan and process all unprocessed files in BankSlipsBin.
    /// Call this on application startup.
    /// </summary>
    public async Task ScanAndProcessAsync()
    {
        _logger.LogInformation("🔍 Starting bank slip startup scan...");

        try
        {
            // Get all .bin files from BankSlipsBin folder
            var binFiles = await GetUnprocessedFilesAsync();

            if (!binFiles.Any())
            {
                _logger.LogInformation("✅ No unprocessed bank slips found");
                return;
            }

            _logger.LogInformation("📁 Found {Count} files to check", binFiles.Count);

            // Process in a scoped service (BankSlipProjectService is scoped)
            using var scope = _serviceProvider.CreateScope();
            var projectService = scope.ServiceProvider.GetRequiredService<BankSlipProjectService>();

            var result = await projectService.ProcessBatchAsync(binFiles);

            _logger.LogInformation(
                "✅ Startup scan complete: {Succeeded} processed, {Skipped} already done, {Failed} failed",
                result.Succeeded, result.Skipped, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during startup scan");
        }
    }

    /// <summary>
    /// Get list of .bin files that haven't been processed yet.
    /// Compares BankSlipsBin files against existing projects in BankSlipsJson/Projects.
    /// </summary>
    private async Task<List<string>> GetUnprocessedFilesAsync()
    {
        var unprocessedFiles = new List<string>();

        try
        {
            // Get the BankSlipsBin folder path
            var bankSlipsBinPath = GetFolderPath(BANK_SLIPS_BIN_PATH);

            if (string.IsNullOrEmpty(bankSlipsBinPath) || !Directory.Exists(bankSlipsBinPath))
            {
                _logger.LogWarning("BankSlipsBin folder not found: {Path}", bankSlipsBinPath);
                return unprocessedFiles;
            }

            // Get all .bin files (including subfolders)
            var allBinFiles = Directory.GetFiles(bankSlipsBinPath, "*.bin", SearchOption.AllDirectories);
            _logger.LogDebug("Found {Count} total .bin files in BankSlipsBin", allBinFiles.Length);

            // Get existing project IDs
            var existingProjectIds = await GetExistingProjectIdsAsync();
            _logger.LogDebug("Found {Count} existing projects", existingProjectIds.Count);

            // Filter to only unprocessed files
            foreach (var filePath in allBinFiles)
            {
                var projectId = Path.GetFileNameWithoutExtension(filePath);

                if (!existingProjectIds.Contains(projectId))
                {
                    unprocessedFiles.Add(filePath);
                }
            }

            _logger.LogDebug("{Count} files are unprocessed", unprocessedFiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unprocessed files");
        }

        return unprocessedFiles;
    }

    /// <summary>
    /// Get set of existing project IDs from BankSlipsJson/Projects folder
    /// </summary>
    private async Task<HashSet<string>> GetExistingProjectIdsAsync()
    {
        var projectIds = new HashSet<string>();

        try
        {
            var projectsPath = Path.Combine(GetFolderPath(BANK_SLIPS_JSON_PATH), "Projects");

            if (!Directory.Exists(projectsPath))
            {
                // Projects folder doesn't exist yet - no existing projects
                return projectIds;
            }

            var projectFiles = Directory.GetFiles(projectsPath, "*.json");

            foreach (var file in projectFiles)
            {
                var projectId = Path.GetFileNameWithoutExtension(file);
                projectIds.Add(projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting existing project IDs");
        }

        return await Task.FromResult(projectIds);
    }

    /// <summary>
    /// Get statistics about the current state
    /// </summary>
    public async Task<ScanStatistics> GetStatisticsAsync()
    {
        var stats = new ScanStatistics();

        try
        {
            var bankSlipsBinPath = GetFolderPath(BANK_SLIPS_BIN_PATH);
            if (!string.IsNullOrEmpty(bankSlipsBinPath) && Directory.Exists(bankSlipsBinPath))
            {
                stats.TotalBinFiles = Directory.GetFiles(bankSlipsBinPath, "*.bin", SearchOption.AllDirectories).Length;
            }

            stats.ExistingProjects = (await GetExistingProjectIdsAsync()).Count;
            stats.UnprocessedFiles = (await GetUnprocessedFilesAsync()).Count;

            // Get review queue count
            using var scope = _serviceProvider.CreateScope();
            var projectService = scope.ServiceProvider.GetRequiredService<BankSlipProjectService>();
            var reviewQueue = await projectService.GetReviewQueueAsync();
            stats.PendingReview = reviewQueue.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting statistics");
        }

        return stats;
    }
}

/// <summary>
/// Statistics about bank slip processing state
/// </summary>
public class ScanStatistics
{
    public int TotalBinFiles { get; set; }
    public int ExistingProjects { get; set; }
    public int UnprocessedFiles { get; set; }
    public int PendingReview { get; set; }
}