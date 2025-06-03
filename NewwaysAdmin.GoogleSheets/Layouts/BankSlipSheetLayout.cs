// NewwaysAdmin.GoogleSheets/Layouts/BankSlipSheetLayout.cs
using NewwaysAdmin.GoogleSheets.Interfaces;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.GoogleSheets.Layouts
{
    public class BankSlipSheetLayout : ISheetLayout<BankSlipData>
    {
        public string LayoutName => "BankSlip";

        public string GenerateSheetTitle(IEnumerable<BankSlipData> data, DateTime? startDate = null, DateTime? endDate = null)
        {
            var title = "Bank Slips";

            if (startDate.HasValue && endDate.HasValue)
            {
                title += $" {startDate.Value:yyyy-MM-dd} to {endDate.Value:yyyy-MM-dd}";
            }
            else if (startDate.HasValue)
            {
                title += $" from {startDate.Value:yyyy-MM-dd}";
            }
            else if (endDate.HasValue)
            {
                title += $" until {endDate.Value:yyyy-MM-dd}";
            }

            var count = data.Count();
            if (count > 0)
            {
                title += $" ({count} records)";
            }

            return title;
        }

        public SheetRow GetHeaderRow()
        {
            var row = new SheetRow { IsHeader = true };

            row.AddCell("Date");
            row.AddCell("Amount (฿)");
            row.AddCell("Account Name");
            row.AddCell("Account Number");
            row.AddCell("Receiver Name");
            row.AddCell("Receiver Account");
            row.AddCell("Note");
            row.AddCell("Collection");
            row.AddCell("Processed By");
            row.AddCell("Processed At");
            row.AddCell("Original File");

            return row;
        }

        public SheetRow ConvertToRow(BankSlipData item)
        {
            var row = new SheetRow();

            // Convert Buddhist calendar to Christian Era for display
            var ceDate = item.TransactionDate.AddYears(-543);
            row.AddCell(ceDate.ToString("yyyy-MM-dd"));

            // Format amount with proper number formatting
            row.AddCell(item.Amount, "#,##0.00");

            row.AddCell(item.AccountName ?? "");
            row.AddCell(item.AccountNumber ?? "");
            row.AddCell(item.ReceiverName ?? "");
            row.AddCell(item.ReceiverAccount ?? "");
            row.AddCell(item.Note ?? "");
            row.AddCell(item.SlipCollectionName ?? "");
            row.AddCell(item.ProcessedBy ?? "");

            // Format processed date
            row.AddCell(item.ProcessedAt != default ? item.ProcessedAt.ToString("yyyy-MM-dd HH:mm") : "");

            // Get just the filename, not the full path
            var fileName = !string.IsNullOrEmpty(item.OriginalFilePath)
                ? Path.GetFileName(item.OriginalFilePath)
                : "";
            row.AddCell(fileName);

            return row;
        }

        public Dictionary<int, string> GetColumnFormats()
        {
            return new Dictionary<int, string>
            {
                { 0, "yyyy-mm-dd" },      // Date column
                { 1, "#,##0.00" },        // Amount column
                { 9, "yyyy-mm-dd hh:mm" } // Processed At column
            };
        }

        public Dictionary<int, int> GetColumnWidths()
        {
            return new Dictionary<int, int>
            {
                { 0, 120 },  // Date
                { 1, 100 },  // Amount
                { 2, 200 },  // Account Name
                { 3, 150 },  // Account Number
                { 4, 200 },  // Receiver Name
                { 5, 150 },  // Receiver Account
                { 6, 300 },  // Note
                { 7, 150 },  // Collection
                { 8, 120 },  // Processed By
                { 9, 150 },  // Processed At
                { 10, 200 }  // Original File
            };
        }

        public void ApplyAdditionalFormatting(SheetData sheetData)
        {
            // Add summary information at the bottom
            if (sheetData.Rows.Count > 1) // More than just header
            {
                // Add empty row
                sheetData.Rows.Add(new SheetRow());

                // Add summary row
                var summaryRow = new SheetRow();
                summaryRow.AddCell("Total Records:");
                summaryRow.AddCell((sheetData.Rows.Count - 2).ToString()); // -2 for header and this row
                summaryRow.AddCell("");
                summaryRow.AddCell("");
                summaryRow.AddCell("");
                summaryRow.AddCell("");
                summaryRow.AddCell("");
                summaryRow.AddCell("");
                summaryRow.AddCell("");
                summaryRow.AddCell($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm}");
                summaryRow.AddCell("");

                sheetData.Rows.Add(summaryRow);

                // Calculate total amount if there are data rows
                var dataRows = sheetData.Rows.Skip(1).Take(sheetData.Rows.Count - 3); // Skip header, summary, and empty row
                var totalAmount = 0m;
                var validAmounts = 0;

                foreach (var row in dataRows)
                {
                    if (row.Cells.Count > 1 && row.Cells[1].Value != null)
                    {
                        if (decimal.TryParse(row.Cells[1].Value.ToString(), out var amount))
                        {
                            totalAmount += amount;
                            validAmounts++;
                        }
                    }
                }

                if (validAmounts > 0)
                {
                    var totalRow = new SheetRow();
                    totalRow.AddCell("Total Amount:");
                    totalRow.AddCell(totalAmount, "#,##0.00");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");
                    totalRow.AddCell("");

                    sheetData.Rows.Add(totalRow);
                }
            }
        }
    }
}