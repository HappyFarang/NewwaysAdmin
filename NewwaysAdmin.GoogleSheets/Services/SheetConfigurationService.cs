﻿using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.IO.Manager;

namespace NewwaysAdmin.GoogleSheets.Services
{
    /// <summary>
    /// Service for managing user sheet configurations using IOManager
    /// </summary>
    public class SheetConfigurationService
    {
        private readonly ModuleColumnRegistry _columnRegistry;
        private readonly IOManager _ioManager;
        private readonly ILogger<SheetConfigurationService> _logger;
        private readonly GoogleSheetsConfig _config;

        // Storage for configurations and libraries
        private IDataStorage<UserSheetConfiguration>? _configStorage;
        private IDataStorage<CustomColumnLibrary>? _libraryStorage;

        public SheetConfigurationService(
            ModuleColumnRegistry columnRegistry,
            IOManager ioManager,
            ILogger<SheetConfigurationService> logger,
            GoogleSheetsConfig config)
        {
            _columnRegistry = columnRegistry;
            _ioManager = ioManager;
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Initialize storage connections
        /// </summary>
        private async Task EnsureStorageInitializedAsync()
        {
            if (_configStorage != null && _libraryStorage != null) return;

            _configStorage ??= await _ioManager.GetStorageAsync<UserSheetConfiguration>("GoogleSheets_UserConfigs");
            _libraryStorage ??= await _ioManager.GetStorageAsync<CustomColumnLibrary>("GoogleSheets_CustomColumns");
        }

        /// <summary>
        /// Load user's sheet configuration for a module
        /// </summary>
        public async Task<UserSheetConfiguration?> LoadConfigurationAsync(string username, string moduleName, string configName = "Default")
        {
            try
            {
                await EnsureStorageInitializedAsync();
                var key = $"{username}_{moduleName}_{configName}";
                return await _configStorage!.LoadAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration for {Username}/{Module}/{Config}", username, moduleName, configName);
                return null;
            }
        }

        /// <summary>
        /// Save user's sheet configuration for a module
        /// </summary>
        public async Task<bool> SaveConfigurationAsync(string username, UserSheetConfiguration config)
        {
            try
            {
                await EnsureStorageInitializedAsync();
                config.LastModified = DateTime.UtcNow;
                var key = $"{username}_{config.ModuleName}_{config.ConfigurationName}";
                await _configStorage!.SaveAsync(key, config);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration for {Username}/{Module}/{Config}",
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
                await EnsureStorageInitializedAsync();
                var key = $"{username}_{moduleName}";
                var library = await _libraryStorage!.LoadAsync(key);

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
                await EnsureStorageInitializedAsync();
                library.LastModified = DateTime.UtcNow;
                var key = $"{username}_{library.ModuleName}";
                await _libraryStorage!.SaveAsync(key, library);
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

                    column.IsSelected = selectedColumn?.IsEnabled ?? false;
                }
            }
            else
            {
                // Default: select commonly used columns
                foreach (var column in availableColumns)
                {
                    column.IsSelected = IsDefaultSelectedColumn(column);
                }
            }

            return availableColumns;
        }

        /// <summary>
        /// Create a default configuration for a module
        /// </summary>
        public UserSheetConfiguration CreateDefaultConfiguration(string moduleName)
        {
            var availableColumns = _columnRegistry.GetModuleColumns(moduleName);

            var config = new UserSheetConfiguration
            {
                ModuleName = moduleName,
                ConfigurationName = "Default"
            };

            // Add default selected columns using natural order from registry
            foreach (var column in availableColumns.Where(c => IsDefaultSelectedColumn(c)))
            {
                config.SelectedColumns.Add(new SelectedColumn
                {
                    PropertyName = column.PropertyName,
                    IsEnabled = true
                });
            }

            return config;
        }

