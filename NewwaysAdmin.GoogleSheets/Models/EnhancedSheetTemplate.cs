// NewwaysAdmin.GoogleSheets/Models/EnhancedSheetTemplate.cs
using MessagePack;

namespace NewwaysAdmin.GoogleSheets.Models.Templates
{

    /// <summary>
    /// Enhanced sheet template with support for checkboxes, formulas, and advanced formatting
    /// </summary>
    [MessagePackObject]
    public class EnhancedSheetTemplate
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public string Name { get; set; } = string.Empty;

        [Key(2)]
        public string Description { get; set; } = string.Empty;

        [Key(3)]
        public string DataType { get; set; } = string.Empty; // "BankSlipData", "SalesData", etc.

        [Key(4)]
        public int Version { get; set; } = 1;

        [Key(5)]
        public bool IsActive { get; set; } = true;

        [Key(6)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Key(7)]
        public string CreatedBy { get; set; } = string.Empty;

        [Key(8)]
        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        [Key(9)]
        public string LastModifiedBy { get; set; } = string.Empty;

        // Main data columns (Row 1: Headers, Row 3+: Data)
        [Key(10)]
        public List<DataColumnTemplate> DataColumns { get; set; } = new();

        // Formula rows (typically Row 2)
        [Key(11)]
        public List<FormulaRowTemplate> FormulaRows { get; set; } = new();

        // Checkbox columns (to the right of data columns)
        [Key(12)]
        public List<CheckboxColumnTemplate> CheckboxColumns { get; set; } = new();

        // Sheet-level formatting options
        [Key(13)]
        public EnhancedSheetFormatting Formatting { get; set; } = new();

        // Metadata
        [Key(14)]
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Template for data columns (main export data)
    /// </summary>
    [MessagePackObject]
    public class DataColumnTemplate
    {
        [Key(0)]
        public int Index { get; set; }

        [Key(1)]
        public string Header { get; set; } = string.Empty;

        [Key(2)]
        public string DataField { get; set; } = string.Empty; // Property name like "Amount", "TransactionDate"

        [Key(3)]
        public string Format { get; set; } = string.Empty; // "#,##0.00", "yyyy-mm-dd"

        [Key(4)]
        public int Width { get; set; } = 120;

        [Key(5)]
        public bool IsVisible { get; set; } = true;

        [Key(6)]
        public string BackgroundColor { get; set; } = string.Empty;

        [Key(7)]
        public string FontColor { get; set; } = string.Empty;

        [Key(8)]
        public bool IsBold { get; set; } = false;

        [Key(9)]
        public bool IsItalic { get; set; } = false;

        [Key(10)]
        public string TextAlignment { get; set; } = "LEFT"; // LEFT, CENTER, RIGHT

        [Key(11)]
        public bool IsLocked { get; set; } = false;

        [Key(12)]
        public string ValidationRule { get; set; } = string.Empty; // For data validation
    }

    /// <summary>
    /// Template for formula rows (typically row 2)
    /// </summary>
    [MessagePackObject]
    public class FormulaRowTemplate
    {
        [Key(0)]
        public int RowIndex { get; set; } = 2; // Usually row 2

        [Key(1)]
        public string BackgroundColor { get; set; } = "#F0F0F0";

        [Key(2)]
        public string FontColor { get; set; } = "#000000";

        [Key(3)]
        public bool IsBold { get; set; } = true;

        [Key(4)]
        public bool IsLocked { get; set; } = true;

        [Key(5)]
        public int Height { get; set; } = 25;

        // Column index -> Formula (e.g., "=SUM(B3:B)" for sum of amount column)
        [Key(6)]
        public Dictionary<int, string> ColumnFormulas { get; set; } = new();

        // Column index -> Label (e.g., "Total:" in the cell before the formula)
        [Key(7)]
        public Dictionary<int, string> ColumnLabels { get; set; } = new();

        // Column index -> Custom formatting for this specific cell
        [Key(8)]
        public Dictionary<int, string> ColumnFormats { get; set; } = new();
    }

    /// <summary>
    /// Template for checkbox columns (interactive checkboxes)
    /// </summary>
    [MessagePackObject]
    public class CheckboxColumnTemplate
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public int Index { get; set; } // Position among checkbox columns

        [Key(2)]
        public string Name { get; set; } = string.Empty; // "Processed", "Verified", etc.

        [Key(3)]
        public string Description { get; set; } = string.Empty;

