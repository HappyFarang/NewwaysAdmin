// NewwaysAdmin.GoogleSheets/Models/SheetTemplate.cs - Corrected version
using MessagePack;

namespace NewwaysAdmin.GoogleSheets.Models
{
    [MessagePackObject]
    public class SheetTemplate
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
        public List<ColumnTemplate> Columns { get; set; } = new();

        [Key(5)]
        public List<FormulaTemplate> Formulas { get; set; } = new();

        [Key(6)]
        public SheetFormatting Formatting { get; set; } = new();

        [Key(7)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Key(8)]
        public string CreatedBy { get; set; } = string.Empty;

        // NEW: Row template system
        [Key(9)]
        public List<RowTemplate> RowTemplates { get; set; } = new();

        [Key(10)]
        public int Version { get; set; } = 1; // For template versioning

        // Helper methods for row management
        public List<RowTemplate> GetOrderedRows() =>
            RowTemplates.Where(r => r.IsVisible).OrderBy(r => r.Order).ToList();

        public List<RowTemplate> GetRowsByType(RowType type) =>
            RowTemplates.Where(r => r.Type == type && r.IsVisible).OrderBy(r => r.Order).ToList();

        public int CalculateDataStartRow()
        {
            var orderedRows = GetOrderedRows();
            var dataRowIndex = orderedRows.FindIndex(r => r.Type == RowType.Data);
            return dataRowIndex == -1 ? 1 : dataRowIndex + 1; // 1-based row indexing
        }

