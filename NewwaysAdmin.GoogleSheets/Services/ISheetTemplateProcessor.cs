// NewwaysAdmin.GoogleSheets/Services/ISheetTemplateProcessor.cs
using NewwaysAdmin.GoogleSheets.Interfaces;
using NewwaysAdmin.GoogleSheets.Models;

namespace NewwaysAdmin.GoogleSheets.Services
{
    /// <summary>
    /// Main handler for processing sheet templates - used both in settings and export
    /// </summary>
    public interface ISheetTemplateProcessor
    {
        /// <summary>
        /// Build a sheet from template and data (main export function)
        /// </summary>
        Task<SheetData> BuildSheetAsync<T>(
            SheetTemplate template,
            IEnumerable<T> data,
            ISheetLayout<T> layout,
            string? customTitle = null);

        /// <summary>
        /// Preview what a template will look like (for template designer)
        /// </summary>
        SheetData PreviewTemplate(SheetTemplate template, int sampleDataCount = 5);

        /// <summary>
        /// Validate a template for issues
        /// </summary>
        TemplateValidationResult ValidateTemplate(SheetTemplate template);

        /// <summary>
        /// Get suggested formulas for a column type
        /// </summary>
        List<FormulaSuggestion> GetFormulaSuggestions(string columnDataType, string columnHeader);

        /// <summary>
        /// Create a default template for a data type
        /// </summary>
        SheetTemplate CreateDefaultTemplate(string dataType, List<ColumnTemplate> columns);
    }

    /// <summary>
    /// Template management service (CRUD operations)
    /// </summary>
    public interface ITemplateService
    {
        Task<SheetTemplate?> GetTemplateAsync(string id);
        Task<List<SheetTemplate>> GetTemplatesAsync(string? dataType = null);
        Task<string> SaveTemplateAsync(SheetTemplate template);
        Task<bool> DeleteTemplateAsync(string id);
        Task<SheetTemplate> CloneTemplateAsync(string id, string newName);
    }

    // Supporting classes for the template system
    public class FormulaSuggestion
    {
        public string Formula { get; }
        public string Name { get; }
        public string Description { get; }

        public FormulaSuggestion(string formula, string name, string description = "")
        {
            Formula = formula;
            Name = name;
            Description = description;
        }

        public override bool Equals(object? obj)
        {
            return obj is FormulaSuggestion other && Formula == other.Formula;
        }

        public override int GetHashCode()
        {
            return Formula.GetHashCode();
        }
    }

    public class TemplateValidationResult
    {
        public bool IsValid { get; set; } = true;
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();

        public void AddError(string error)
        {
            Errors.Add(error);
            IsValid = false;
        }

        public void AddWarning(string warning)
        {
            Warnings.Add(warning);
        }
    }
}