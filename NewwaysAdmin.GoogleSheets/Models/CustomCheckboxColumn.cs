// NewwaysAdmin.GoogleSheets/Models/CustomCheckboxColumn.cs
using MessagePack;

namespace NewwaysAdmin.GoogleSheets.Models
{
    /// <summary>
    /// Represents a user-defined checkbox column for categorization
    /// </summary>
    [MessagePackObject]
    public class CustomCheckboxColumn
    {
        [Key(0)]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Key(1)]
        public string Name { get; set; } = string.Empty; // "Fuel Expenses", "Payroll", etc.

        [Key(2)]
        public string Description { get; set; } = string.Empty; // Optional description

        [Key(3)]
        public string FormulaType { get; set; } = "SUMIF"; // SUMIF, COUNTIF, etc.

        [Key(4)]
        public string TargetColumnProperty { get; set; } = "Amount"; // Which data column to sum/count

        [Key(5)]
        public bool IsActive { get; set; } = true;

        [Key(6)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Key(7)]
        public int DisplayOrder { get; set; } = 0; // Order of checkbox columns

        /// <summary>
        /// Generate the formula for this checkbox column
        /// Example: =SUMIF(E3:E, TRUE, B3:B)
        /// </summary>
        public string GenerateFormula(string checkboxColumnLetter, string targetColumnLetter, int dataStartRow, int dataEndRow)
        {
            var checkboxRange = $"{checkboxColumnLetter}{dataStartRow}:{checkboxColumnLetter}{dataEndRow}";
            var targetRange = $"{targetColumnLetter}{dataStartRow}:{targetColumnLetter}{dataEndRow}";

            return FormulaType switch
            {
                "SUMIF" => $"=SUMIF({checkboxRange}, TRUE, {targetRange})",
                "COUNTIF" => $"=COUNTIF({checkboxRange}, TRUE)",
                "AVERAGEIF" => $"=AVERAGEIF({checkboxRange}, TRUE, {targetRange})",
                _ => $"=SUMIF({checkboxRange}, TRUE, {targetRange})" // Default to SUMIF
            };
        }
    }

    /// <summary>
    /// User's collection of custom checkbox columns for a specific module
    /// </summary>
    [MessagePackObject]
    public class UserCheckboxColumnCollection
    {
        [Key(0)]
        public string UserId { get; set; } = string.Empty;

        [Key(1)]
        public string ModuleName { get; set; } = string.Empty; // "BankSlips", "Sales", etc.

        [Key(2)]
        public string TemplateName { get; set; } = string.Empty; // Optional: tie to specific template

        [Key(3)]
        public List<CustomCheckboxColumn> CheckboxColumns { get; set; } = new();

        [Key(4)]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Get checkbox columns ordered by display order
        /// </summary>
        public List<CustomCheckboxColumn> GetOrderedColumns()
        {
            return CheckboxColumns
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.CreatedAt)
                .ToList();
        }

        /// <summary>
        /// Add a new checkbox column
        /// </summary>
        public void AddCheckboxColumn(string name, string description = "", string formulaType = "SUMIF", string targetColumn = "Amount")
        {
            var column = new CustomCheckboxColumn
            {
                Name = name,
                Description = description,
                FormulaType = formulaType,
                TargetColumnProperty = targetColumn,
                DisplayOrder = CheckboxColumns.Count
            };

            CheckboxColumns.Add(column);
            LastUpdated = DateTime.UtcNow;
        }

        /// <summary>
        /// Remove a checkbox column
        /// </summary>
        public bool RemoveCheckboxColumn(string columnId)
        {
            var column = CheckboxColumns.FirstOrDefault(c => c.Id == columnId);
            if (column != null)
            {
                CheckboxColumns.Remove(column);
                LastUpdated = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reorder checkbox columns
        /// </summary>
        public void ReorderColumns(List<string> columnIds)
        {
            for (int i = 0; i < columnIds.Count; i++)
            {
                var column = CheckboxColumns.FirstOrDefault(c => c.Id == columnIds[i]);
                if (column != null)
                {
                    column.DisplayOrder = i;
                }
            }
            LastUpdated = DateTime.UtcNow;
        }
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