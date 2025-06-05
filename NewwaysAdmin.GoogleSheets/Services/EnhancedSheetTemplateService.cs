// NewwaysAdmin.GoogleSheets/Services/EnhancedSheetTemplateService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.Shared.IO;

namespace NewwaysAdmin.GoogleSheets.Services
{
    public interface IEnhancedSheetTemplateService
    {
        Task<List<EnhancedSheetTemplate>> GetTemplatesAsync(string? dataType = null);
        Task<EnhancedSheetTemplate?> GetTemplateAsync(string templateId);
        Task<bool> SaveTemplateAsync(EnhancedSheetTemplate template);
        Task<bool> DeleteTemplateAsync(string templateId);
        Task<EnhancedSheetTemplate> CreateDefaultTemplateAsync<T>(string templateName);
        Task<List<CheckboxColumnTemplate>> GetReusableCheckboxTemplatesAsync();
        Task<bool> SaveCheckboxTemplateAsync(CheckboxColumnTemplate checkboxTemplate);
    }

    public class EnhancedSheetTemplateService : IEnhancedSheetTemplateService
    {
        private readonly IDataStorage<List<EnhancedSheetTemplate>> _templateStorage;
        private readonly IDataStorage<List<CheckboxColumnTemplate>> _checkboxStorage;
        private readonly ILogger<EnhancedSheetTemplateService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public EnhancedSheetTemplateService(
            IDataStorage<List<EnhancedSheetTemplate>> templateStorage,
            IDataStorage<List<CheckboxColumnTemplate>> checkboxStorage,
            ILogger<EnhancedSheetTemplateService> logger)
        {
            _templateStorage = templateStorage ?? throw new ArgumentNullException(nameof(templateStorage));
            _checkboxStorage = checkboxStorage ?? throw new ArgumentNullException(nameof(checkboxStorage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<EnhancedSheetTemplate>> GetTemplatesAsync(string? dataType = null)
        {
            try
            {
                await _lock.WaitAsync();

                var templates = await _templateStorage.LoadAsync("enhanced-sheet-templates") ?? new List<EnhancedSheetTemplate>();

                if (!string.IsNullOrEmpty(dataType))
                {
                    templates = templates.Where(t => t.DataType.Equals(dataType, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                return templates
                    .Where(t => t.IsActive)
                    .OrderBy(t => t.DataType)
                    .ThenBy(t => t.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving enhanced sheet templates for data type: {DataType}", dataType);
                return new List<EnhancedSheetTemplate>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<EnhancedSheetTemplate?> GetTemplateAsync(string templateId)
        {
            try
            {
                var templates = await GetTemplatesAsync();
                return templates.FirstOrDefault(t => t.Id == templateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving enhanced sheet template: {TemplateId}", templateId);
                return null;
            }
        }

        public async Task<bool> SaveTemplateAsync(EnhancedSheetTemplate template)
        {
            try
            {
                await _lock.WaitAsync();

                var templates = await _templateStorage.LoadAsync("enhanced-sheet-templates") ?? new List<EnhancedSheetTemplate>();

                // Validate template
                if (!ValidateTemplate(template))
                {
                    _logger.LogWarning("Template validation failed for: {TemplateName}", template.Name);
                    return false;
                }

                // Update existing or add new
                var existingIndex = templates.FindIndex(t => t.Id == template.Id);
                if (existingIndex >= 0)
                {
                    templates[existingIndex] = template;
                    _logger.LogInformation("Updated existing enhanced template: {TemplateName}", template.Name);
                }
                else
                {
                    templates.Add(template);
                    _logger.LogInformation("Added new enhanced template: {TemplateName}", template.Name);
                }

                await _templateStorage.SaveAsync("enhanced-sheet-templates", templates);

                _logger.LogInformation("Saved enhanced sheet template: {TemplateName} for {DataType}", template.Name, template.DataType);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving enhanced sheet template: {TemplateName}", template.Name);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> DeleteTemplateAsync(string templateId)
        {
            try
            {
                await _lock.WaitAsync();

                var templates = await _templateStorage.LoadAsync("enhanced-sheet-templates") ?? new List<EnhancedSheetTemplate>();
                var template = templates.FirstOrDefault(t => t.Id == templateId);

                if (template != null)
                {
                    // Soft delete - mark as inactive
                    template.IsActive = false;
                    await _templateStorage.SaveAsync("enhanced-sheet-templates", templates);

                    _logger.LogInformation("Deleted enhanced sheet template: {TemplateId}", templateId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting enhanced sheet template: {TemplateId}", templateId);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<EnhancedSheetTemplate> CreateDefaultTemplateAsync<T>(string templateName)
        {
            var template = new EnhancedSheetTemplate
            {
                Name = templateName,
                DataType = typeof(T).Name,
                CreatedAt = DateTime.UtcNow,
                Version = 1,
                IsActive = true
            };

            // Auto-generate data columns from type properties
            var properties = typeof(T).GetProperties();
            var columnIndex = 0;

            foreach (var property in properties)
            {
                if (ShouldIncludeProperty(property))
                {
                    var column = new DataColumnTemplate
                    {
                        Index = columnIndex++,
                        Header = FormatPropertyName(property.Name),
                        DataField = property.Name,
                        Format = GetDefaultFormat(property.PropertyType),
                        Width = GetDefaultWidth(property.PropertyType),
                        IsVisible = true,
                        TextAlignment = GetDefaultAlignment(property.PropertyType)
                    };

                    template.DataColumns.Add(column);
                }
            }

            // Add default formula row for common calculations
            await AddDefaultFormulas(template);

            // Add default checkbox columns based on data type
            await AddDefaultCheckboxColumns(template);

            return template;
        }

        public async Task<List<CheckboxColumnTemplate>> GetReusableCheckboxTemplatesAsync()
        {
            try
            {
                var checkboxTemplates = await _checkboxStorage.LoadAsync("reusable-checkbox-templates") ?? new List<CheckboxColumnTemplate>();
                return checkboxTemplates.OrderBy(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving reusable checkbox templates");
                return new List<CheckboxColumnTemplate>();
            }
        }

        public async Task<bool> SaveCheckboxTemplateAsync(CheckboxColumnTemplate checkboxTemplate)
        {
            try
            {
                await _lock.WaitAsync();

                var checkboxTemplates = await _checkboxStorage.LoadAsync("reusable-checkbox-templates") ?? new List<CheckboxColumnTemplate>();

                // Remove existing with same name
                checkboxTemplates.RemoveAll(c => c.Name.Equals(checkboxTemplate.Name, StringComparison.OrdinalIgnoreCase));

                // Add updated template
                checkboxTemplates.Add(checkboxTemplate);

                await _checkboxStorage.SaveAsync("reusable-checkbox-templates", checkboxTemplates);

                _logger.LogInformation("Saved reusable checkbox template: {CheckboxName}", checkboxTemplate.Name);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving checkbox template: {CheckboxName}", checkboxTemplate.Name);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        #region Default Template Generation

        private async Task AddDefaultFormulas(EnhancedSheetTemplate template)
        {
            var formulaRow = new FormulaRowTemplate
            {
                RowIndex = 1,
                BackgroundColor = "#E8E8E8",
                IsBold = true,
                IsLocked = true
            };

            if (template.DataType.Contains("BankSlip", StringComparison.OrdinalIgnoreCase))
            {
                await AddBankSlipDefaultFormulas(template, formulaRow);
            }
            else if (template.DataType.Contains("Sales", StringComparison.OrdinalIgnoreCase))
            {
                await AddSalesDefaultFormulas(template, formulaRow);
            }
            else
            {
                await AddGenericDefaultFormulas(template, formulaRow);
            }

            if (formulaRow.ColumnFormulas.Any() || formulaRow.ColumnLabels.Any())
            {
                template.FormulaRows.Add(formulaRow);
            }
        }

        private async Task AddBankSlipDefaultFormulas(EnhancedSheetTemplate template, FormulaRowTemplate formulaRow)
        {
            // Find amount column
            var amountColumn = template.DataColumns.FirstOrDefault(c =>
                c.DataField.Equals("Amount", StringComparison.OrdinalIgnoreCase));

            if (amountColumn != null)
            {
                var amountColumnLetter = GetColumnLetter(amountColumn.Index);

                // Add total amount formula
                formulaRow.ColumnLabels[amountColumn.Index] = "Total:";
                formulaRow.ColumnFormulas[amountColumn.Index] = $"=SUM({amountColumnLetter}3:{amountColumnLetter})";
            }

            // Add record count in first column
            formulaRow.ColumnLabels[0] = "Count:";
            formulaRow.ColumnFormulas[0] = "=COUNTA(A3:A)";

            await Task.CompletedTask;
        }

        private async Task AddSalesDefaultFormulas(EnhancedSheetTemplate template, FormulaRowTemplate formulaRow)
        {
            // Find quantity and amount columns
            var quantityColumn = template.DataColumns.FirstOrDefault(c =>
                c.DataField.Equals("Quantity", StringComparison.OrdinalIgnoreCase));
            var amountColumn = template.DataColumns.FirstOrDefault(c =>
                c.DataField.Equals("Amount", StringComparison.OrdinalIgnoreCase));

            if (quantityColumn != null)
            {
                var quantityColumnLetter = GetColumnLetter(quantityColumn.Index);
                formulaRow.ColumnLabels[quantityColumn.Index] = "Total Qty:";
                formulaRow.ColumnFormulas[quantityColumn.Index] = $"=SUM({quantityColumnLetter}3:{quantityColumnLetter})";
            }

            if (amountColumn != null)
            {
                var amountColumnLetter = GetColumnLetter(amountColumn.Index);
                formulaRow.ColumnLabels[amountColumn.Index] = "Total Revenue:";
                formulaRow.ColumnFormulas[amountColumn.Index] = $"=SUM({amountColumnLetter}3:{amountColumnLetter})";
            }

            await Task.CompletedTask;
        }

        private async Task AddGenericDefaultFormulas(EnhancedSheetTemplate template, FormulaRowTemplate formulaRow)
        {
            // Add record count in first column
            formulaRow.ColumnLabels[0] = "Count:";
            formulaRow.ColumnFormulas[0] = "=COUNTA(A3:A)";

            await Task.CompletedTask;
        }

        private async Task AddDefaultCheckboxColumns(EnhancedSheetTemplate template)
        {
            if (template.DataType.Contains("BankSlip", StringComparison.OrdinalIgnoreCase))
            {
                // Add default checkboxes for bank slips
                template.CheckboxColumns.AddRange(new[]
                {
                    new CheckboxColumnTemplate
                    {
                        Index = 0,
                        Name = "Processed",
                        Description = "Mark as processed",
                        FormulaTemplate = "=SUMIF({CHECKBOX_COLUMN}:{CHECKBOX_COLUMN},TRUE,B:B)",
                        Type = CheckboxType.Manual,
                        Width = 100,
                        IsVisible = true,
                        BackgroundColor = "#E8F5E8"
                    },
                    new CheckboxColumnTemplate
                    {
                        Index = 1,
                        Name = "Verified",
                        Description = "Mark as verified",
                        FormulaTemplate = "=COUNTIF({CHECKBOX_COLUMN}:{CHECKBOX_COLUMN},TRUE)",
                        Type = CheckboxType.Manual,
                        Width = 100,
                        IsVisible = true,
                        BackgroundColor = "#F0F8FF"
                    }
                });
            }
            else if (template.DataType.Contains("Sales", StringComparison.OrdinalIgnoreCase))
            {
                // Add default checkboxes for sales
                template.CheckboxColumns.Add(new CheckboxColumnTemplate
                {
                    Index = 0,
                    Name = "Shipped",
                    Description = "Mark as shipped",
                    FormulaTemplate = "=SUMIF({CHECKBOX_COLUMN}:{CHECKBOX_COLUMN},TRUE,C:C)",
                    Type = CheckboxType.Manual,
                    Width = 100,
                    IsVisible = true,
                    BackgroundColor = "#E8F5E8"
                });
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Helper Methods

        private bool ValidateTemplate(EnhancedSheetTemplate template)
        {
            if (string.IsNullOrWhiteSpace(template.Name)) return false;
            if (string.IsNullOrWhiteSpace(template.DataType)) return false;
            if (!template.DataColumns.Any()) return false;

            // Validate data columns
            foreach (var column in template.DataColumns)
            {
                if (string.IsNullOrWhiteSpace(column.Header)) return false;
                if (string.IsNullOrWhiteSpace(column.DataField)) return false;
            }

            // Validate checkbox columns
            foreach (var checkbox in template.CheckboxColumns)
            {
                if (string.IsNullOrWhiteSpace(checkbox.Name)) return false;
            }

            return true;
        }

        private bool ShouldIncludeProperty(System.Reflection.PropertyInfo property)
        {
            // Skip complex types and internal properties
            var skipTypes = new[] { typeof(object), typeof(Dictionary<,>), typeof(List<>) };
            var skipNames = new[] { "Id", "Status", "ProcessedBy", "ProcessedAt" };

            return !skipTypes.Any(t => t.IsAssignableFrom(property.PropertyType)) &&
                   !skipNames.Contains(property.Name) &&
                   property.CanRead &&
                   !property.PropertyType.IsClass || property.PropertyType == typeof(string);
        }

        private string FormatPropertyName(string propertyName)
        {
            // Convert PascalCase to Title Case
            return System.Text.RegularExpressions.Regex.Replace(propertyName, "([A-Z])", " $1").Trim();
        }

        private string GetDefaultFormat(Type propertyType)
        {
            if (propertyType == typeof(decimal) || propertyType == typeof(decimal?))
                return "#,##0.00";
            if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
                return "yyyy-mm-dd";
            if (propertyType == typeof(int) || propertyType == typeof(int?))
                return "#,##0";
            return string.Empty;
        }

        private int GetDefaultWidth(Type propertyType)
        {
            if (propertyType == typeof(string))
                return 150;
            if (propertyType == typeof(decimal) || propertyType == typeof(decimal?))
                return 100;
            if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
                return 120;
            return 100;
        }

        private string GetDefaultAlignment(Type propertyType)
        {
            if (propertyType == typeof(decimal) || propertyType == typeof(decimal?) ||
                propertyType == typeof(int) || propertyType == typeof(int?))
                return "RIGHT";
            if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
                return "CENTER";
            return "LEFT";
        }

        private string GetColumnLetter(int columnIndex)
        {
            var letter = "";
            while (columnIndex >= 0)
            {
                letter = (char)('A' + (columnIndex % 26)) + letter;
                columnIndex = columnIndex / 26 - 1;
            }
            return letter;
        }

        #endregion
    }
}