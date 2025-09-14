//NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipCollectionExtensions.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.FileIndexing.Core;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    /// <summary>
    /// Extension service that connects existing SlipCollection objects to the external monitoring system
    /// Bridges the gap between the current bank slip system and new automated processing
    /// </summary>
    public class BankSlipCollectionExtensions
    {
        private readonly ExternalIndexManager _externalIndexManager;
        private readonly BankSlipOcrService _bankSlipService;
        private readonly IAuthenticationService _authService;
        private readonly ILogger<BankSlipCollectionExtensions> _logger;

        public BankSlipCollectionExtensions(
            ExternalIndexManager externalIndexManager,
            BankSlipOcrService bankSlipService,
            IAuthenticationService authService,
            ILogger<BankSlipCollectionExtensions> logger)
        {
            _externalIndexManager = externalIndexManager ?? throw new ArgumentNullException(nameof(externalIndexManager));
            _bankSlipService = bankSlipService ?? throw new ArgumentNullException(nameof(bankSlipService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Enable external monitoring for a SlipCollection
        /// Registers the collection's SourceDirectory with the external index manager
        /// </summary>
        public async Task<bool> EnableExternalMonitoringAsync(SlipCollection collection)
        {
            try
            {
                if (!collection.IsExternalMonitoringEnabled)
                {
                    _logger.LogDebug("Collection {CollectionName} does not have external monitoring enabled", collection.Name);
                    return false;
                }

                if (string.IsNullOrEmpty(collection.SourceDirectory) || !Directory.Exists(collection.SourceDirectory))
                {
                    _logger.LogWarning("Source directory {SourceDirectory} for collection {CollectionName} does not exist",
                        collection.SourceDirectory, collection.Name);
                    return false;
                }

                _logger.LogInformation("Enabling external monitoring for collection {CollectionName} on folder {SourceDirectory}",
                    collection.Name, collection.SourceDirectory);

                // Register with external index manager using the collection's unique ID
                var success = await _externalIndexManager.RegisterExternalFolderAsync(
                    collection.ExternalCollectionId,
                    collection.SourceDirectory,
                    collection.MonitoredExtensions
                );

                if (success)
                {
                    // Update collection statistics
                    collection.LastScanned = DateTime.UtcNow;
                    _logger.LogInformation("Successfully registered external monitoring for collection {CollectionName}", collection.Name);
                }
                else
                {
                    _logger.LogError("Failed to register external monitoring for collection {CollectionName}", collection.Name);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling external monitoring for collection {CollectionName}", collection.Name);
                return false;
            }
        }

        /// <summary>
        /// Disable external monitoring for a SlipCollection
        /// </summary>
        public async Task<bool> DisableExternalMonitoringAsync(SlipCollection collection)
        {
            try
            {
                _logger.LogInformation("Disabling external monitoring for collection {CollectionName}", collection.Name);

                var success = await _externalIndexManager.UnregisterExternalFolderAsync(collection.ExternalCollectionId);

                if (success)
                {
                    collection.EnableExternalMonitoring = false;
                    _logger.LogInformation("Successfully disabled external monitoring for collection {CollectionName}", collection.Name);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling external monitoring for collection {CollectionName}", collection.Name);
                return false;
            }
        }

        /// <summary>
        /// Get monitoring status and statistics for a collection
        /// </summary>
        public async Task<CollectionMonitoringStatus> GetMonitoringStatusAsync(SlipCollection collection)
        {
            try
            {
                var status = new CollectionMonitoringStatus
                {
                    CollectionId = collection.Id,
                    CollectionName = collection.Name,
                    IsMonitored = collection.IsExternalMonitoringEnabled,
                    SourceDirectory = collection.SourceDirectory,
                    LastScanned = collection.LastScanned,
                    LastProcessed = collection.LastProcessed,
                    ProcessedFileCount = collection.ProcessedFileCount,
                    FailedFileCount = collection.FailedFileCount
                };

                // Get detailed stats from external index if monitoring is enabled
                if (collection.IsExternalMonitoringEnabled)
                {
                    var externalIndex = await _externalIndexManager.GetExternalIndexAsync(collection.ExternalCollectionId);
                    if (externalIndex != null && externalIndex.Count > 0)
                    {
                        status.TotalFiles = externalIndex.Count;
                        status.UnprocessedFiles = externalIndex
                            .Count(f => f.ProcessingStage == NewwaysAdmin.Shared.IO.FileIndexing.Models.ProcessingStage.Detected);
                    }
                }

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting monitoring status for collection {CollectionName}", collection.Name);
                return new CollectionMonitoringStatus
                {
                    CollectionId = collection.Id,
                    CollectionName = collection.Name,
                    IsMonitored = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Scan a monitored collection for new files
        /// Triggers the external index manager to check for changes
        /// </summary>
        public async Task<bool> ScanCollectionAsync(SlipCollection collection)
        {
            try
            {
                if (!collection.IsExternalMonitoringEnabled)
                {
                    _logger.LogDebug("Collection {CollectionName} is not monitored, skipping scan", collection.Name);
                    return false;
                }

                _logger.LogDebug("Scanning collection {CollectionName} for new files", collection.Name);

                var success = await _externalIndexManager.ScanExternalFolderAsync(collection.ExternalCollectionId);

                if (success)
                {
                    collection.LastScanned = DateTime.UtcNow;
                    _logger.LogDebug("Successfully scanned collection {CollectionName}", collection.Name);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning collection {CollectionName}", collection.Name);
                return false;
            }
        }

        /// <summary>
        /// Check if a user has permission to access a collection
        /// </summary>
        public async Task<bool> CheckUserAccessAsync(SlipCollection collection, string username)
        {
            try
            {
                // Admin users have access to all collections
                var user = await _authService.GetUserByNameAsync(username);
                if (user?.IsAdmin == true)
                {
                    return true;
                }

                // Check if user is specifically authorized for this collection
                return collection.HasUserAccess(username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking user access for {Username} on collection {CollectionName}",
                    username, collection.Name);
                return false;
            }
        }

        /// <summary>
        /// Get all collections that a user has access to
        /// </summary>
        public async Task<List<SlipCollection>> GetUserAccessibleCollectionsAsync(string username)
        {
            try
            {
                var allCollections = await _bankSlipService.GetUserCollectionsAsync(username);
                var accessibleCollections = new List<SlipCollection>();

                foreach (var collection in allCollections)
                {
                    if (await CheckUserAccessAsync(collection, username))
                    {
                        accessibleCollections.Add(collection);
                    }
                }

                _logger.LogDebug("User {Username} has access to {Count} collections", username, accessibleCollections.Count);
                return accessibleCollections;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting accessible collections for user {Username}", username);
                return new List<SlipCollection>();
            }
        }
    }

    /// <summary>
    /// Status information for a monitored collection
    /// </summary>
    public class CollectionMonitoringStatus
    {
        public string CollectionId { get; set; } = string.Empty;
        public string CollectionName { get; set; } = string.Empty;
        public bool IsMonitored { get; set; }
        public string SourceDirectory { get; set; } = string.Empty;
        public DateTime? LastScanned { get; set; }
        public DateTime? LastProcessed { get; set; }
        public int ProcessedFileCount { get; set; }
        public int FailedFileCount { get; set; }
        public int TotalFiles { get; set; }
        public int UnprocessedFiles { get; set; }
        public string? ErrorMessage { get; set; }

        public double ProcessingSuccessRate =>
            TotalFiles > 0 ? (double)ProcessedFileCount / TotalFiles * 100 : 0;
    }
}