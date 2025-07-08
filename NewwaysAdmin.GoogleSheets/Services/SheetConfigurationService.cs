using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.GoogleSheets.Services
{
    /// <summary>
    /// Service for managing user sheet configurations using IDataStorage
    /// </summary>
    public class SheetConfigurationService
    {
        private readonly ModuleColumnRegistry _columnRegistry;
        private readonly IDataStorage<UserSheetConfiguration> _userConfigStorage;
        private readonly IDataStorage<CustomColumnLibrary> _customColumnStorage;
        private readonly ILogger<SheetConfigurationService> _logger;

        public SheetConfigurationService(
            ModuleColumnRegistry columnRegistry,
            IDataStorage<UserSheetConfiguration> userConfigStorage,
            IDataStorage<CustomColumnLibrary> customColumnStorage,
            ILogger<SheetConfigurationService> logger)
        {
            _columnRegistry = columnRegistry ?? throw new ArgumentNullException(nameof(columnRegistry));
            _userConfigStorage = userConfigStorage ?? throw new ArgumentNullException(nameof(userConfigStorage));
            _customColumnStorage = customColumnStorage ?? throw new ArgumentNullException(nameof(customColumnStorage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Load user's sheet configuration for a module
        /// </summary>
        public async Task<UserSheetConfiguration?> LoadConfigurationAsync(string username, string moduleName, string configName = "Default")
        {
            try
            {
                var key = $"{username}_{moduleName}_{configName}";
                return await _userConfigStorage.LoadAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading sheet configuration for {Username}/{Module}/{Config}",
                    username, moduleName, configName);
                return null;
            }
        }

        /// <summary>
        /// Save user's sheet configuration
        /// </summary>
        public async Task<bool> SaveConfigurationAsync(string username, UserSheetConfiguration config)
        {
            try
            {
                config.LastModified = DateTime.UtcNow;
                var key = $"{username}_{config.ModuleName}_{config.ConfigurationName}";
                await _userConfigStorage.SaveAsync(key, config);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving sheet configuration for {Username}/{Module}/{Config}",
                    username, config.ModuleName, config.ConfigurationName);
                return false;
            }
        }

        /// <summary>
        /// Load custom column library for a module
        /// </summary>
        public async Task<CustomColumnLibrary> LoadCustomColumnLibraryAsync(string username, string moduleName)
        {
            try
            {
                var key = $"{username}_{moduleName}";
                var library = await _customColumnStorage.LoadAsync(key);

                if (library == null)
                {
                    // Create default library with common templates
                    library = CreateDefaultCustomColumnLibrary(moduleName);
                    await SaveCustomColumnLibraryAsync(username, library);
                }

                return library;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading custom column library for {Username}/{Module}", username, moduleName);
                return CreateDefaultCustomColumnLibrary(moduleName);
            }
        }

        /// <summary>
        /// Save custom column library
        /// </summary>
        public async Task<bool> SaveCustomColumnLibraryAsync(string username, CustomColumnLibrary library)
        {
            try
            {
                library.LastModified = DateTime.UtcNow;
                var key = $"{username}_{library.ModuleName}";
                await _customColumnStorage.SaveAsync(key, library);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving custom column library for {Username}/{Module}",
                    username, library.ModuleName);
                return false;
            }
        }

        /// <summary>
        /// Get available columns for a module with current user selection state
        /// Uses natural order from ModuleColumnRegistry
        /// </summary>
        public List<ColumnDefinition> GetAvailableColumnsForModule(string moduleName, UserSheetConfiguration? userConfig = null)
        {
            var availableColumns = _columnRegistry.GetModuleColumns(moduleName);

            // Apply user selection state if config provided
            if (userConfig != null)
            {
                foreach (var column in availableColumns)
                {
                    var selectedColumn = userConfig.SelectedColumns
                        .FirstOrDefault(sc => sc.PropertyName == column.PropertyName);

                    column.IsSelected = selectedColumn?.IsEnabled ?? column.IsSelected;
                }
            }

            return availableColumns;
        }

        /// <summary>
        /// Create a default custom column library for a module
        /// </summary>
        private CustomColumnLibrary CreateDefaultCustomColumnLibrary(string moduleName)
        {
            var library = new CustomColumnLibrary
            {
                ModuleName = moduleName,
                CreatedAt = DateTime.UtcNow,
                LastModified = DateTime.UtcNow
            };

            // Add common templates based on module
            switch (moduleName.ToLower())
            {
                case "bankslips":
                    library.Templates.AddRange(CreateBankSlipTemplates());
                    break;

                case "sales":
                    library.Templates.AddRange(CreateSalesTemplates());
                    break;

                default:
                    library.Templates.AddRange(CreateGenericTemplates());
                    break;
            }

            return library;
        }

        /// <summary>
        /// Create default templates for bank slip module
        /// </summary>
        private List<CustomColumnTemplate> CreateBankSlipTemplates()
        {
            return new List<CustomColumnTemplate>
            {
                new CustomColumnTemplate
                {
                    Name = "Gas Expenses",
                    FormulaType = FormulaType.SumIf,
                    DataType = DataType.Currency,
                    SumColumnName = "Amount",
                    CriteriaColumn = "Description",
                    CriteriaValue = "*gas*"
                },
                new CustomColumnTemplate
                {
                    Name = "Food Expenses",
                    FormulaType = FormulaType.SumIf,
                    DataType = DataType.Currency,
                    SumColumnName = "Amount",
                    CriteriaColumn = "Description",
                    CriteriaValue = "*food*"
                },
                new CustomColumnTemplate
                {
                    Name = "Total Count",
                    FormulaType = FormulaType.Count,
                    DataType = DataType.Number,
                    SumColumnName = "Amount"
                }
            };
        }

        /// <summary>
        /// Create default templates for sales module
        /// </summary>
        private List<CustomColumnTemplate> CreateSalesTemplates()
        {
            return new List<CustomColumnTemplate>
            {
                new CustomColumnTemplate
                {
                    Name = "Total Revenue",
                    FormulaType = FormulaType.Sum,
                    DataType = DataType.Currency,
                    SumColumnName = "Amount"
                },
                new CustomColumnTemplate
                {
                    Name = "Average Sale",
                    FormulaType = FormulaType.Average,
                    DataType = DataType.Currency,
                    SumColumnName = "Amount"
                }
            };
        }

        /// <summary>
        /// Create generic templates
        /// </summary>
        private List<CustomColumnTemplate> CreateGenericTemplates()
        {
            return new List<CustomColumnTemplate>
            {
                new CustomColumnTemplate
                {
                    Name = "Total Count",
                    FormulaType = FormulaType.Count,
                    DataType = DataType.Number,
                    SumColumnName = "Id"
                }
            };
        }

        /// <summary>
        /// Delete a user's configuration
        /// </summary>
        public async Task<bool> DeleteConfigurationAsync(string username, string moduleName, string configName = "Default")
        {
            try
            {
                var key = $"{username}_{moduleName}_{configName}";
                await _userConfigStorage.DeleteAsync(key);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting sheet configuration for {Username}/{Module}/{Config}",
                    username, moduleName, configName);
                return false;
            }
        }

        /// <summary>
        /// Get all configurations for a user
        /// </summary>
        public async Task<List<UserSheetConfiguration>> GetUserConfigurationsAsync(string username)
        {
            try
            {
                // Note: This would need to be implemented based on your IDataStorage interface
                // If it doesn't support listing keys, you might need to maintain an index
                _logger.LogWarning("GetUserConfigurationsAsync not fully implemented - IDataStorage may not support key enumeration");
                return new List<UserSheetConfiguration>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting configurations for user {Username}", username);
                return new List<UserSheetConfiguration>();
            }
        }
    }
}