// NewwaysAdmin.WebAdmin/Services/BankSlips/Processing/BankSlipStartupScanner.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;

/// <summary>
/// Scans BankSlipsBin folder for unprocessed files on startup.
/// Uses hybrid approach:
/// - BankSlipsBin: Direct file access (raw binary files)
/// - BankSlipJson: Storage system (JSON objects)
/// </summary>
public class BankSlipStartupScanner
{
    private readonly ILogger<BankSlipStartupScanner> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly EnhancedStorageFactory _storageFactory;

    // Folder names (registered in storage system)
    private const string BIN_FOLDER = "BankSlipsBin";
    private const string JSON_FOLDER = "BankSlipJson";
    private const string PROJECTS_SUBFOLDER = "Projects";

    public BankSlipStartupScanner(
        ILogger<BankSlipStartupScanner> logger,
        IServiceProvider serviceProvider,
        EnhancedStorageFactory storageFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _storageFactory = storageFactory;
    }

    /// <summary>
    /// Scan for and process any unprocessed bank slips.
    /// Called on application startup.
    /// </summary>
    public async Task ScanAndProcessAsync()
    {
        _logger.LogInformation("🔍 Starting bank slip startup scan...");

        try
        {
            // Step 1: Get list of .bin files using storage system's path resolution
            var binFolderPath = GetStorageFolderPath(BIN_FOLDER);

            if (!Directory.Exists(binFolderPath))
            {
                _logger.LogWarning("BankSlipsBin folder not found: {Path}", binFolderPath);
                _logger.LogInformation("✅ No unprocessed bank slips found");
                return;
            }

            var allBinFiles = Directory.GetFiles(binFolderPath, "*.bin", SearchOption.AllDirectories);

            if (allBinFiles.Length == 0)
            {
                _logger.LogInformation("✅ No bank slip files found in BankSlipsBin");
                return;
            }

            _logger.LogInformation("📁 Found {Count} files in BankSlipsBin", allBinFiles.Length);

            // Step 2: Get existing project IDs using storage system
            var existingProjectIds = await GetExistingProjectIdsAsync();
            _logger.LogDebug("Found {Count} existing projects", existingProjectIds.Count);

            // Step 3: Find unprocessed files
            var unprocessedFiles = allBinFiles
                .Where(f => !existingProjectIds.Contains(Path.GetFileNameWithoutExtension(f)))
                .ToList();

            if (unprocessedFiles.Count == 0)
            {
                _logger.LogInformation("✅ All {Count} bank slips already processed", allBinFiles.Length);
                return;
            }

            _logger.LogInformation("📋 Found {Unprocessed} unprocessed files (out of {Total} total)",
                unprocessedFiles.Count, allBinFiles.Length);

            // Step 4: Process using the project service
            using var scope = _serviceProvider.CreateScope();
            var projectService = scope.ServiceProvider.GetRequiredService<BankSlipProjectService>();

            var result = await projectService.ProcessBatchAsync(unprocessedFiles);

            _logger.LogInformation("✅ Startup scan complete: {Processed} processed, {Skipped} already done, {Failed} failed",
                result.Succeeded, result.Skipped, result.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during startup scan");
        }
    }

    /// <summary>
    /// Get the full path to a registered storage folder.
    /// Uses StorageConfiguration.DEFAULT_BASE_DIRECTORY + folder name.
    /// </summary>
    private string GetStorageFolderPath(string folderName)
    {
        // Simple approach: folder name = folder path for our folders
        return Path.Combine(StorageConfiguration.DEFAULT_BASE_DIRECTORY, folderName);
    }

    /// <summary>
    /// Get set of existing project IDs from BankSlipJson/Projects folder
    /// </summary>
    private async Task<HashSet<string>> GetExistingProjectIdsAsync()
    {
        var projectIds = new HashSet<string>();

        try
        {
            var storage = _storageFactory.GetStorage<BankSlipProject>(JSON_FOLDER);
            var allIdentifiers = await storage.ListIdentifiersAsync();

            foreach (var identifier in allIdentifiers)
            {
                if (identifier.StartsWith($"{PROJECTS_SUBFOLDER}/"))
                {
                    var projectId = identifier.Replace($"{PROJECTS_SUBFOLDER}/", "");
                    projectIds.Add(projectId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting existing project IDs, assuming none exist");
        }

        return projectIds;
    }

    /// <summary>
    /// Get statistics about bank slip processing
    /// </summary>
    public async Task<(int total, int processed, int pending)> GetStatisticsAsync()
    {
        try
        {
            var binFolderPath = GetStorageFolderPath(BIN_FOLDER);
            var total = 0;

            if (Directory.Exists(binFolderPath))
            {
                total = Directory.GetFiles(binFolderPath, "*.bin", SearchOption.AllDirectories).Length;
            }

            var existingProjectIds = await GetExistingProjectIdsAsync();
            var processed = existingProjectIds.Count;

            return (total, processed, total - processed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting statistics");
            return (0, 0, 0);
        }
    }
}