        /// <summary>
        /// Generate sheet data based on user configuration
        /// </summary>
        public SheetData GenerateSheetData<T>(
            IEnumerable<T> data,
            UserSheetConfiguration config,
            Func<T, string, object?> propertyValueGetter)
        {
            var sheetData = new SheetData
            {
                Title = $"{config.ModuleName} Export - {DateTime.Now:yyyy-MM-dd}"
            };

            var dataList = data.ToList();
            var enabledColumns = GetEnabledColumnsInOrder(config);
            var customColumns = config.CustomColumns.ToList();

            // 1. Add header row
            if (config.RowSettings.UseHeaderRow)
            {
                var headerRow = CreateHeaderRow(config, enabledColumns, customColumns);
                sheetData.Rows.Add(headerRow);
            }

            // 2. Add formula row if enabled
            if (config.RowSettings.UseFormulaRow)
            {
                var formulaRow = CreateFormulaRow(config, enabledColumns, customColumns, dataList.Count);
                sheetData.Rows.Add(formulaRow);
            }

            // 3. Add data rows
            foreach (var item in dataList)
            {
                var dataRow = CreateDataRow(item, config, enabledColumns, customColumns, propertyValueGetter);
                sheetData.Rows.Add(dataRow);
            }

            // 4. Add summary rows
            if (config.RowSettings.AddSummaryRowsAfterData)
            {
                // Add empty row for separation
                sheetData.Rows.Add(new SheetRow());

                // Add total row for Amount column if present
                var amountColumnIndex = enabledColumns.FindIndex(ec => ec.PropertyName == "Amount");
                if (amountColumnIndex >= 0)
                {
                    var summaryRow = CreateAmountSummaryRow(amountColumnIndex, GetDataStartRow(config), dataList.Count);
                    sheetData.Rows.Add(summaryRow);
                }
            }

            return sheetData;
        }

        /// <summary>
        /// Get enabled columns in the natural order from ModuleColumnRegistry
        /// </summary>
        private List<ColumnDefinition> GetEnabledColumnsInOrder(UserSheetConfiguration config)
        {
            var availableColumns = _columnRegistry.GetModuleColumns(config.ModuleName);
            var enabledPropertyNames = config.SelectedColumns
                .Where(sc => sc.IsEnabled)
                .Select(sc => sc.PropertyName)
                .ToHashSet();

            // Return columns in registry order, but only enabled ones
            return availableColumns
                .Where(ac => enabledPropertyNames.Contains(ac.PropertyName))
                .ToList();
        }

        /// <summary>
        /// Create default custom column library with common templates for bank slips
        /// </summary>
        private CustomColumnLibrary CreateDefaultCustomColumnLibrary(string moduleName)
        {
            var library = new CustomColumnLibrary { ModuleName = moduleName };

            if (moduleName == "BankSlips")
            {
                library.Templates.AddRange(new[]
                {
                    new CustomColumnTemplate
                    {
                        Name = "Gas",
                        FormulaType = FormulaType.SumIf,
                        DataType = DataType.Currency,
                        SumColumnName = "Amount"
                    },
                    new CustomColumnTemplate
                    {
                        Name = "Labor",
                        FormulaType = FormulaType.SumIf,
                        DataType = DataType.Currency,
                        SumColumnName = "Amount"
                    },
                    new CustomColumnTemplate
                    {
                        Name = "Tools",
                        FormulaType = FormulaType.SumIf,
                        DataType = DataType.Currency,
                        SumColumnName = "Amount"
                    },
                    new CustomColumnTemplate
                    {
                        Name = "Staff",
                        FormulaType = FormulaType.SumIf,
                        DataType = DataType.Currency,
                        SumColumnName = "Amount"
                    },
                    new CustomColumnTemplate
                    {
                        Name = "Equipment",
                        FormulaType = FormulaType.SumIf,
                        DataType = DataType.Currency,
                        SumColumnName = "Amount"
                    }
                });
            }

            return library;
        }