        [Key(4)]
        public CheckboxType Type { get; set; } = CheckboxType.Manual;

        [Key(5)]
        public int Width { get; set; } = 100;

        [Key(6)]
        public bool IsVisible { get; set; } = true;

        [Key(7)]
        public string BackgroundColor { get; set; } = "#F8F8F8";

        [Key(8)]
        public string CheckedColor { get; set; } = "#4CAF50";

        [Key(9)]
        public string UncheckedColor { get; set; } = "#FFFFFF";

        // Formula template for the formula row (Row 2)
        // Use placeholders like {CHECKBOX_COLUMN} for the checkbox column reference
        // Example: "=SUMIF({CHECKBOX_COLUMN}:{CHECKBOX_COLUMN},TRUE,B:B)"
        [Key(10)]
        public string FormulaTemplate { get; set; } = string.Empty;

        [Key(11)]
        public bool ShowFormulaResult { get; set; } = true;

        [Key(12)]
        public string FormulaResultFormat { get; set; } = string.Empty;

        // For reusable templates
        [Key(13)]
        public bool IsReusable { get; set; } = false;

        [Key(14)]
        public string Category { get; set; } = string.Empty; // "Financial", "Status", "Workflow", etc.

        [Key(15)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Key(16)]
        public string CreatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Enhanced formatting options for the entire sheet
    /// </summary>
    [MessagePackObject]
    public class EnhancedSheetFormatting
    {
        // Header row formatting (Row 1)
        [Key(0)]
        public bool FreezeHeaderRow { get; set; } = true;

        [Key(1)]
        public bool FreezeFormulaRow { get; set; } = true;

        [Key(2)]
        public string HeaderBackgroundColor { get; set; } = "#4472C4";

        [Key(3)]
        public string HeaderFontColor { get; set; } = "#FFFFFF";

        [Key(4)]
        public bool HeaderIsBold { get; set; } = true;

        [Key(5)]
        public int HeaderHeight { get; set; } = 30;

        // Data area formatting (Row 3+)
        [Key(6)]
        public bool AlternateRowColors { get; set; } = true;

        [Key(7)]
        public string AlternateColor { get; set; } = "#F8F9FA";

        [Key(8)]
        public bool AddAutoFilter { get; set; } = true;

        [Key(9)]
        public bool AddBorders { get; set; } = true;

        [Key(10)]
        public string BorderStyle { get; set; } = "SOLID";

        [Key(11)]
        public string BorderColor { get; set; } = "#E0E0E0";

        // Sheet protection
        [Key(12)]
        public bool ProtectSheet { get; set; } = false;

        [Key(13)]
        public bool AllowSort { get; set; } = true;

        [Key(14)]
        public bool AllowFilter { get; set; } = true;

        [Key(15)]
        public bool AllowEditCheckboxes { get; set; } = true;

        [Key(16)]
        public bool AllowEditData { get; set; } = false;

        // Advanced options
        [Key(17)]
        public bool ShowGridlines { get; set; } = true;

        [Key(18)]
        public int DefaultRowHeight { get; set; } = 21;

        [Key(19)]
        public string SheetTabColor { get; set; } = string.Empty;

        [Key(20)]
        public bool HideFormulaBar { get; set; } = false;
    }

    /// <summary>
    /// Types of checkbox interactions
    /// </summary>
    public enum CheckboxType
    {
        Manual = 0,      // User manually checks/unchecks
        Calculated = 1,  // Auto-checked based on formula
        ReadOnly = 2     // Display only, cannot be changed
    }

    /// <summary>
    /// Represents the complete layout for export generation
    /// </summary>
    [MessagePackObject]
    public class TemplateLayout
    {
        [Key(0)]
        public string TemplateId { get; set; } = string.Empty;

        [Key(1)]
        public string SheetTitle { get; set; } = string.Empty;

        [Key(2)]
        public int DataStartRow { get; set; } = 3; // Row where data starts

        [Key(3)]
        public int DataStartColumn { get; set; } = 1; // Column where data starts (A = 1)

        [Key(4)]
        public int CheckboxStartColumn { get; set; } = 0; // Calculated based on data columns

        [Key(5)]
        public List<string> ColumnLetters { get; set; } = new(); // A, B, C, etc.

        [Key(6)]
        public Dictionary<string, string> PlaceholderReplacements { get; set; } = new();

        [Key(7)]
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}