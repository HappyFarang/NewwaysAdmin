// NewwaysAdmin.GoogleSheets/Models/SheetTemplate.cs
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
    }

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
    }

    [MessagePackObject]
    public class FormulaTemplate
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty; // "Total Amount", "Record Count"

        [Key(1)]
        public string Formula { get; set; } = string.Empty; // "=SUM(B:B)", "=COUNTA(A:A)-1"

        [Key(2)]
        public int Row { get; set; }

        [Key(3)]
        public int Column { get; set; }

        [Key(4)]
        public string Format { get; set; } = string.Empty;

        [Key(5)]
        public bool IsBold { get; set; } = true;
    }

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
        public bool AlternateRowColors { get; set; } = true;

        [Key(4)]
        public string AlternateColor { get; set; } = "#F8F8F8";

        [Key(5)]
        public bool AddSummarySection { get; set; } = true;

        [Key(6)]
        public int SummaryStartRow { get; set; } = -1; // -1 means calculate automatically
    }
}