        public (int startRow, int endRow) CalculateDataRange(int dataRecordCount)
        {
            var startRow = CalculateDataStartRow();
            var endRow = startRow + dataRecordCount - 1;
            return (startRow, endRow);
        }
    }

    // NEW: Row template system
    public enum RowType
    {
        Header = 1,      // Column headers
        Formula = 2,     // Formula calculations
        Data = 3,        // Actual data records
        Summary = 4,     // Summary information
        Separator = 5,   // Empty spacing row
        Image = 6,       // Future: Image placeholders
        Chart = 7        // Future: Chart placeholders
    }

    [MessagePackObject]
    public class RowTemplate
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public RowType Type { get; set; }

        [Key(2)]
        public int Order { get; set; } // Position in sheet (1-based)

        [Key(3)]
        public string Name { get; set; } = string.Empty; // "Headers", "Summary Totals", etc.

        [Key(4)]
        public bool IsVisible { get; set; } = true;

        [Key(5)]
        public string BackgroundColor { get; set; } = string.Empty;

        [Key(6)]
        public bool IsBold { get; set; } = false;

        [Key(7)]
        public int Height { get; set; } = 21; // Row height in pixels

        [Key(8)]
        public bool IsProtected { get; set; } = false;

        // Type-specific settings stored as JSON strings
        [Key(9)]
        public Dictionary<string, string> Settings { get; set; } = new();
    }

    // Enhanced FormulaTemplate with dynamic range support
    [MessagePackObject]
    public class FormulaTemplate
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty; // "Total Amount", "Record Count"

        [Key(1)]
        public string Formula { get; set; } = string.Empty; // "=SUM({DATA_RANGE})", "=COUNTA({DATA_RANGE})"

        [Key(2)]
        public int Row { get; set; } // Specific row (for legacy support)

        [Key(3)]
        public int Column { get; set; } // Column index (0-based)

        [Key(4)]
        public string Format { get; set; } = string.Empty;

        [Key(5)]
        public bool IsBold { get; set; } = true;

        // NEW: Dynamic range configuration
        [Key(6)]
        public string RowTemplateId { get; set; } = string.Empty; // Which row template this belongs to

        [Key(7)]
        public RangeType RangeType { get; set; } = RangeType.DataRowsOnly;

        [Key(8)]
        public string ColumnHeader { get; set; } = string.Empty; // Target column by header name

        [Key(9)]
        public string Description { get; set; } = string.Empty; // What this formula does
    }

    // NEW: Range type for dynamic formula calculation
    public enum RangeType
    {
        DataRowsOnly = 1,     // Only include data rows (recommended)
        EntireColumn = 2,     // Use entire column (B:B) 
        AllRowsBelow = 3,     // Include all rows from formula row down
        CustomRange = 4,      // User provides exact range
        VisibleDataOnly = 5   // Only visible/filtered data
    }

    // Settings classes for different row types
    [MessagePackObject]
    public class HeaderRowSettings
    {
        [Key(0)]
        public bool UseTemplateHeaders { get; set; } = true;

        [Key(1)]
        public Dictionary<string, string> CustomHeaders { get; set; } = new(); // Column index -> custom header

        [Key(2)]
        public bool FreezeRow { get; set; } = true;

        [Key(3)]
        public bool AddAutoFilter { get; set; } = true;
    }

    [MessagePackObject]
    public class FormulaRowSettings
    {
        [Key(0)]
        public List<string> FormulaTemplateIds { get; set; } = new(); // Which formulas to include

        [Key(1)]
        public bool ShowFormulaDescriptions { get; set; } = false; // Show what formulas do

        [Key(2)]
        public string DefaultRangeType { get; set; } = nameof(RangeType.DataRowsOnly);

        [Key(3)]
        public bool ProtectFormulas { get; set; } = true;
    }

    [MessagePackObject]
    public class DataRowSettings
    {
        [Key(0)]
        public string DataSourceType { get; set; } = string.Empty; // "BankSlipData", etc.

        [Key(1)]
        public bool AllowEditing { get; set; } = false;

        [Key(2)]
        public bool AlternateRowColors { get; set; } = true;

        [Key(3)]
        public string AlternateColor { get; set; } = "#F8F9FA";

        [Key(4)]
        public int MaxRows { get; set; } = -1; // -1 = unlimited
    }

    [MessagePackObject]
    public class SummaryRowSettings
    {
        [Key(0)]
        public bool ShowRecordCount { get; set; } = true;

        [Key(1)]
        public bool ShowExportTimestamp { get; set; } = true;

        [Key(2)]
        public bool ShowDataRange { get; set; } = false;

        [Key(3)]
        public string CustomText { get; set; } = string.Empty;
    }

    // Enhanced ColumnTemplate (keeping existing structure)
    [MessagePackObject]
    public class ColumnTemplate
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
        public int Width { get; set; } = 100;

        [Key(5)]
        public bool IsVisible { get; set; } = true;

        [Key(6)]
        public string BackgroundColor { get; set; } = string.Empty;

        [Key(7)]
        public bool IsBold { get; set; } = false;

        // NEW: Column data type for auto-formula suggestions
        [Key(8)]
        public string DataType { get; set; } = "Text"; // Text, Number, Currency, Date, Checkbox

        [Key(9)]
        public bool IsProtected { get; set; } = false;
    }

    // Enhanced SheetFormatting (keeping existing + new features)
    [MessagePackObject]
    public class SheetFormatting
    {
        [Key(0)]
        public bool FreezeHeaderRow { get; set; } = true;

        [Key(1)]
        public bool AutoFilter { get; set; } = true;

        [Key(2)]
        public string HeaderBackgroundColor { get; set; } = "#E8E8E8";

        [Key(3)]
        public bool AlternateRowColors { get; set; } = true; // FIXED: Corrected syntax

        [Key(4)]
        public string AlternateColor { get; set; } = "#F8F8F8";

        [Key(5)]
        public bool AddSummarySection { get; set; } = true;

        [Key(6)]
        public int SummaryStartRow { get; set; } = -1; // -1 means calculate automatically

        // NEW: Row-based formatting options
        [Key(7)]
        public bool UseRowTemplates { get; set; } = false; // Enable new row template system

        [Key(8)]
        public bool AutoResizeColumns { get; set; } = true;

        [Key(9)]
        public bool ShowGridlines { get; set; } = true;

        [Key(10)]
        public bool ProtectSheet { get; set; } = false;

        [Key(11)]
        public string SheetPassword { get; set; } = string.Empty;
    }
}

// Helper extension methods for working with templates
namespace NewwaysAdmin.GoogleSheets.Models.Extensions
{
    public static class SheetTemplateExtensions
    {
        public static T? GetRowSettings<T>(this RowTemplate rowTemplate) where T : class
        {
            var settingsKey = typeof(T).Name;
            if (rowTemplate.Settings.ContainsKey(settingsKey))
            {
                var json = rowTemplate.Settings[settingsKey];
                return System.Text.Json.JsonSerializer.Deserialize<T>(json);
            }
            return null;
        }

        public static void SetRowSettings<T>(this RowTemplate rowTemplate, T settings) where T : class
        {
            var settingsKey = typeof(T).Name;
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            rowTemplate.Settings[settingsKey] = json;
        }

