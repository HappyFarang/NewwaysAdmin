// NewwaysAdmin.WebAdmin/Services/UnifiedTemplateService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.WebAdmin.Components.Settings.GoogleSheets.Models;
using System.Text.Json;

namespace NewwaysAdmin.WebAdmin.Services
{
    public interface IUnifiedTemplateService
    {
        Task<List<GoogleSheetTemplate>> GetAllTemplatesAsync();
        Task<List<GoogleSheetTemplate>> GetTemplatesByTypeAsync(TemplateType templateType);
        Task<List<GoogleSheetTemplate>> GetTemplatesByDataTypeAsync(string dataType);
        Task<GoogleSheetTemplate?> GetTemplateAsync(string templateId);
        Task<bool> SaveTemplateAsync(GoogleSheetTemplate template);
        Task<bool> DeleteTemplateAsync(string templateId);
        Task<GoogleSheetTemplate> CreateDefaultTemplateAsync(TemplateType templateType, string templateName, string? dataType = null);
        Task<List<GoogleSheetTemplate>> SearchTemplatesAsync(string searchTerm);
        Task<bool> DuplicateTemplateAsync(string templateId, string newName);
    }

    public class UnifiedTemplateService : IUnifiedTemplateService
    {
        private readonly ILogger<UnifiedTemplateService> _logger;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly string _dataFilePath;

        public UnifiedTemplateService(ILogger<UnifiedTemplateService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Create data directory if it doesn't exist
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NewwaysAdmin", "Templates");
            Directory.CreateDirectory(dataDir);
            _dataFilePath = Path.Combine(dataDir, "google-sheet-templates.json");
        }

