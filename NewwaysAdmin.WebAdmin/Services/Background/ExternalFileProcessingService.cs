// NewwaysAdmin.WebAdmin/Services/Background/ExternalFileProcessingService.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.FileIndexing.Core;
using NewwaysAdmin.Shared.IO.FileIndexing.Models;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.Services.FileProcessing;

namespace NewwaysAdmin.WebAdmin.Services.Background
{
    /// <summary>
    /// Background service that monitors external collections for new files
    /// and processes them using registered processors (expandable system)
    /// </summary>
    public class ExternalFileProcessingService : BackgroundService
    {
        private readonly ILogger<ExternalFileProcessingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<IExternalFileProcessor> _processors = new();
        private readonly Dictionary<string, DateTime> _lastScanTimes = new();

        private readonly TimeSpan _scanInterval = TimeSpan.FromMinutes(1); // Check every minute
        private readonly TimeSpan _minimumRescanInterval = TimeSpan.FromMinutes(5); // Rescan collections every 5 min

        public ExternalFileProcessingService(
            ILogger<ExternalFileProcessingService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Register a file processor (called during startup)
        /// </summary>
        public void RegisterProcessor(IExternalFileProcessor processor)
        {
            if (processor == null) throw new ArgumentNullException(nameof(processor));

            _processors.Add(processor);
            _logger.LogInformation("📝 Registered file processor: {ProcessorName} for extensions: {Extensions}",
                processor.Name, string.Join(", ", processor.Extensions));
        }

        /// <summary>
        /// Main background processing loop
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 ExternalFileProcessingService starting...");

            // Wait a bit for services to initialize
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await ProcessExternalCollectionsAsync(stoppingToken);
                    await Task.Delay(_scanInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("🛑 ExternalFileProcessingService stopping...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Critical error in ExternalFileProcessingService");
                throw; // Let the host handle restart
            }
        }

        /// <summary>
        /// Process all registered external collections
        /// </summary>
        private async Task ProcessExternalCollectionsAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var externalIndexManager = scope.ServiceProvider.GetRequiredService<ExternalIndexManager>();

                var collections = externalIndexManager.GetRegisteredCollections();
                var activeCollections = collections.Where(c => Directory.Exists(c.ExternalPath)).ToList();

                if (!activeCollections.Any())
                {
                    _logger.LogDebug("📁 No active external collections found");
                    return;
                }

                _logger.LogDebug("🔄 Processing {CollectionCount} external collections", activeCollections.Count());

                foreach (var collection in activeCollections)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    await ProcessSingleCollectionAsync(collection, externalIndexManager, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing external collections");
            }
        }

        /// <summary>
        /// Process a single external collection
        /// </summary>
        private async Task ProcessSingleCollectionAsync(
            ExternalIndexCollection collection,
            ExternalIndexManager externalIndexManager,
            CancellationToken stoppingToken)
        {
            try
            {
                var now = DateTime.UtcNow;
                var lastScan = _lastScanTimes.GetValueOrDefault(collection.Name, DateTime.MinValue);

                // Only rescan if enough time has passed
                if (now - lastScan < _minimumRescanInterval)
                {
                    return;
                }

                _logger.LogDebug("🔍 Scanning collection: {CollectionName} at {Path}",
                    collection.Name, collection.ExternalPath);

                // Rescan the collection to detect new files
                var scanSuccess = await externalIndexManager.ScanExternalFolderAsync(collection.Name);
                if (!scanSuccess)
                {
                    _logger.LogWarning("⚠️ Failed to scan collection: {CollectionName}", collection.Name);
                    return;
                }

                _lastScanTimes[collection.Name] = now;

                // Get the current index
                var indexEntries = await externalIndexManager.GetExternalIndexAsync(collection.Name);
                var unprocessedFiles = indexEntries
                    .Where(entry => entry.ProcessingStage == ProcessingStage.Detected)
                    .ToList();

                if (!unprocessedFiles.Any())
                {
                    _logger.LogDebug("✅ No unprocessed files in collection: {CollectionName}", collection.Name);
                    return;
                }

                _logger.LogInformation("📋 Found {UnprocessedCount} unprocessed files in collection: {CollectionName}",
                    unprocessedFiles.Count, collection.Name);

                // Process each unprocessed file
                foreach (var fileEntry in unprocessedFiles)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    await ProcessSingleFileAsync(fileEntry.FilePath, collection.Name, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing collection: {CollectionName}", collection.Name);
            }
        }

        /// <summary>
        /// Process a single file using appropriate processor
        /// </summary>
        private async Task ProcessSingleFileAsync(string filePath, string collectionName, CancellationToken stoppingToken)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("📁 File no longer exists: {FilePath}", filePath);
                    return;
                }