        // Resolve formula placeholders with actual data ranges
        public static string ResolveFormulaPlaceholders(
            this FormulaTemplate formula,
            string columnLetter,
            int dataStartRow,
            int dataEndRow,
            int totalRows)
        {
            var resolvedFormula = formula.Formula;

            // Replace range placeholders based on range type
            resolvedFormula = formula.RangeType switch
            {
                RangeType.DataRowsOnly => resolvedFormula
                    .Replace("{DATA_RANGE}", $"{columnLetter}{dataStartRow}:{columnLetter}{dataEndRow}")
                    .Replace("{RANGE}", $"{columnLetter}{dataStartRow}:{columnLetter}{dataEndRow}"),

                RangeType.EntireColumn => resolvedFormula
                    .Replace("{DATA_RANGE}", $"{columnLetter}:{columnLetter}")
                    .Replace("{RANGE}", $"{columnLetter}:{columnLetter}"),

                RangeType.AllRowsBelow => resolvedFormula
                    .Replace("{DATA_RANGE}", $"{columnLetter}{dataStartRow}:{columnLetter}")
                    .Replace("{RANGE}", $"{columnLetter}{dataStartRow}:{columnLetter}"),

                _ => resolvedFormula // CustomRange - user specifies exact range
            };

            // Replace other common placeholders
            resolvedFormula = resolvedFormula
                .Replace("{COLUMN}", columnLetter)
                .Replace("{DATA_START}", dataStartRow.ToString())
                .Replace("{DATA_END}", dataEndRow.ToString())
                .Replace("{TOTAL_ROWS}", totalRows.ToString());

            return resolvedFormula;
        }

        public static string GetColumnLetter(int columnIndex)
        {
            string columnLetter = "";
            while (columnIndex >= 0)
            {
                columnLetter = (char)('A' + columnIndex % 26) + columnLetter;
                columnIndex = columnIndex / 26 - 1;
            }
            return columnLetter;
        }

        // Create default row templates for a new sheet template
        public static void InitializeDefaultRowLayout(this SheetTemplate template)
        {
            if (template.RowTemplates.Any()) return; // Already initialized

            template.RowTemplates.AddRange(new[]
            {
                new RowTemplate
                {
                    Type = RowType.Header,
                    Order = 1,
                    Name = "Column Headers",
                    IsBold = true,
                    BackgroundColor = "#E8E8E8",
                    IsProtected = true
                },
                new RowTemplate
                {
                    Type = RowType.Data,
                    Order = 2,
                    Name = "Data Records",
                    BackgroundColor = ""
                }
            });

            // Enable row template system
            template.Formatting.UseRowTemplates = true;
        }

        // Add a formula row to the template
        public static RowTemplate AddFormulaRow(
            this SheetTemplate template,
            string name,
            int position = -1)
        {
            if (position == -1)
            {
                // Insert before data rows
                var dataRowOrder = template.GetRowsByType(RowType.Data).FirstOrDefault()?.Order ?? 2;
                position = dataRowOrder;

                // Shift existing rows down
                foreach (var row in template.RowTemplates.Where(r => r.Order >= position))
                {
                    row.Order++;
                }
            }

            var formulaRow = new RowTemplate
            {
                Type = RowType.Formula,
                Order = position,
                Name = name,
                IsBold = true,
                BackgroundColor = "#F0F8FF",
                IsProtected = true
            };

            // Initialize with default formula row settings
            formulaRow.SetRowSettings(new FormulaRowSettings
            {
                ProtectFormulas = true,
                ShowFormulaDescriptions = false
            });

            template.RowTemplates.Add(formulaRow);
            return formulaRow;
        }

        // Add formulas to a formula row
        public static void AddFormulasToRow(
            this SheetTemplate template,
            string rowTemplateId,
            Dictionary<string, string> columnFormulas,
            RangeType rangeType = RangeType.DataRowsOnly)
        {
            foreach (var (columnHeader, formulaText) in columnFormulas)
            {
                var columnIndex = template.Columns.FindIndex(c => c.Header == columnHeader);
                if (columnIndex == -1) continue;

                template.Formulas.Add(new FormulaTemplate
                {
                    Name = $"{columnHeader} {formulaText.Split('(')[0].Replace("=", "")}",
                    Formula = formulaText,
                    Column = columnIndex,
                    ColumnHeader = columnHeader,
                    RowTemplateId = rowTemplateId,
                    RangeType = rangeType,
                    IsBold = true
                });
            }
        }
    }
}