        public async Task<List<GoogleSheetTemplate>> GetAllTemplatesAsync()
        {
            try
            {
                await _lock.WaitAsync();

                if (!File.Exists(_dataFilePath))
                {
                    _logger.LogInformation("No templates file found, returning empty list");
                    return new List<GoogleSheetTemplate>();
                }

                var json = await File.ReadAllTextAsync(_dataFilePath);
                var templates = JsonSerializer.Deserialize<List<GoogleSheetTemplate>>(json) ?? new List<GoogleSheetTemplate>();

                _logger.LogInformation($"Loaded {templates.Count} Google Sheets templates");
                return templates.OrderBy(t => t.Name).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading Google Sheets templates");
                return new List<GoogleSheetTemplate>();
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<List<GoogleSheetTemplate>> GetTemplatesByTypeAsync(TemplateType templateType)
        {
            var allTemplates = await GetAllTemplatesAsync();
            return allTemplates.Where(t => t.Type == templateType).ToList();
        }

        public async Task<List<GoogleSheetTemplate>> GetTemplatesByDataTypeAsync(string dataType)
        {
            var allTemplates = await GetAllTemplatesAsync();
            return allTemplates.Where(t => t.DataType.Equals(dataType, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public async Task<GoogleSheetTemplate?> GetTemplateAsync(string templateId)
        {
            var templates = await GetAllTemplatesAsync();
            return templates.FirstOrDefault(t => t.Id == templateId);
        }

        public async Task<bool> SaveTemplateAsync(GoogleSheetTemplate template)
        {
            try
            {
                await _lock.WaitAsync();

                var templates = await GetAllTemplatesAsync();
                var existingIndex = templates.FindIndex(t => t.Id == template.Id);

                if (existingIndex >= 0)
                {
                    // Update existing template
                    template.LastModified = DateTime.UtcNow;
                    templates[existingIndex] = template;
                    _logger.LogInformation($"Updated template: {template.Name} (ID: {template.Id})");
                }
                else
                {
                    // Add new template
                    if (string.IsNullOrEmpty(template.Id))
                    {
                        template.Id = Guid.NewGuid().ToString();
                    }
                    template.CreatedDate = DateTime.UtcNow;
                    template.LastModified = DateTime.UtcNow;
                    templates.Add(template);
                    _logger.LogInformation($"Created new template: {template.Name} (ID: {template.Id})");
                }

                var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_dataFilePath, json);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error saving template: {template?.Name}");
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

                var templates = await GetAllTemplatesAsync();
                var templateToDelete = templates.FirstOrDefault(t => t.Id == templateId);
                if (templateToDelete == null)
                {
                    _logger.LogWarning($"Template not found for deletion: {templateId}");
                    return false;
                }

                templates.Remove(templateToDelete);
                var json = JsonSerializer.Serialize(templates, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_dataFilePath, json);

                _logger.LogInformation($"Deleted template: {templateToDelete.Name} (ID: {templateId})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting template: {templateId}");
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<GoogleSheetTemplate> CreateDefaultTemplateAsync(TemplateType templateType, string templateName, string? dataType = null)
        {
            var template = new GoogleSheetTemplate
            {
                Id = Guid.NewGuid().ToString(),
                Name = templateName,
                Type = templateType,
                CreatedDate = DateTime.UtcNow,
                LastModified = DateTime.UtcNow,
                Version = 1,
                IsActive = true,
                Metadata = new Dictionary<string, string>()
            };

            if (templateType == TemplateType.Basic)
            {
                template.Description = "Basic template for simple data export";
                template.Columns = new List<ColumnDefinition>
                {
                    new() { Header = "Date", DataType = "Date", Format = "yyyy-mm-dd" },
                    new() { Header = "Description", DataType = "Text", Format = "Default" },
                    new() { Header = "Amount", DataType = "Currency", Format = "$#,##0.00" }
                };
                template.Formulas = new List<FormulaDefinition>();
            }
            else // Enhanced
            {
                template.Description = "Enhanced template with checkboxes, formulas, and advanced formatting";
                template.DataType = dataType ?? "Other";
                template.DataColumns = new List<DataColumnTemplate>
                {
                    new() { Index = 0, Header = "Date", DataField = "Date", Width = 120, TextAlignment = "CENTER" },
                    new() { Index = 1, Header = "Description", DataField = "Description", Width = 200, TextAlignment = "LEFT" },
                    new() { Index = 2, Header = "Amount", DataField = "Amount", Width = 100, Format = "#,##0.00", TextAlignment = "RIGHT" }
                };
                template.CheckboxColumns = new List<CheckboxColumnTemplate>
                {
                    new() { Index = 0, Name = "Processed", Type = CheckboxType.Manual, Width = 100 },
                    new() { Index = 1, Name = "Verified", Type = CheckboxType.Manual, Width = 100 }
                };
                template.FormulaRows = new List<FormulaRowTemplate>
                {
                    new()
                    {
                        RowIndex = 2,
                        BackgroundColor = "#F0F0F0",
                        FontColor = "#000000",
                        IsBold = true,
                        ColumnLabels = new Dictionary<int, string> { { 2, "Total:" } },
                        ColumnFormulas = new Dictionary<int, string> { { 2, "=SUM(C3:C)" } }
                    }
                };
                template.Formatting = new TemplateFormatting
                {
                    FreezeHeaderRow = true,
                    HeaderBackgroundColor = "#4472C4",
                    HeaderFontColor = "#FFFFFF",
                    HeaderIsBold = true,
                    AlternateRowColors = true,
                    AddAutoFilter = true,
                    AddBorders = true
                };
            }

            return template;
        }

        public async Task<List<GoogleSheetTemplate>> SearchTemplatesAsync(string searchTerm)
        {
            var allTemplates = await GetAllTemplatesAsync();

            if (string.IsNullOrWhiteSpace(searchTerm))
                return allTemplates;

            var normalizedSearchTerm = searchTerm.ToLowerInvariant();

            return allTemplates.Where(t =>
                t.Name.ToLowerInvariant().Contains(normalizedSearchTerm) ||
                t.Description.ToLowerInvariant().Contains(normalizedSearchTerm) ||
                t.DataType.ToLowerInvariant().Contains(normalizedSearchTerm)
            ).ToList();
        }

        public async Task<bool> DuplicateTemplateAsync(string templateId, string newName)
        {
            try
            {
                var originalTemplate = await GetTemplateAsync(templateId);
                if (originalTemplate == null)
                {
                    _logger.LogWarning($"Template not found for duplication: {templateId}");
                    return false;
                }

                // Create a deep clone
                var duplicatedTemplate = CloneTemplate(originalTemplate);
                duplicatedTemplate.Id = Guid.NewGuid().ToString();
                duplicatedTemplate.Name = newName;
                duplicatedTemplate.CreatedDate = DateTime.UtcNow;
                duplicatedTemplate.LastModified = DateTime.UtcNow;

                return await SaveTemplateAsync(duplicatedTemplate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error duplicating template: {templateId}");
                return false;
            }
        }

        private GoogleSheetTemplate CloneTemplate(GoogleSheetTemplate source)
        {
            // Simple deep clone using JSON serialization
            var json = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<GoogleSheetTemplate>(json) ?? new GoogleSheetTemplate();
        }
    }
}