        private bool IsDefaultSelectedColumn(ColumnDefinition column)
        {
            // Define which columns should be selected by default
            var defaultColumns = new[]
            {
                "TransactionDate", "Amount", "AccountName", "ReceiverName",
                "Note", "SlipCollectionName"
            };

            return defaultColumns.Contains(column.PropertyName);
        }

        private SheetRow CreateHeaderRow(UserSheetConfiguration config, List<ColumnDefinition> enabledColumns, List<CustomColumn> customColumns)
        {
            var headerRow = new SheetRow { IsHeader = true };

            // Add headers for selected pre-defined columns
            foreach (var column in enabledColumns)
            {
                headerRow.AddCell(column.DisplayName);
            }

            // FIXED: Add only ONE header for custom columns (checkbox column only)
            foreach (var customColumn in customColumns)
            {
                headerRow.AddCell($"{customColumn.Name} ✓"); // Only the tick column header
            }

            return headerRow;
        }

        private SheetRow CreateFormulaRow(UserSheetConfiguration config, List<ColumnDefinition> enabledColumns, List<CustomColumn> customColumns, int dataRowCount)
        {
            var formulaRow = new SheetRow();
            int columnIndex = 0;

            // Add empty cells for pre-defined columns (no formulas there)
            foreach (var column in enabledColumns)
            {
                formulaRow.AddCell("");
                columnIndex++;
            }

            // FIXED: Add formula directly to the tick box column
            var dataStartRow = GetDataStartRow(config);
            var dataEndRow = dataStartRow + dataRowCount - 1;

            foreach (var customColumn in customColumns)
            {
                var formula = GenerateFormula(customColumn, columnIndex, dataStartRow, dataEndRow, enabledColumns);
                formulaRow.AddCell(formula); // Formula goes in the tick box column
                columnIndex++;
                // REMOVED: No longer adding empty cell for separate tick column
            }

            return formulaRow;
        }


        private SheetRow CreateDataRow<T>(
            T item,
            UserSheetConfiguration config,
            List<ColumnDefinition> enabledColumns,
            List<CustomColumn> customColumns,
            Func<T, string, object?> propertyValueGetter)
        {
            var dataRow = new SheetRow();

            // Add data for pre-defined columns
            foreach (var column in enabledColumns)
            {
                var value = propertyValueGetter(item, column.PropertyName);
                dataRow.AddCell(FormatCellValue(value, column));
            }

            // FIXED: Add only one cell per custom column with proper checkbox formatting
            foreach (var customColumn in customColumns)
            {
                var checkboxCell = new SheetCell
                {
                    Value = false, // NEW - boolean value
                    IsCheckbox = true
                };
                dataRow.Cells.Add(checkboxCell);
            }

            return dataRow;
        }

        private SheetRow CreateAmountSummaryRow(int amountColumnIndex, int dataStartRow, int dataRowCount)
        {
            var summaryRow = new SheetRow();

            // Fill cells up to amount column
            for (int i = 0; i <= amountColumnIndex; i++)
            {
                if (i == amountColumnIndex)
                {
                    var dataEndRow = dataStartRow + dataRowCount - 1;
                    var columnLetter = ColumnLetterHelper.GetColumnLetter(i);
                    var formula = $"SUM({columnLetter}{dataStartRow}:{columnLetter}{dataEndRow})";
                    summaryRow.AddCell(formula);
                }
                else if (i == 0)
                {
                    summaryRow.AddCell("Total:");
                }
                else
                {
                    summaryRow.AddCell("");
                }
            }

            return summaryRow;
        }

