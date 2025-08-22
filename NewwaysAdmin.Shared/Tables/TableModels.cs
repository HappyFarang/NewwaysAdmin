// NewwaysAdmin.Shared/Tables/TableModels.cs
// Enums and models for the generic DictionaryTable component

namespace NewwaysAdmin.Shared.Tables
{
    /// <summary>
    /// Defines how the table data should be laid out
    /// </summary>
    public enum TableLayout
    {
        /// <summary>
        /// Traditional column layout - headers on top, data flows vertically
        /// </summary>
        Vertical,

        /// <summary>
        /// Transposed row layout - headers on left, data flows horizontally  
        /// </summary>
        Horizontal
    }

    /// <summary>
    /// Configuration for custom table columns (checkboxes, buttons, etc.)
    /// </summary>
    public class TableCustomColumn
    {
        public string Name { get; set; } = string.Empty;
        public TableCustomColumnType Type { get; set; } = TableCustomColumnType.Text;
        public string CssClass { get; set; } = string.Empty;
        public string SumFieldName { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    /// <summary>
    /// Types of custom table columns supported
    /// </summary>
    public enum TableCustomColumnType
    {
        Text,
        Checkbox,
        Button,
        Formula
    }
}