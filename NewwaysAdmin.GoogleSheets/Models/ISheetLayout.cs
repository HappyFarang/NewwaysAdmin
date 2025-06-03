// NewwaysAdmin.GoogleSheets/Interfaces/ISheetLayout.cs
using NewwaysAdmin.GoogleSheets.Models;

namespace NewwaysAdmin.GoogleSheets.Interfaces
{
    /// <summary>
    /// Interface for defining how data should be laid out in Google Sheets
    /// </summary>
    /// <typeparam name="T">The type of data being exported</typeparam>
    public interface ISheetLayout<T>
    {
        /// <summary>
        /// The name/identifier for this layout
        /// </summary>
        string LayoutName { get; }

        /// <summary>
        /// Generate the sheet title for the given data
        /// </summary>
        string GenerateSheetTitle(IEnumerable<T> data, DateTime? startDate = null, DateTime? endDate = null);

        /// <summary>
        /// Get the header row for this layout
        /// </summary>
        SheetRow GetHeaderRow();

        /// <summary>
        /// Convert a single data item to a sheet row
        /// </summary>
        SheetRow ConvertToRow(T item);

        /// <summary>
        /// Convert multiple data items to sheet data
        /// </summary>
        SheetData ConvertToSheetData(IEnumerable<T> data, DateTime? startDate = null, DateTime? endDate = null)
        {
            var sheetData = new SheetData
            {
                Title = GenerateSheetTitle(data, startDate, endDate)
            };

            // Add header
            sheetData.Rows.Add(GetHeaderRow());

            // Add data rows
            foreach (var item in data)
            {
                sheetData.Rows.Add(ConvertToRow(item));
            }

            // Add metadata
            sheetData.Metadata["ExportedAt"] = DateTime.UtcNow;
            sheetData.Metadata["RecordCount"] = data.Count();
            if (startDate.HasValue)
                sheetData.Metadata["StartDate"] = startDate.Value;
            if (endDate.HasValue)
                sheetData.Metadata["EndDate"] = endDate.Value;

            return sheetData;
        }

        /// <summary>
        /// Get formatting rules for specific columns (optional)
        /// </summary>
        Dictionary<int, string> GetColumnFormats() => new();

        /// <summary>
        /// Get column widths (optional) - key is column index, value is width
        /// </summary>
        Dictionary<int, int> GetColumnWidths() => new();

        /// <summary>
        /// Apply any additional formatting after data is added (optional)
        /// </summary>
        void ApplyAdditionalFormatting(SheetData sheetData) { }
    }

    /// <summary>
    /// Registry for managing sheet layouts
    /// </summary>
    public interface ISheetLayoutRegistry
    {
        /// <summary>
        /// Register a layout for a specific type
        /// </summary>
        void RegisterLayout<T>(ISheetLayout<T> layout);

        /// <summary>
        /// Get a layout for a specific type
        /// </summary>
        ISheetLayout<T>? GetLayout<T>();

        /// <summary>
        /// Get all registered layouts
        /// </summary>
        IEnumerable<string> GetRegisteredLayouts();
    }
}