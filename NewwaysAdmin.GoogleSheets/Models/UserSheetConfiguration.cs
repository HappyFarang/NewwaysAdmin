namespace NewwaysAdmin.GoogleSheets.Models
{
    /// <summary>
    /// User's complete configuration for a module's Google Sheet export
    /// Saved as JSON via NewwaysIOManager
    /// </summary>
    public class UserSheetConfiguration
    {
        public string ModuleName { get; set; } = string.Empty; // "BankSlips"
        public string ConfigurationName { get; set; } = "Default"; // User can save multiple configs
        public List<SelectedColumn> SelectedColumns { get; set; } = new();
        public List<CustomColumn> CustomColumns { get; set; } = new();
        public RowStructureSettings RowSettings { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Represents a selected column from the module's available columns
    /// Uses natural order from ModuleColumnRegistry
    /// </summary>
    public class SelectedColumn
    {
        public string PropertyName { get; set; } = string.Empty; // From ColumnDefinition
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Simple custom column - much simpler than before
    /// </summary>
    public class CustomColumn
    {
        public string Name { get; set; } = string.Empty; // "Gas" - this IS the ID
        public FormulaType FormulaType { get; set; } = FormulaType.SumIf;
        public DataType DataType { get; set; } = DataType.Currency;
        public string SumColumnName { get; set; } = string.Empty; // Which pre-defined column to sum (e.g., "Amount")
        public string? CustomFormula { get; set; } // Only if FormulaType.Custom
    }

    /// <summary>
    /// Simple formula types we support
    /// </summary>
    public enum FormulaType
    {
        Sum,     // =SUM(column#:DataStart,column#:DataEnd)
        SumIf,   // =SUMIF(TickColumn, TRUE, AmountColumn) - most common for expense categories
        Average, // =AVERAGE(column#:DataStart,column#:DataEnd)
        Count,   // =COUNT(column#:DataStart,column#:DataEnd)
        Custom   // User enters their own formula
    }

    /// <summary>
    /// Simple data types we support
    /// </summary>
    public enum DataType
    {
        Currency,
        Int,
        Float,
        Number, // General number format
        Text,   // Plain text
        // Add more types as nee
    }

    /// <summary>
    /// Row structure configuration
    /// 
    /// Sheet Structure (example):
    /// Row 1: [HEADER] - "Date | Amount | Account | Gas ✓ | Gas" (if UseHeaderRow = true)
    /// Row 2: [FORMULA] - "" | "" | "" | "" | "SUMIF(...)" (if UseFormulaRow = true)  
    /// Row 3+: [DATA] - Actual transaction data (dynamic count)
    /// Row N+1: [EMPTY] - Separator (if AddSummaryRowsAfterData = true)
    /// Row N+2: [SUMMARY] - "Total: | SUM(...) | | |" (if AddSummaryRowsAfterData = true)
    /// 
    /// Data start/end rows are calculated dynamically based on settings and data count
    /// </summary>
    public class RowStructureSettings
    {
        public bool UseHeaderRow { get; set; } = true;
        public bool UseFormulaRow { get; set; } = false; // Enable formula row
        public bool LockHeaderRow { get; set; } = true;
        public bool LockFormulaRow { get; set; } = true;
        public bool BoldHeaderRow { get; set; } = true;
        public bool AddSummaryRowsAfterData { get; set; } = true;
    }

    /// <summary>
    /// Reusable custom column templates that users can save/load
    /// Saved separately from UserSheetConfiguration
    /// </summary>
    public class CustomColumnLibrary
    {
        public string ModuleName { get; set; } = string.Empty; // "BankSlips"
        public List<CustomColumnTemplate> Templates { get; set; } = new();
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Template for creating custom columns - simplified
    /// </summary>
    public class CustomColumnTemplate
    {
        public string Name { get; set; } = string.Empty; // "Gas", "Labor", "Tools"
        public FormulaType FormulaType { get; set; } = FormulaType.SumIf;
        public DataType DataType { get; set; } = DataType.Currency;
        public string SumColumnName { get; set; } = string.Empty; // "Amount" for BankSlips
        public string? CustomFormula { get; set; } // Only if FormulaType.Custom
    }
}