        /// <summary>
        /// Generate formula for custom column - supports your tick box SUMIF system
        /// </summary>
        private string GenerateFormula(CustomColumn customColumn, int formulaColumnIndex, int dataStartRow, int dataEndRow, List<ColumnDefinition> enabledColumns)
        {
            var formulaColumnLetter = ColumnLetterHelper.GetColumnLetter(formulaColumnIndex);

            switch (customColumn.FormulaType)
            {
                case FormulaType.Sum:
                    // FIXED: Add "=" prefix
                    return $"=SUM({formulaColumnLetter}{dataStartRow}:{formulaColumnLetter}{dataEndRow})";

                case FormulaType.SumIf:
                    if (!string.IsNullOrEmpty(customColumn.SumColumnName))
                    {
                        // Find the sum column (e.g., "Amount")
                        var sumColumnIndex = enabledColumns.FindIndex(ec => ec.PropertyName == customColumn.SumColumnName);
                        if (sumColumnIndex >= 0)
                        {
                            var sumColumnLetter = ColumnLetterHelper.GetColumnLetter(sumColumnIndex);
                            // FIXED: Add "=" prefix and use same column for both formula and checkboxes
                            return $"=SUMIF({formulaColumnLetter}{dataStartRow}:{formulaColumnLetter}{dataEndRow}, TRUE, {sumColumnLetter}{dataStartRow}:{sumColumnLetter}{dataEndRow})";
                        }
                    }
                    return "";

                case FormulaType.Custom:
                    // FIXED: Add "=" prefix if not already present
                    var formula = customColumn.CustomFormula ?? "";
                    return formula.StartsWith("=") ? formula : $"={formula}";

                default:
                    return "";
            }
        }

        /// <summary>
        /// Calculate which row data starts on based on settings
        /// </summary>
        private int GetDataStartRow(UserSheetConfiguration config)
        {
            int row = 1;
            if (config.RowSettings.UseHeaderRow) row++;
            if (config.RowSettings.UseFormulaRow) row++;
            return row;
        }

        private string FormatCellValue(object? value, ColumnDefinition column)
        {
            if (value == null) return "";

            var format = column.Format;

            // Apply formatting based on data type
            return value switch
            {
                DateTime dt when format.Contains("yyyy") => dt.ToString(format),
                decimal dec when format.Contains("#,##0") => dec.ToString(format),
                double dbl when format.Contains("#,##0") => dbl.ToString(format),
                _ => value.ToString() ?? ""
            };
        }
        // <summary>
        /// Get all configurations for a user and module
        /// </summary>
        public async Task<List<UserSheetConfiguration>> GetUserConfigurationsAsync(string username, string moduleName)
        {
            try
            {
                await EnsureStorageInitializedAsync();

                // Get all identifiers from storage
                var allIdentifiers = await _configStorage!.ListIdentifiersAsync();

                // Filter to configurations for this user and module
                var userPrefix = $"{username}_{moduleName}_";
                var userIdentifiers = allIdentifiers.Where(id => id.StartsWith(userPrefix)).ToList();

                var configurations = new List<UserSheetConfiguration>();

                foreach (var identifier in userIdentifiers)
                {
                    try
                    {
                        var config = await _configStorage.LoadAsync(identifier);
                        if (config != null)
                        {
                            configurations.Add(config);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error loading configuration {Identifier}", identifier);
                    }
                }

                _logger.LogInformation("Loaded {Count} configurations for user {Username}, module {ModuleName}",
                    configurations.Count, username, moduleName);

                return configurations.OrderBy(c => c.ConfigurationName).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user configurations for {Username}/{Module}", username, moduleName);
                return new List<UserSheetConfiguration>();
            }
        }

        /// <summary>
        /// Delete a user configuration
        /// </summary>
        public async Task<bool> DeleteConfigurationAsync(string username, string moduleName, string configName)
        {
            try
            {
                await EnsureStorageInitializedAsync();
                var key = $"{username}_{moduleName}_{configName}";

                if (!await _configStorage!.ExistsAsync(key))
                {
                    _logger.LogWarning("Configuration not found: {Key}", key);
                    return false;
                }

                await _configStorage.DeleteAsync(key);
                _logger.LogInformation("Deleted configuration {Username}/{Module}/{Config}", username, moduleName, configName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting configuration {Username}/{Module}/{Config}", username, moduleName, configName);
                return false;
            }
        }
    }
}