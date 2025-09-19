// NewwaysAdmin.WebAdmin/Services/Monitoring/ExternalMonitorConfigService.cs

using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Shared.IO.FileIndexing.Core;
using NewwaysAdmin.SharedModels.Models.Monitoring;
using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Services.Monitoring
{
    /// <summary>
    /// Service for managing external monitor configurations
    /// Bridges the UI configuration with the existing monitoring infrastructure
    /// </summary>
    public class ExternalMonitorConfigService
    {
        private readonly ILogger<ExternalMonitorConfigService> _logger;
        private readonly EnhancedStorageFactory _storageFactory;
        private readonly IAuthenticationService _authService;
        private readonly ExternalIndexManager _externalIndexManager;

        // Storage identifier constant
        private const string CONFIG_IDENTIFIER = "external_monitor_configs";
        private const string STORAGE_FOLDER = "Settings";

        public ExternalMonitorConfigService(
            ILogger<ExternalMonitorConfigService> logger,
            EnhancedStorageFactory storageFactory,
            IAuthenticationService authService,
            ExternalIndexManager externalIndexManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _externalIndexManager = externalIndexManager ?? throw new ArgumentNullException(nameof(externalIndexManager));
        }

        /// <summary>
        /// Get storage instance
        /// </summary>
        private IDataStorage<List<ExternalMonitorConfig>> GetStorage()
        {
            // Get storage from factory - Settings folder should already be registered
            return _storageFactory.GetStorage<List<ExternalMonitorConfig>>(STORAGE_FOLDER);
        }

        /// <summary>
        /// Get all monitor configurations
        /// </summary>
        public async Task<List<ExternalMonitorConfig>> GetAllConfigsAsync()
        {
            try
            {
                var storage = GetStorage();

                // Check if configs exist
                if (!await storage.ExistsAsync(CONFIG_IDENTIFIER))
                {
                    _logger.LogDebug("No existing monitor configurations found, returning empty list");
                    return new List<ExternalMonitorConfig>();
                }

                var configs = await storage.LoadAsync(CONFIG_IDENTIFIER);
                _logger.LogDebug("Loaded {Count} external monitor configurations", configs.Count);
                return configs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load external monitor configurations");
                return new List<ExternalMonitorConfig>();
            }
        }

        /// <summary>
        /// Get a specific monitor configuration by ID
        /// </summary>
        public async Task<ExternalMonitorConfig?> GetConfigAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            var configs = await GetAllConfigsAsync();
            return configs.FirstOrDefault(c => c.Id == id);
        }

        /// <summary>
        /// Create a new monitor configuration
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> CreateConfigAsync(ExternalMonitorConfig config)
        {
            try
            {
                // Validate the configuration
                var (isValid, errorMessage) = config.Validate();
                if (!isValid)
                {
                    return (false, errorMessage);
                }

                // Check if path exists
                if (!Directory.Exists(config.ExternalPath))
                {
                    _logger.LogWarning("External path does not exist: {Path}", config.ExternalPath);
                    return (false, $"Path does not exist: {config.ExternalPath}");
                }

                // Set metadata
                var authState = await _authService.GetCurrentSessionAsync();
                config.CreatedBy = authState?.Username ?? "system";
                config.CreatedAt = DateTime.UtcNow;

                // Load existing configs
                var configs = await GetAllConfigsAsync();

                // Check for duplicate names
                if (configs.Any(c => c.Name.Equals(config.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, $"A monitor with name '{config.Name}' already exists");
                }

                // Add the new config
                configs.Add(config);

                // Save to storage
                var storage = GetStorage();
                await storage.SaveAsync(CONFIG_IDENTIFIER, configs);

                // Register with external index manager if active
                if (config.IsActive)
                {
                    var registered = await RegisterWithIndexManagerAsync(config);
                    if (!registered)
                    {
                        _logger.LogWarning("Config saved but failed to register with index manager: {Name}", config.Name);
                    }
                }

                _logger.LogInformation("Created external monitor config: {Name} for path: {Path}",
                    config.Name, config.ExternalPath);

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create external monitor config");
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update an existing monitor configuration
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateConfigAsync(ExternalMonitorConfig config)
        {
            try
            {
                // Validate the configuration
                var (isValid, errorMessage) = config.Validate();
                if (!isValid)
                {
                    return (false, errorMessage);
                }

                var configs = await GetAllConfigsAsync();
                var existingIndex = configs.FindIndex(c => c.Id == config.Id);

                if (existingIndex == -1)
                {
                    return (false, "Configuration not found");
                }

                // Update metadata
                var authState = await _authService.GetCurrentSessionAsync();
                config.ModifiedBy = authState?.Username ?? "system";
                config.ModifiedAt = DateTime.UtcNow;

                // Preserve statistics from existing config
                var existing = configs[existingIndex];
                config.ProcessedCount = existing.ProcessedCount;
                config.FailedCount = existing.FailedCount;
                config.PendingCount = existing.PendingCount;
                config.LastScanned = existing.LastScanned;

                // Replace the config
                configs[existingIndex] = config;

                // Save to storage
                var storage = GetStorage();
                await storage.SaveAsync(CONFIG_IDENTIFIER, configs);

                // Re-register with index manager if path or status changed
                if (config.ExternalPath != existing.ExternalPath ||
                    config.IsActive != existing.IsActive)
                {
                    if (config.IsActive)
                    {
                        await RegisterWithIndexManagerAsync(config);
                    }
                    // Note: We don't unregister inactive ones as the background service will skip them
                }

                _logger.LogInformation("Updated external monitor config: {Name}", config.Name);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update external monitor config");
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Delete a monitor configuration
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> DeleteConfigAsync(string id)
        {
            try
            {
                var configs = await GetAllConfigsAsync();
                var config = configs.FirstOrDefault(c => c.Id == id);

                if (config == null)
                {
                    return (false, "Configuration not found");
                }

                configs.Remove(config);

                // Save to storage
                var storage = GetStorage();
                await storage.SaveAsync(CONFIG_IDENTIFIER, configs);

                _logger.LogInformation("Deleted external monitor config: {Name}", config.Name);
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete external monitor config");
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Toggle active status of a monitor
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> ToggleActiveStatusAsync(string id)
        {
            try
            {
                var config = await GetConfigAsync(id);
                if (config == null)
                {
                    return (false, "Configuration not found");
                }

                config.IsActive = !config.IsActive;
                return await UpdateConfigAsync(config);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle active status for config {Id}", id);
                return (false, $"Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Register a configuration with the external index manager
        /// </summary>
        private async Task<bool> RegisterWithIndexManagerAsync(ExternalMonitorConfig config)
        {
            try
            {
                // Use the config ID as the collection name for the index manager
                // This ensures uniqueness and allows us to map back to the config
                var collectionName = $"Monitor_{config.Id}";

                var success = await _externalIndexManager.RegisterExternalFolderAsync(
                    collectionName,
                    config.ExternalPath,
                    config.MonitoredExtensions.ToArray()
                );

                if (success)
                {
                    _logger.LogInformation("Registered monitor {Name} with index manager as collection {CollectionName}",
                        config.Name, collectionName);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register monitor with index manager: {Name}", config.Name);
                return false;
            }
        }

        /// <summary>
        /// Get statistics for all monitors
        /// </summary>
        public async Task<MonitorStatisticsSummary> GetStatisticsSummaryAsync()
        {
            var configs = await GetAllConfigsAsync();

            return new MonitorStatisticsSummary
            {
                TotalMonitors = configs.Count,
                ActiveMonitors = configs.Count(c => c.IsActive),
                TotalProcessed = configs.Sum(c => c.ProcessedCount),
                TotalFailed = configs.Sum(c => c.FailedCount),
                TotalPending = configs.Sum(c => c.PendingCount)
            };
        }

        /// <summary>
        /// Get available document types from the pattern system
        /// TODO: Connect to actual pattern management service
        /// </summary>
        public async Task<List<string>> GetAvailableDocumentTypesAsync()
        {
            try
            {
                // TODO: Replace with actual call to pattern management service
                // For now, return common types
                await Task.CompletedTask; // Simulate async work

                return new List<string>
                {
                    "BankSlips",
                    "Invoices",
                    "Receipts",
                    "Bills",
                    "Documents"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available document types");
                return new List<string>();
            }
        }

        /// <summary>
        /// Get available formats for a document type
        /// TODO: Connect to actual pattern management service
        /// </summary>
        public async Task<List<string>> GetAvailableFormatsAsync(string documentType)
        {
            try
            {
                // TODO: Replace with actual call to pattern management service
                // For now, return formats based on document type
                await Task.CompletedTask; // Simulate async work

                return documentType switch
                {
                    "BankSlips" => new List<string> { "KBIZ", "KBank", "SCB", "BBL", "TMB" },
                    "Invoices" => new List<string> { "Standard", "Detailed", "Simple" },
                    "Receipts" => new List<string> { "Standard", "Simple" },
                    "Bills" => new List<string> { "Utility", "Service", "Product" },
                    _ => new List<string> { "Default" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available formats for {DocumentType}", documentType);
                return new List<string>();
            }
        }
    }

    /// <summary>
    /// Summary statistics for all monitors
    /// </summary>
    public class MonitorStatisticsSummary
    {
        public int TotalMonitors { get; set; }
        public int ActiveMonitors { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalFailed { get; set; }
        public int TotalPending { get; set; }
    }
}