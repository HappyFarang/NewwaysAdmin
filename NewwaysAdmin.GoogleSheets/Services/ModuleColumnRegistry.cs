// NewwaysAdmin.GoogleSheets/Services/ModuleColumnRegistry.cs
namespace NewwaysAdmin.GoogleSheets.Services
{
    /// <summary>
    /// Registry for module column definitions - completely generic approach
    /// </summary>
    public class ModuleColumnRegistry
    {
        private static readonly Dictionary<string, List<ColumnDefinition>> _moduleColumns = new()
        {
            // Bank Slips module columns
            {
                "BankSlips", new List<ColumnDefinition>
                {
                    new() { PropertyName = "TransactionDate", DisplayName = "Date", DataType = "Date", Format = "yyyy-mm-dd", Width = 120, CanSum = false },
                    new() { PropertyName = "Amount", DisplayName = "Amount (฿)", DataType = "Currency", Format = "#,##0.00", Width = 100, CanSum = true },
                    new() { PropertyName = "AccountName", DisplayName = "Account Name", DataType = "Text", Format = "Default", Width = 200, CanSum = false },
                    new() { PropertyName = "AccountNumber", DisplayName = "Account Number", DataType = "Text", Format = "Default", Width = 150, CanSum = false },
                    new() { PropertyName = "ReceiverName", DisplayName = "Receiver Name", DataType = "Text", Format = "Default", Width = 200, CanSum = false },
                    new() { PropertyName = "ReceiverAccount", DisplayName = "Receiver Account", DataType = "Text", Format = "Default", Width = 150, CanSum = false },
                    new() { PropertyName = "Note", DisplayName = "Note", DataType = "Text", Format = "Default", Width = 300, CanSum = false },
                    new() { PropertyName = "SlipCollectionName", DisplayName = "Collection", DataType = "Text", Format = "Default", Width = 150, CanSum = false },
                    new() { PropertyName = "ProcessedBy", DisplayName = "Processed By", DataType = "Text", Format = "Default", Width = 120, CanSum = false },
                    new() { PropertyName = "ProcessedAt", DisplayName = "Processed At", DataType = "Date", Format = "yyyy-mm-dd hh:mm", Width = 150, CanSum = false },
                    new() { PropertyName = "OriginalFilePath", DisplayName = "Original File", DataType = "Text", Format = "Default", Width = 200, CanSum = false },
                    new() { PropertyName = "Status", DisplayName = "Status", DataType = "Text", Format = "Default", Width = 100, CanSum = false },
                    new() { PropertyName = "ErrorReason", DisplayName = "Error Reason", DataType = "Text", Format = "Default", Width = 250, CanSum = false },
                    new() { PropertyName = "Id", DisplayName = "ID", DataType = "Text", Format = "Default", Width = 100, CanSum = false }
                }
            }

            // Future modules can be added here like:
            // { "Sales", GetSalesColumns() },
            // { "Orders", GetOrderColumns() },
            // { "Inventory", GetInventoryColumns() }
        };

        /// <summary>
        /// Get available columns for a specific module
        /// </summary>
        public List<ColumnDefinition> GetModuleColumns(string moduleName)
        {
            return _moduleColumns.TryGetValue(moduleName, out var columns)
                ? new List<ColumnDefinition>(columns) // Return a copy
                : new List<ColumnDefinition>();
        }

        /// <summary>
        /// Get list of all available modules
        /// </summary>
        public List<string> GetAvailableModules()
        {
            return _moduleColumns.Keys.ToList();
        }

        /// <summary>
        /// Check if a module exists
        /// </summary>
        public bool HasModule(string moduleName)
        {
            return _moduleColumns.ContainsKey(moduleName);
        }

        /// <summary>
        /// Add or update columns for a module (for future dynamic loading)
        /// </summary>
        public void RegisterModule(string moduleName, List<ColumnDefinition> columns)
        {
            _moduleColumns[moduleName] = columns;
        }
    }

    /// <summary>
    /// Represents a column definition for any module
    /// </summary>
    public class ColumnDefinition
    {
        public string PropertyName { get; set; } = string.Empty;  // The actual property name (TransactionDate)
        public string DisplayName { get; set; } = string.Empty;   // User-friendly name (Date)
        public string DataType { get; set; } = string.Empty;      // Text, Date, Currency, Number
        public string Format { get; set; } = string.Empty;        // #,##0.00, yyyy-mm-dd, Default
        public int Width { get; set; } = 150;                     // Column width in pixels
        public bool CanSum { get; set; } = false;                 // Quick flag: can be used in SUM formulas (most common)
        public string Formula { get; set; } = string.Empty;       // Any custom formula: SUM, AVERAGE, COUNT, MAX, MIN, etc.
        public bool IsSelected { get; set; } = false;             // For UI binding - is this column selected?
    }

    /// <summary>
    /// Helper class for generating column letters (A, B, C, ... Z, AA, AB, etc.)
    /// </summary>
    public static class ColumnLetterHelper
    {
        /// <summary>
        /// Convert column index (0-based) to Excel column letter
        /// 0 = A, 1 = B, 25 = Z, 26 = AA, etc.
        /// </summary>
        public static string GetColumnLetter(int columnIndex)
        {
            string columnName = "";
            while (columnIndex >= 0)
            {
                columnName = (char)('A' + columnIndex % 26) + columnName;
                columnIndex = columnIndex / 26 - 1;
            }
            return columnName;
        }

        /// <summary>
        /// Get the next available column letter after the data columns
        /// </summary>
        public static string GetNextAvailableColumn(int lastDataColumnIndex)
        {
            return GetColumnLetter(lastDataColumnIndex + 1);
        }
    }
}