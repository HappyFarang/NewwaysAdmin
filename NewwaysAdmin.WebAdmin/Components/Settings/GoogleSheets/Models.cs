namespace NewwaysAdmin.WebAdmin.Components.Settings.GoogleSheets.Models
{
    public enum TemplateType
    {
        Basic,
        Enhanced
    }

    public enum DisplayMode
    {
        Welcome,
        List,
        Designer
    }

    /// <summary>
    /// Unified Google Sheets template that can handle both basic and enhanced features
    /// </summary>
    public class GoogleSheetTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public TemplateType Type { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastModified { get; set; }

        // Basic template properties
        public List<ColumnDefinition> Columns { get; set; } = new();
        public List<FormulaDefinition> Formulas { get; set; } = new();

        // Enhanced template properties (only used when Type = Enhanced)
        public string DataType { get; set; } = string.Empty; // "BankSlipData", "SalesData", etc.
        public List<DataColumnTemplate> DataColumns { get; set; } = new();
        public List<FormulaRowTemplate> FormulaRows { get; set; } = new();
        public List<CheckboxColumnTemplate> CheckboxColumns { get; set; } = new();
        public TemplateFormatting Formatting { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();

        // Version and tracking
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public string LastModifiedBy { get; set; } = string.Empty;
    }

    // Basic template models
    public class ColumnDefinition
    {
        public string Header { get; set; } = string.Empty;
        public string DataType { get; set; } = "Text";
        public string Format { get; set; } = "Default";
        public bool IsRequired { get; set; } = false;
        public string ValidationRule { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        public bool AllowEdit { get; set; } = true;
    }

    public class FormulaDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;
        public string TargetColumn { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    // Enhanced template models (moved from Features to Settings)
    public class DataColumnTemplate
    {
        public int Index { get; set; }
        public string Header { get; set; } = string.Empty;
        public string DataField { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int Width { get; set; } = 120;
        public bool IsVisible { get; set; } = true;
        public string BackgroundColor { get; set; } = string.Empty;
        public string FontColor { get; set; } = string.Empty;
        public bool IsBold { get; set; } = false;
        public bool IsItalic { get; set; } = false;
        public string TextAlignment { get; set; } = "LEFT";
        public bool IsLocked { get; set; } = false;
        public string ValidationRule { get; set; } = string.Empty;
    }

    public class FormulaRowTemplate
    {
        public int RowIndex { get; set; } = 2;
        public string BackgroundColor { get; set; } = "#F0F0F0";
        public string FontColor { get; set; } = "#000000";
        public bool IsBold { get; set; } = true;
        public bool IsLocked { get; set; } = true;
        public int Height { get; set; } = 25;
        public Dictionary<int, string> ColumnFormulas { get; set; } = new();
        public Dictionary<int, string> ColumnLabels { get; set; } = new();
        public Dictionary<int, string> ColumnFormats { get; set; } = new();
    }

    public class CheckboxColumnTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CheckboxType Type { get; set; } = CheckboxType.Manual;
        public int Width { get; set; } = 100;
        public bool IsVisible { get; set; } = true;
        public string BackgroundColor { get; set; } = "#F8F8F8";
        public string CheckedColor { get; set; } = "#4CAF50";
        public string UncheckedColor { get; set; } = "#FFFFFF";
        public string FormulaTemplate { get; set; } = string.Empty;
        public bool ShowFormulaResult { get; set; } = true;
        public string FormulaResultFormat { get; set; } = string.Empty;
        public bool IsReusable { get; set; } = false;
        public string Category { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
    }

    public class TemplateFormatting
    {
        // Header row formatting
        public bool FreezeHeaderRow { get; set; } = true;
        public bool FreezeFormulaRow { get; set; } = true;
        public string HeaderBackgroundColor { get; set; } = "#4472C4";
        public string HeaderFontColor { get; set; } = "#FFFFFF";
        public bool HeaderIsBold { get; set; } = true;
        public int HeaderHeight { get; set; } = 30;

        // Data area formatting
        public bool AlternateRowColors { get; set; } = true;
        public string AlternateColor { get; set; } = "#F8F9FA";
        public bool AddAutoFilter { get; set; } = true;
        public bool AddBorders { get; set; } = true;
        public string BorderStyle { get; set; } = "SOLID";
        public string BorderColor { get; set; } = "#E0E0E0";

        // Sheet protection
        public bool ProtectSheet { get; set; } = false;
        public bool AllowSort { get; set; } = true;
        public bool AllowFilter { get; set; } = true;
        public bool AllowEditCheckboxes { get; set; } = true;
        public bool AllowEditData { get; set; } = false;

        // Advanced options
        public bool ShowGridlines { get; set; } = true;
        public int DefaultRowHeight { get; set; } = 21;
        public string SheetTabColor { get; set; } = string.Empty;
        public bool HideFormulaBar { get; set; } = false;
    }

    public enum CheckboxType
    {
        Manual = 0,      // User manually checks/unchecks
        Calculated = 1,  // Auto-checked based on formula
        ReadOnly = 2     // Display only, cannot be changed
    }
}