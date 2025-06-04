// NewwaysAdmin.GoogleSheets/Models/SheetModels.cs
using MessagePack;
using System.ComponentModel.DataAnnotations;
using Key = MessagePack.KeyAttribute;

namespace NewwaysAdmin.GoogleSheets.Models
{
    /// <summary>
    /// Configuration for Google Sheets integration
    /// </summary>
    public class GoogleSheetsConfig
    {
        public required string CredentialsPath { get; set; }
        public required string ApplicationName { get; set; }
        public bool AutoShareWithUser { get; set; } = false;
        public string? DefaultShareEmail { get; set; }
    }

    /// <summary>
    /// Result of an export operation
    /// </summary>
    public class ExportResult
    {
        public bool Success { get; set; }
        public string? SheetId { get; set; }
        public string? SheetUrl { get; set; }
        public int RowsExported { get; set; }
        public List<string> Errors { get; set; } = new();
        public DateTime ExportTime { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Information about a created or existing sheet
    /// </summary>
    public class SheetInfo
    {
        public required string SheetId { get; set; }
        public required string Title { get; set; }
        public string? Url { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
    }

    /// <summary>
    /// User-specific configuration stored as MessagePack
    /// </summary>
    [MessagePackObject]
    public class UserSheetConfig
    {
        [Key(0)]
        public string UserId { get; set; } = string.Empty;

        [Key(1)]
        public string ModuleName { get; set; } = string.Empty;

        [Key(2)]
        public Dictionary<string, object> Settings { get; set; } = new();

        [Key(3)]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [Key(4)]
        public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Admin-level sheet configuration
    /// </summary>
    [MessagePackObject]
    public class AdminSheetConfig
    {
        [Key(0)]
        public string ModuleName { get; set; } = string.Empty;

        [Key(1)]
        public string DefaultSheetName { get; set; } = string.Empty;

        [Key(2)]
        public bool AutoCreateSheets { get; set; } = true;

        [Key(3)]
        public bool ShareWithUsers { get; set; } = false;

        [Key(4)]
        public List<string> DefaultColumns { get; set; } = new();

        [Key(5)]
        public Dictionary<string, string> ColumnSettings { get; set; } = new();

        [Key(6)]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        [Key(7)]
        public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Data about a cell in the sheet
    /// </summary>
    public class SheetCell
    {
        public object? Value { get; set; }
        public string? Formula { get; set; }  // Add this
        public string? Format { get; set; }
        public string? BackgroundColor { get; set; }
        public string? FontColor { get; set; }
        public bool IsBold { get; set; }
        public string? Note { get; set; }
        public CellType Type { get; set; } = CellType.Value; // Add this
    }

    public enum CellType
    {
        Value,
        Formula,
        Header,
        Summary
    }

    /// <summary>
    /// Represents a row of data to be exported
    /// </summary>
    public class SheetRow
    {
        public List<SheetCell> Cells { get; set; } = new();
        public bool IsHeader { get; set; } = false;
        public string? BackgroundColor { get; set; }

        public void AddCell(object? value, string? format = null)
        {
            Cells.Add(new SheetCell { Value = value, Format = format });
        }
    }

    /// <summary>
    /// Complete sheet data for export
    /// </summary>
    public class SheetData
    {
        public required string Title { get; set; }
        public List<SheetRow> Rows { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();

        public void AddHeaderRow(params string[] headers)
        {
            var row = new SheetRow { IsHeader = true };
            foreach (var header in headers)
            {
                row.AddCell(header);
            }
            Rows.Add(row);
        }

        public void AddDataRow(params object?[] values)
        {
            var row = new SheetRow();
            foreach (var value in values)
            {
                row.AddCell(value);
            }
            Rows.Add(row);
        }
    }
}