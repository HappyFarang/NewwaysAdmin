// NewwaysAdmin.GoogleSheets/Services/SheetTemplateService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.Shared.IO;
using System.Reflection;

namespace NewwaysAdmin.GoogleSheets.Services
{
    public interface ISheetTemplateService
    {
        Task<List<SheetTemplate>> GetTemplatesAsync(string? dataType = null);
        Task<SheetTemplate?> GetTemplateAsync(string templateId);
        Task<bool> SaveTemplateAsync(SheetTemplate template);
        Task<bool> DeleteTemplateAsync(string templateId);
        Task<SheetData> ApplyTemplateAsync<T>(SheetTemplate template, IEnumerable<T> data, DateTime? startDate = null, DateTime? endDate = null);
    }

    public class SheetTemplateService : ISheetTemplateService
    {
        private readonly IDataStorage<List<SheetTemplate>> _templateStorage;
        private readonly ILogger<SheetTemplateService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SheetTemplateService(
            IDataStorage<List<SheetTemplate>> templateStorage,
            ILogger<SheetTemplateService> logger)
        {
            _templateStorage = templateStorage ?? throw new ArgumentNullException(nameof(templateStorage));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<SheetTemplate>> GetTemplatesAsync(string? dataType = null)
        {
            try
            {
                await _lock.WaitAsync();

                var templates = await _templateStorage.LoadAsync("sheet-templates") ?? new List<SheetTemplate>();

                if (!string.IsNullOrEmpty(dataType))
                {
                    templates = templates.Where(t => t.DataType.Equals(dataType, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                return templates.OrderBy(t => t.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sheet templates for data type: {DataType}", dataType);
                return new List<SheetTemplate>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<SheetTemplate?> GetTemplateAsync(string templateId)
        {
            try
            {
                var templates = await GetTemplatesAsync();
                return templates.FirstOrDefault(t => t.Id == templateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sheet template: {TemplateId}", templateId);
                return null;
            }
        }

        public async Task<bool> SaveTemplateAsync(SheetTemplate template)
        {
            try
            {
                await _lock.WaitAsync();

                var templates = await _templateStorage.LoadAsync("sheet-templates") ?? new List<SheetTemplate>();

                // Remove existing template with same ID
                templates.RemoveAll(t => t.Id == template.Id);

                // Add updated template
                templates.Add(template);

                await _templateStorage.SaveAsync("sheet-templates", templates);

                _logger.LogInformation("Saved sheet template: {TemplateName} for {DataType}", template.Name, template.DataType);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving sheet template: {TemplateName}", template.Name);
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

                var templates = await _templateStorage.LoadAsync("sheet-templates") ?? new List<SheetTemplate>();
                var removed = templates.RemoveAll(t => t.Id == templateId);

                if (removed > 0)
                {
                    await _templateStorage.SaveAsync("sheet-templates", templates);
                    _logger.LogInformation("Deleted sheet template: {TemplateId}", templateId);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting sheet template: {TemplateId}", templateId);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<SheetData> ApplyTemplateAsync<T>(SheetTemplate template, IEnumerable<T> data, DateTime? startDate = null, DateTime? endDate = null)
        {
            try
            {
                var sheetData = new SheetData
                {
                    Title = GenerateSheetTitle(template, data.Cast<object>(), startDate, endDate)
                };

                // Add header row based on template
                var headerRow = new SheetRow { IsHeader = true };
                foreach (var column in template.Columns.Where(c => c.IsVisible).OrderBy(c => c.Index))
                {
                    var cell = new SheetCell
                    {
                        Value = column.Header,
                        BackgroundColor = template.Formatting.HeaderBackgroundColor,
                        IsBold = true
                    };
                    headerRow.Cells.Add(cell);
                }
                sheetData.Rows.Add(headerRow);

                // Add data rows
                var dataList = data.ToList();
                var properties = typeof(T).GetProperties();

                for (int rowIndex = 0; rowIndex < dataList.Count; rowIndex++)
                {
                    var dataRow = new SheetRow();
                    var item = dataList[rowIndex];

                    // Apply alternate row coloring
                    if (template.Formatting.AlternateRowColors && rowIndex % 2 == 1)
                    {
                        dataRow.BackgroundColor = template.Formatting.AlternateColor;
                    }

                    foreach (var column in template.Columns.Where(c => c.IsVisible).OrderBy(c => c.Index))
                    {
                        var property = properties.FirstOrDefault(p => p.Name.Equals(column.DataField, StringComparison.OrdinalIgnoreCase));
                        var value = property?.GetValue(item);

                        // Special handling for dates (convert Buddhist to Christian Era for BankSlips)
                        if (property?.PropertyType == typeof(DateTime) && column.DataField.Contains("Date", StringComparison.OrdinalIgnoreCase))
                        {
                            if (value is DateTime dateValue && dateValue.Year > 2500) // Buddhist calendar
                            {
                                value = dateValue.AddYears(-543);
                            }
                        }

                        var cell = new SheetCell
                        {
                            Value = value,
                            Format = column.Format,
                            BackgroundColor = column.BackgroundColor,
                            IsBold = column.IsBold
                        };
                        dataRow.Cells.Add(cell);
                    }

                    sheetData.Rows.Add(dataRow);
                }

                // Add formulas/summary section
                if (template.Formatting.AddSummarySection && template.Formulas.Any())
                {
                    await AddSummarySectionAsync(sheetData, template, dataList.Count);
                }

                // Add metadata
                sheetData.Metadata["TemplateId"] = template.Id;
                sheetData.Metadata["TemplateName"] = template.Name;
                sheetData.Metadata["ExportedAt"] = DateTime.UtcNow;
                sheetData.Metadata["RecordCount"] = dataList.Count;

                return sheetData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying template {TemplateName} to data", template.Name);
                throw;
            }
        }

        private async Task AddSummarySectionAsync(SheetData sheetData, SheetTemplate template, int dataRowCount)
        {
            // Add empty row before summary
            sheetData.Rows.Add(new SheetRow());

            var summaryStartRow = template.Formatting.SummaryStartRow > 0
                ? template.Formatting.SummaryStartRow
                : sheetData.Rows.Count + 1;

            foreach (var formula in template.Formulas.OrderBy(f => f.Row).ThenBy(f => f.Column))
            {
                // Create row if it doesn't exist
                while (sheetData.Rows.Count <= summaryStartRow + formula.Row)
                {
                    sheetData.Rows.Add(new SheetRow());
                }

                var row = sheetData.Rows[summaryStartRow + formula.Row];

                // Ensure enough cells in the row
                while (row.Cells.Count <= formula.Column)
                {
                    row.Cells.Add(new SheetCell());
                }

                // Add the formula
                row.Cells[formula.Column] = new SheetCell
                {
                    Formula = formula.Formula,
                    Format = formula.Format,
                    IsBold = formula.IsBold,
                    Type = CellType.Formula
                };

                // Add label in the previous column if it's a single formula
                if (formula.Column > 0 && string.IsNullOrEmpty(row.Cells[formula.Column - 1].Value?.ToString()))
                {
                    row.Cells[formula.Column - 1] = new SheetCell
                    {
                        Value = formula.Name + ":",
                        IsBold = true
                    };
                }
            }
        }

        private string GenerateSheetTitle(SheetTemplate template, IEnumerable<object> data, DateTime? startDate, DateTime? endDate)
        {
            var title = template.Name;

            if (startDate.HasValue && endDate.HasValue)
            {
                title += $" {startDate.Value:yyyy-MM-dd} to {endDate.Value:yyyy-MM-dd}";
            }

            var count = data.Count();
            if (count > 0)
            {
                title += $" ({count} records)";
            }

            return title;
        }
    }
}