                var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();
                var processor = FindProcessorForExtension(fileExtension);

                if (processor == null)
                {
                    _logger.LogDebug("🚫 No processor found for file: {FileName} (extension: {Extension})",
                        Path.GetFileName(filePath), fileExtension);
                    return;
                }

                _logger.LogInformation("⚡ Processing file: {FileName} with {ProcessorName}",
                    Path.GetFileName(filePath), processor.Name);

                // Mark as processing started
                await UpdateFileProcessingStageAsync(filePath, collectionName, ProcessingStage.ProcessingStarted);

                // Process the file
                var success = await processor.ProcessAsync(filePath, collectionName);

                // Update processing stage based on result
                var finalStage = success
                    ? ProcessingStage.ProcessingCompleted
                    : ProcessingStage.ProcessingFailed;

                await UpdateFileProcessingStageAsync(filePath, collectionName, finalStage);

                if (success)
                {
                    _logger.LogInformation("✅ Successfully processed: {FileName}", Path.GetFileName(filePath));
                }
                else
                {
                    _logger.LogWarning("❌ Failed to process: {FileName}", Path.GetFileName(filePath));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Error processing file: {FileName}", Path.GetFileName(filePath));

                // Mark as failed
                await UpdateFileProcessingStageAsync(filePath, collectionName, ProcessingStage.ProcessingFailed);
            }
        }

        /// <summary>
        /// Find the appropriate processor for a file extension
        /// </summary>
        private IExternalFileProcessor? FindProcessorForExtension(string extension)
        {
            return _processors.FirstOrDefault(p =>
                p.Extensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Update the processing stage for a file in the index
        /// </summary>
        private async Task UpdateFileProcessingStageAsync(string filePath, string collectionName, ProcessingStage stage)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var externalIndexManager = scope.ServiceProvider.GetRequiredService<ExternalIndexManager>();

                // Get current index
                var indexEntries = await externalIndexManager.GetExternalIndexAsync(collectionName);
                var fileEntry = indexEntries.FirstOrDefault(e => e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

                if (fileEntry != null)
                {
                    fileEntry.ProcessingStage = stage;
                    fileEntry.LastModified = DateTime.UtcNow;

                    // Save updated index (this is a bit inefficient but simple for now)
                    await SaveUpdatedIndexAsync(collectionName, indexEntries);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Failed to update processing stage for {FilePath}", filePath);
            }
        }

        /// <summary>
        /// Save updated index back to storage
        /// </summary>
        private async Task SaveUpdatedIndexAsync(string collectionName, List<FileIndexEntry> indexEntries)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var storageFactory = scope.ServiceProvider.GetRequiredService<EnhancedStorageFactory>();
                var storage = storageFactory.GetStorage<List<FileIndexEntry>>("ExternalFileIndexes");

                await storage.SaveAsync($"index_{collectionName}", indexEntries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💥 Failed to save updated index for {CollectionName}", collectionName);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("🛑 ExternalFileProcessingService stopped");
            return base.StopAsync(cancellationToken);
        }
    }
}