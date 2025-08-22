// NewwaysAdmin.Shared/Tables/DictionaryTableParser.cs
// Service for transforming raw dictionary data into table-ready format

using System;
using System.Collections.Generic;
using System.Linq;

namespace NewwaysAdmin.Shared.Tables
{
    /// <summary>
    /// Transforms raw dictionary data into format suitable for DictionaryTable component
    /// Handles column ordering, data type conversion, formula insertion, etc.
    /// </summary>
    public class DictionaryTableParser
    {
        /// <summary>
        /// Transform List of dictionaries into column-based table data
        /// </summary>
        public Dictionary<string, List<string>> ParseToColumns(
            List<Dictionary<string, string>> rawData,
            List<string> selectedColumns,
            TableParseOptions? options = null)
        {
            options ??= new TableParseOptions();
            var tableData = new Dictionary<string, List<string>>();

            if (!rawData.Any() || !selectedColumns.Any())
                return tableData;

            // Filter out error documents
            var cleanData = rawData.Where(doc => !doc.ContainsKey("Error")).ToList();

            // Limit rows if specified
            if (options.MaxRows.HasValue)
            {
                cleanData = cleanData.Take(options.MaxRows.Value).ToList();
            }

            // Build columns in the specified order
            foreach (var columnName in selectedColumns)
            {
                var columnData = new List<string>();

                // Add formula row if enabled
                if (options.IncludeFormulaRow)
                {
                    columnData.Add(GenerateFormula(columnName, options));
                }

                // Add actual data rows
                foreach (var row in cleanData)
                {
                    columnData.Add(ExtractValue(row, columnName, options));
                }

                tableData[columnName] = columnData;
            }

            // Add custom columns (tick boxes, etc.)
            foreach (var customColumn in options.CustomColumns)
            {
                var customData = new List<string>();

                // Add formula for custom column
                if (options.IncludeFormulaRow)
                {
                    customData.Add(GenerateCustomFormula(customColumn, options));
                }

                // Add custom column data
                for (int i = 0; i < cleanData.Count; i++)
                {
                    customData.Add(GenerateCustomValue(customColumn, cleanData[i], options));
                }

                tableData[FormatCustomColumnHeader(customColumn)] = customData;
            }

            return tableData;
        }

        /// <summary>
        /// Extract and format value from row dictionary
        /// </summary>
        private string ExtractValue(Dictionary<string, string> row, string columnName, TableParseOptions options)
        {
            if (!row.TryGetValue(columnName, out var value))
            {
                return options.EmptyValuePlaceholder;
            }

            // ✅ CRITICAL: Ensure value is always a string
            var stringValue = value?.ToString() ?? string.Empty;

            if (string.IsNullOrEmpty(stringValue))
            {
                return options.EmptyValuePlaceholder;
            }

            // Truncate long values if specified
            if (options.MaxValueLength.HasValue && stringValue.Length > options.MaxValueLength.Value)
            {
                return stringValue.Substring(0, options.MaxValueLength.Value - 3) + "...";
            }

            // Handle numeric conversion if needed
            if (options.ConvertNumbers && IsNumericColumn(columnName))
            {
                if (decimal.TryParse(stringValue, out var numericValue))
                {
                    return numericValue.ToString(options.NumericFormat);
                }
            }

            return stringValue;
        }

        /// <summary>
        /// Generate formula for standard columns
        /// </summary>
        private string GenerateFormula(string columnName, TableParseOptions options)
        {
            if (IsNumericColumn(columnName))
            {
                return options.NumericFormula;
            }
            return options.NonNumericFormula;
        }

        /// <summary>
        /// Generate formula for custom columns
        /// </summary>
        private string GenerateCustomFormula(TableCustomColumn customColumn, TableParseOptions options)
        {
            return customColumn.Type switch
            {
                TableCustomColumnType.Checkbox => options.CheckboxFormula,
                TableCustomColumnType.Button => "—",
                _ => options.NonNumericFormula
            };
        }

        /// <summary>
        /// Generate value for custom columns
        /// </summary>
        private string GenerateCustomValue(TableCustomColumn customColumn, Dictionary<string, string> row, TableParseOptions options)
        {
            return customColumn.Type switch
            {
                TableCustomColumnType.Checkbox => options.CheckboxPlaceholder,
                TableCustomColumnType.Button => options.ButtonPlaceholder,
                _ => options.EmptyValuePlaceholder
            };
        }

        /// <summary>
        /// Format custom column header with appropriate icons
        /// </summary>
        private string FormatCustomColumnHeader(TableCustomColumn customColumn)
        {
            return customColumn.Type switch
            {
                TableCustomColumnType.Checkbox => $"{customColumn.Name} ✓",
                TableCustomColumnType.Button => $"{customColumn.Name} ▶",
                _ => customColumn.Name
            };
        }

        /// <summary>
        /// Check if column contains numeric data
        /// </summary>
        private bool IsNumericColumn(string columnName)
        {
            var numericKeywords = new[] { "Amount", "Total", "Fee", "Cost", "Balance", "Price", "Sum" };
            return numericKeywords.Any(keyword =>
                columnName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Configuration options for table parsing
    /// </summary>
    public class TableParseOptions
    {
        public bool IncludeFormulaRow { get; set; } = true;
        public int? MaxRows { get; set; } = null;
        public int? MaxValueLength { get; set; } = 20;
        public bool ConvertNumbers { get; set; } = true;
        public string NumericFormat { get; set; } = "F2";
        public string EmptyValuePlaceholder { get; set; } = "—";
        public string NumericFormula { get; set; } = "=SUM(...)";
        public string NonNumericFormula { get; set; } = "—";
        public string CheckboxFormula { get; set; } = "=SUMIF(...)";
        public string CheckboxPlaceholder { get; set; } = "☐";
        public string ButtonPlaceholder { get; set; } = "▶";
        public List<TableCustomColumn> CustomColumns { get; set; } = new();
    }

}