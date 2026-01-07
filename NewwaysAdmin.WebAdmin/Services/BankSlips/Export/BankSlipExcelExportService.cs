// File: NewwaysAdmin.WebAdmin/Services/BankSlips/Export/BankSlipExcelExportService.cs

using ClosedXML.Excel;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips.Export;

/// <summary>
/// Professional Excel export for bank slip projects using ClosedXML.
/// Creates formatted .xlsx files with frozen panes, formulas, and styling.
/// </summary>
public class BankSlipExcelExportService
{
    private readonly ILogger<BankSlipExcelExportService> _logger;

    // Column indices for fixed columns (1-based for ClosedXML)
    private const int COL_DATE = 1;
    private const int COL_TIME = 2;
    private const int COL_RECIPIENT = 3;
    private const int COL_TOTAL = 4;
    private const int COL_NOTE = 5;
    private const int COL_VAT = 6;
    private const int COL_VAT_7PCT = 7;  // VAT 7% calculation
    private const int COL_BILL = 8;
    private const int COL_NO_BILL = 9;   // New: No Bill column
    private const int COL_CATEGORIES_START = 10; // Categories start here

    // Row layout:
    // Row 1: Category headers (merged across subcategories) + Category SUM formulas
    // Row 2: Subcategory headers
    // Row 3: Totals row
    // Row 4+: Data rows
    private const int ROW_CATEGORY_HEADER = 1;
    private const int ROW_SUBCATEGORY_HEADER = 2;
    private const int ROW_TOTALS = 3;
    private const int ROW_DATA_START = 4;

    public BankSlipExcelExportService(ILogger<BankSlipExcelExportService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Export projects to Excel with professional formatting
    /// </summary>
    public byte[] ExportToExcel(
        List<BankSlipProject> projects,
        ExportSettings settings)
    {
        _logger.LogInformation("Starting Excel export for {Count} projects", projects.Count);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(settings.SheetName ?? "Bank Slips");

        // Build list of category columns (grouped by category)
        var categoryColumns = BuildCategoryColumns(projects, settings.SelectedCategories);

        // 1. Create two-row headers (category + subcategory)
        CreateHeaders(sheet, categoryColumns, settings);

        // 2. Add data rows
        var currentRow = ROW_DATA_START;
        foreach (var project in projects.OrderBy(p => p.TransactionTimestamp))
        {
            AddDataRow(sheet, currentRow, project, categoryColumns, settings);
            currentRow++;
        }

        var lastDataRow = currentRow - 1;

        // 3. Add totals row formulas
        CreateTotalsRow(sheet, lastDataRow, categoryColumns, settings);

        // 4. Apply column widths
        ApplyColumnWidths(sheet, categoryColumns);

        // 5. Freeze panes (freeze header rows + totals, and fixed columns)
        sheet.SheetView.FreezeRows(ROW_TOTALS);
        sheet.SheetView.FreezeColumns(COL_NO_BILL); // Freeze up to No Bill column

        // 6. Apply alternating row colors for data
        ApplyAlternatingRowColors(sheet, ROW_DATA_START, lastDataRow, categoryColumns.Count);

        // 7. Add auto-filter on subcategory header row
        var lastCol = COL_CATEGORIES_START + categoryColumns.Count - 1;
        if (categoryColumns.Count == 0) lastCol = COL_NO_BILL;
        sheet.Range(ROW_SUBCATEGORY_HEADER, COL_DATE, lastDataRow, lastCol).SetAutoFilter();

        // Save to memory stream
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        _logger.LogInformation("Excel export complete: {Rows} data rows, {Cols} category columns",
            projects.Count, categoryColumns.Count);

        return stream.ToArray();
    }

    /// <summary>
    /// Build ordered list of category columns based on data and selection
    /// </summary>
    private List<CategoryColumn> BuildCategoryColumns(
        List<BankSlipProject> projects,
        List<string>? selectedCategories)
    {
        // Get all unique category+subcategory combinations from data
        var categoryData = projects
            .Where(p => p.StructuredMemo?.CategoryName != null)
            .Select(p => new
            {
                Category = p.StructuredMemo!.CategoryName!,
                SubCategory = p.StructuredMemo.SubCategoryName
            })
            .Distinct()
            .ToList();

        // Build column list - group by category, then subcategories
        var columns = new List<CategoryColumn>();

        var grouped = categoryData
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            // Skip if category not selected
            if (selectedCategories != null && !selectedCategories.Contains(group.Key))
                continue;

            // Add subcategories as separate columns
            var subs = group.Where(x => x.SubCategory != null)
                           .Select(x => x.SubCategory!)
                           .Distinct()
                           .OrderBy(s => s);

            foreach (var sub in subs)
            {
                columns.Add(new CategoryColumn
                {
                    CategoryName = group.Key,
                    SubCategoryName = sub,
                    DisplayName = sub // Just show subcategory name for brevity
                });
            }

            // If any items have no subcategory, add the category itself
            if (group.Any(x => x.SubCategory == null))
            {
                columns.Add(new CategoryColumn
                {
                    CategoryName = group.Key,
                    SubCategoryName = null,
                    DisplayName = group.Key
                });
            }
        }

        return columns;
    }

    /// <summary>
    /// Create two-row header: Row 1 = Categories (merged), Row 2 = Subcategories
    /// </summary>
    private void CreateHeaders(
        IXLWorksheet sheet,
        List<CategoryColumn> categoryColumns,
        ExportSettings settings)
    {
        // === ROW 1: Category headers (merged for fixed columns, merged per category for dynamic) ===

        // Fixed columns - merge across both rows
        sheet.Cell(ROW_CATEGORY_HEADER, COL_DATE).Value = "Date";
        sheet.Range(ROW_CATEGORY_HEADER, COL_DATE, ROW_SUBCATEGORY_HEADER, COL_DATE).Merge();

        sheet.Cell(ROW_CATEGORY_HEADER, COL_TIME).Value = "Time";
        sheet.Range(ROW_CATEGORY_HEADER, COL_TIME, ROW_SUBCATEGORY_HEADER, COL_TIME).Merge();

        sheet.Cell(ROW_CATEGORY_HEADER, COL_RECIPIENT).Value = "To";
        sheet.Range(ROW_CATEGORY_HEADER, COL_RECIPIENT, ROW_SUBCATEGORY_HEADER, COL_RECIPIENT).Merge();

        sheet.Cell(ROW_CATEGORY_HEADER, COL_TOTAL).Value = "Total";
        sheet.Range(ROW_CATEGORY_HEADER, COL_TOTAL, ROW_SUBCATEGORY_HEADER, COL_TOTAL).Merge();

        sheet.Cell(ROW_CATEGORY_HEADER, COL_NOTE).Value = "Note";
        sheet.Range(ROW_CATEGORY_HEADER, COL_NOTE, ROW_SUBCATEGORY_HEADER, COL_NOTE).Merge();

        if (settings.IncludeVat)
        {
            sheet.Cell(ROW_CATEGORY_HEADER, COL_VAT).Value = "VAT";
            sheet.Range(ROW_CATEGORY_HEADER, COL_VAT, ROW_SUBCATEGORY_HEADER, COL_VAT).Merge();

            sheet.Cell(ROW_CATEGORY_HEADER, COL_VAT_7PCT).Value = "VAT 7%";
            sheet.Range(ROW_CATEGORY_HEADER, COL_VAT_7PCT, ROW_SUBCATEGORY_HEADER, COL_VAT_7PCT).Merge();
        }

        if (settings.IncludeBill)
        {
            sheet.Cell(ROW_CATEGORY_HEADER, COL_BILL).Value = "Bill";
            sheet.Range(ROW_CATEGORY_HEADER, COL_BILL, ROW_SUBCATEGORY_HEADER, COL_BILL).Merge();

            sheet.Cell(ROW_CATEGORY_HEADER, COL_NO_BILL).Value = "No Bill";
            sheet.Range(ROW_CATEGORY_HEADER, COL_NO_BILL, ROW_SUBCATEGORY_HEADER, COL_NO_BILL).Merge();
        }

        // === Dynamic category columns ===
        // Group columns by category to know merge ranges
        var categoryGroups = categoryColumns
            .Select((col, index) => new { col, index })
            .GroupBy(x => x.col.CategoryName)
            .ToList();

        // Four tones for alternating categories (row 1 and row 2 each get their own alternating pair)
        var categoryTone1 = XLColor.FromArgb(0, 100, 0);      // Dark green for row 1, even categories
        var categoryTone2 = XLColor.FromArgb(34, 139, 34);    // Forest green for row 1, odd categories
        var subcategoryTone1 = XLColor.FromArgb(120, 200, 120); // Medium light green for row 2, even categories
        var subcategoryTone2 = XLColor.FromArgb(152, 251, 152); // Pale green for row 2, odd categories

        var colIndex = COL_CATEGORIES_START;
        var categoryIndex = 0;
        foreach (var group in categoryGroups)
        {
            var startCol = colIndex;
            var columnsInCategory = group.Count();
            var endCol = startCol + columnsInCategory - 1;

            // Determine which tones to use for this category
            var isEvenCategory = (categoryIndex % 2 == 0);
            var catTone = isEvenCategory ? categoryTone1 : categoryTone2;
            var subTone = isEvenCategory ? subcategoryTone1 : subcategoryTone2;

            // Row 1: Category name (merged across all its subcategories)
            sheet.Cell(ROW_CATEGORY_HEADER, startCol).Value = group.Key;
            if (columnsInCategory > 1)
            {
                sheet.Range(ROW_CATEGORY_HEADER, startCol, ROW_CATEGORY_HEADER, endCol).Merge();
            }

            // Style category header with alternating tone
            var categoryHeaderRange = sheet.Range(ROW_CATEGORY_HEADER, startCol, ROW_CATEGORY_HEADER, endCol);
            categoryHeaderRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            categoryHeaderRange.Style.Font.Bold = true;
            categoryHeaderRange.Style.Fill.BackgroundColor = catTone;
            categoryHeaderRange.Style.Font.FontColor = XLColor.White;

            // Row 2: Subcategory names with alternating tone
            foreach (var item in group)
            {
                var subCell = sheet.Cell(ROW_SUBCATEGORY_HEADER, colIndex);
                subCell.Value = item.col.SubCategoryName ?? item.col.CategoryName;
                subCell.Style.Fill.BackgroundColor = subTone;
                colIndex++;
            }

            categoryIndex++;
        }

        // === Apply styling to both header rows ===
        var lastCol = COL_CATEGORIES_START + categoryColumns.Count - 1;
        if (categoryColumns.Count == 0) lastCol = COL_NO_BILL;

        // Row 1 style for FIXED columns only (category columns already styled above)
        var row1FixedRange = sheet.Range(ROW_CATEGORY_HEADER, COL_DATE, ROW_CATEGORY_HEADER, COL_NO_BILL);
        row1FixedRange.Style.Font.Bold = true;
        row1FixedRange.Style.Fill.BackgroundColor = XLColor.DarkGreen;
        row1FixedRange.Style.Font.FontColor = XLColor.White;
        row1FixedRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        row1FixedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // Row 2 style (subcategory headers) - green tones already applied per category above
        var row2Range = sheet.Range(ROW_SUBCATEGORY_HEADER, COL_DATE, ROW_SUBCATEGORY_HEADER, lastCol);
        row2Range.Style.Font.Bold = true;
        row2Range.Style.Font.FontColor = XLColor.Black;
        row2Range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        row2Range.Style.Border.BottomBorder = XLBorderStyleValues.Medium;

        // Apply green tone to fixed columns in row 2 (they're merged so this fills the merged area)
        var fixedColsRow2 = sheet.Range(ROW_SUBCATEGORY_HEADER, COL_DATE, ROW_SUBCATEGORY_HEADER, COL_NO_BILL);
        fixedColsRow2.Style.Fill.BackgroundColor = XLColor.FromArgb(120, 200, 120); // subcategoryTone1
    }

    /// <summary>
    /// Create totals row with SUM formulas and category totals
    /// </summary>
    private void CreateTotalsRow(
        IXLWorksheet sheet,
        int lastDataRow,
        List<CategoryColumn> categoryColumns,
        ExportSettings settings)
    {
        // Label
        sheet.Cell(ROW_TOTALS, COL_DATE).Value = "TOTALS";
        sheet.Cell(ROW_TOTALS, COL_DATE).Style.Font.Bold = true;

        // Total column sum
        if (lastDataRow >= ROW_DATA_START)
        {
            var totalColLetter = GetColumnLetter(COL_TOTAL);
            sheet.Cell(ROW_TOTALS, COL_TOTAL).FormulaA1 = $"=SUM({totalColLetter}{ROW_DATA_START}:{totalColLetter}{lastDataRow})";
            sheet.Cell(ROW_TOTALS, COL_TOTAL).Style.NumberFormat.Format = "#,##0.00";
        }

        // VAT sum and 7% calculation
        if (settings.IncludeVat && lastDataRow >= ROW_DATA_START)
        {
            var vatColLetter = GetColumnLetter(COL_VAT);
            sheet.Cell(ROW_TOTALS, COL_VAT).FormulaA1 = $"=SUM({vatColLetter}{ROW_DATA_START}:{vatColLetter}{lastDataRow})";
            sheet.Cell(ROW_TOTALS, COL_VAT).Style.NumberFormat.Format = "#,##0.00";

            // VAT 7% = VAT sum * 0.07
            var vatTotalCell = $"{vatColLetter}{ROW_TOTALS}";
            sheet.Cell(ROW_TOTALS, COL_VAT_7PCT).FormulaA1 = $"={vatTotalCell}*0.07";
            sheet.Cell(ROW_TOTALS, COL_VAT_7PCT).Style.NumberFormat.Format = "#,##0.00";
        }

        // Bill and No Bill sums
        if (settings.IncludeBill && lastDataRow >= ROW_DATA_START)
        {
            var billColLetter = GetColumnLetter(COL_BILL);
            sheet.Cell(ROW_TOTALS, COL_BILL).FormulaA1 = $"=SUM({billColLetter}{ROW_DATA_START}:{billColLetter}{lastDataRow})";
            sheet.Cell(ROW_TOTALS, COL_BILL).Style.NumberFormat.Format = "#,##0.00";

            var noBillColLetter = GetColumnLetter(COL_NO_BILL);
            sheet.Cell(ROW_TOTALS, COL_NO_BILL).FormulaA1 = $"=SUM({noBillColLetter}{ROW_DATA_START}:{noBillColLetter}{lastDataRow})";
            sheet.Cell(ROW_TOTALS, COL_NO_BILL).Style.NumberFormat.Format = "#,##0.00";
        }

        // Category column sums (subcategory level)
        var colIndex = COL_CATEGORIES_START;
        foreach (var cat in categoryColumns)
        {
            if (lastDataRow >= ROW_DATA_START)
            {
                var colLetter = GetColumnLetter(colIndex);
                sheet.Cell(ROW_TOTALS, colIndex).FormulaA1 = $"=SUM({colLetter}{ROW_DATA_START}:{colLetter}{lastDataRow})";
                sheet.Cell(ROW_TOTALS, colIndex).Style.NumberFormat.Format = "#,##0.00";
            }
            colIndex++;
        }

        // === Category-level totals (sum of all subcategories) in Row 2 ===
        // Put "CategoryName - SUM" in row 2, first column of each category group
        var categoryGroups = categoryColumns
            .Select((col, index) => new { col, index })
            .GroupBy(x => x.col.CategoryName)
            .ToList();

        colIndex = COL_CATEGORIES_START;
        foreach (var group in categoryGroups)
        {
            var startCol = colIndex;
            var columnsInCategory = group.Count();
            var endCol = startCol + columnsInCategory - 1;

            // Build formula to sum all subcategory totals for this category
            var startLetter = GetColumnLetter(startCol);
            var endLetter = GetColumnLetter(endCol);

            // Put category sum formula in subcategory header row (row 2), first column of the group
            // Shows: "Payroll" in cell with formula that calculates sum of all payroll subcategory totals
            var categorySumFormula = $"=SUM({startLetter}{ROW_TOTALS}:{endLetter}{ROW_TOTALS})";
            var categorySumCell = sheet.Cell(ROW_SUBCATEGORY_HEADER, startCol);

            // Add the category total to a new row above? No - let's put it IN the category header cell itself
            // Actually, merged cells with formulas are tricky. Let's add a dedicated "category totals" display
            // Best approach: Add the sum to the right of the category name in the header
            // We'll update the category header text to include the sum reference

            // For clean display, let's add the category total formula to row 1 (in the merged cell)
            // ClosedXML allows formulas in merged cells - they show in the first cell
            var categoryHeaderCell = sheet.Cell(ROW_CATEGORY_HEADER, startCol);
            var currentValue = categoryHeaderCell.GetString();

            // We can't mix text and formulas easily, so let's put category totals in a separate approach:
            // Add a comment or create a summary section. For now, let's add the sum to the subcategory header's first cell
            // Append formula reference to show: "Beer (=SUM)" - but this doesn't work either

            // Best solution: Put the category-level sum in the TOTALS row, in the FIRST column of each category
            // But wait, that's where the subcategory sum goes...

            // Alternative: Create a formula cell in row 2 that shows the total
            // Let's try: In row 2, first column of category, show "SubcatName (CategoryTotal: =SUM)"
            // This gets messy. 

            // Simplest clean solution: Add category total as a CONCATENATE in row 1
            // Actually, let's just accept that the totals row shows subcategory sums
            // AND we add a category summary row BELOW totals? No, that breaks the freeze.

            // Final decision: Put category sum formulas in Row 2 as cell comments/notes
            // OR: Better - change header to show formula result
            // The cleanest: Keep as-is but add text+formula using a helper column approach

            // ACTUAL SOLUTION: Show category totals WITHIN the merged header using a formula
            // We'll rebuild the header to use: ="Payroll - "&TEXT(SUM(...),"#,##0")
            if (lastDataRow >= ROW_DATA_START)
            {
                var sumRange = $"{startLetter}{ROW_TOTALS}:{endLetter}{ROW_TOTALS}";
                categoryHeaderCell.FormulaA1 = $"=\"{group.Key} - \"&TEXT(SUM({sumRange}),\"#,##0\")";
            }

            colIndex += columnsInCategory;
        }

        // Style totals row - blue-gray to distinguish from alternating rows
        var lastCol = COL_CATEGORIES_START + categoryColumns.Count - 1;
        if (categoryColumns.Count == 0) lastCol = COL_NO_BILL;

        var totalsRange = sheet.Range(ROW_TOTALS, COL_DATE, ROW_TOTALS, lastCol);
        totalsRange.Style.Font.Bold = true;
        totalsRange.Style.Fill.BackgroundColor = XLColor.FromArgb(176, 196, 222); // Light steel blue
        totalsRange.Style.Border.TopBorder = XLBorderStyleValues.Thin;
        totalsRange.Style.Border.BottomBorder = XLBorderStyleValues.Double;
    }

    /// <summary>
    /// Add a single data row
    /// </summary>
    private void AddDataRow(
        IXLWorksheet sheet,
        int row,
        BankSlipProject project,
        List<CategoryColumn> categoryColumns,
        ExportSettings settings)
    {
        // Date and Time
        sheet.Cell(row, COL_DATE).Value = project.TransactionTimestamp.ToString("yyyy/MM/dd");
        sheet.Cell(row, COL_TIME).Value = project.TransactionTimestamp.ToString("HH:mm");

        // Recipient
        sheet.Cell(row, COL_RECIPIENT).Value = project.GetRecipient();

        // Total (as number for calculations)
        var totalValue = ParseAmount(project.GetTotal());
        sheet.Cell(row, COL_TOTAL).Value = totalValue;
        sheet.Cell(row, COL_TOTAL).Style.NumberFormat.Format = "#,##0.00";

        // Note - only show memo part if structured, otherwise show full raw note (for old format)
        string noteValue;
        if (project.HasStructuralNote && project.StructuredMemo != null)
        {
            // Structural note: only show the actual memo content
            noteValue = project.StructuredMemo.Memo ?? "";
        }
        else
        {
            // Old format or no structure: show full note as-is
            noteValue = project.GetNote() ?? "";
        }
        sheet.Cell(row, COL_NOTE).Value = noteValue;

        // VAT - write amount if has VAT
        if (settings.IncludeVat)
        {
            if (project.HasVat == true)
            {
                var vatCell = sheet.Cell(row, COL_VAT);
                vatCell.Value = totalValue;
                vatCell.Style.NumberFormat.Format = "#,##0.00";
                vatCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
            }
            // VAT 7% column is empty in data rows - only formula in totals
        }

        // Bill / No Bill - write amount in appropriate column
        if (settings.IncludeBill)
        {
            if (project.HasBill)
            {
                var billCell = sheet.Cell(row, COL_BILL);
                billCell.Value = totalValue;
                billCell.Style.NumberFormat.Format = "#,##0.00";
                billCell.Style.Fill.BackgroundColor = XLColor.LightGreen;
            }
            else
            {
                // No Bill - amount goes here
                var noBillCell = sheet.Cell(row, COL_NO_BILL);
                noBillCell.Value = totalValue;
                noBillCell.Style.NumberFormat.Format = "#,##0.00";
            }
        }

        // Category columns - put amount in matching category
        var projectCategory = project.StructuredMemo?.CategoryName;
        var projectSubCategory = project.StructuredMemo?.SubCategoryName;

        var colIndex = COL_CATEGORIES_START;
        foreach (var cat in categoryColumns)
        {
            var matches = (cat.CategoryName == projectCategory) &&
                         (cat.SubCategoryName == projectSubCategory ||
                          (cat.SubCategoryName == null && projectSubCategory == null));

            if (matches)
            {
                sheet.Cell(row, colIndex).Value = totalValue;
                sheet.Cell(row, colIndex).Style.NumberFormat.Format = "#,##0.00";
            }
            colIndex++;
        }
    }

    /// <summary>
    /// Apply column widths
    /// </summary>
    private void ApplyColumnWidths(IXLWorksheet sheet, List<CategoryColumn> categoryColumns)
    {
        sheet.Column(COL_DATE).Width = 12;
        sheet.Column(COL_TIME).Width = 8;
        sheet.Column(COL_RECIPIENT).Width = 40;
        sheet.Column(COL_TOTAL).Width = 12;
        sheet.Column(COL_NOTE).Width = 30;
        sheet.Column(COL_VAT).Width = 12;
        sheet.Column(COL_VAT_7PCT).Width = 12;
        sheet.Column(COL_BILL).Width = 12;
        sheet.Column(COL_NO_BILL).Width = 12;

        // Category columns - need to be wider for single-subcategory categories
        // because the header shows "CategoryName - 99,999"
        var categoryGroups = categoryColumns
            .Select((col, index) => new { col, index })
            .GroupBy(x => x.col.CategoryName)
            .ToList();

        var colIndex = COL_CATEGORIES_START;
        foreach (var group in categoryGroups)
        {
            var columnsInCategory = group.Count();

            // Estimate width needed for "CategoryName - 99,999" format
            var categoryName = group.Key;
            var estimatedHeaderWidth = categoryName.Length + 12; // " - 99,999" adds ~10 chars
            var minWidthPerColumn = Math.Max(12, estimatedHeaderWidth / columnsInCategory);

            // For single-column categories, make sure it's wide enough for the header
            if (columnsInCategory == 1)
            {
                minWidthPerColumn = Math.Max(18, estimatedHeaderWidth); // At least 18 for single column
            }

            foreach (var item in group)
            {
                sheet.Column(colIndex).Width = minWidthPerColumn;
                colIndex++;
            }
        }
    }

    /// <summary>
    /// Apply alternating row colors for readability (white and light gray)
    /// </summary>
    private void ApplyAlternatingRowColors(IXLWorksheet sheet, int startRow, int endRow, int categoryCount)
    {
        var lastCol = COL_CATEGORIES_START + categoryCount - 1;
        if (categoryCount == 0) lastCol = COL_NO_BILL;

        var lightGray = XLColor.FromArgb(240, 240, 240); // Light gray for alternating rows

        for (int row = startRow; row <= endRow; row++)
        {
            var rowRange = sheet.Range(row, COL_DATE, row, lastCol);

            if ((row - startRow) % 2 == 1) // Odd offset = light gray
            {
                foreach (var cell in rowRange.Cells())
                {
                    // Only apply to cells without existing background (preserve green VAT/Bill cells)
                    if (cell.Style.Fill.BackgroundColor == XLColor.NoColor ||
                        cell.Style.Fill.BackgroundColor == XLColor.Transparent ||
                        cell.Style.Fill.BackgroundColor.Color.A == 0) // Also check for fully transparent
                    {
                        cell.Style.Fill.BackgroundColor = lightGray;
                    }
                }
            }
            // Even offset rows stay white (default) - no action needed
        }
    }

    /// <summary>
    /// Get Excel column letter from 1-based index
    /// </summary>
    private string GetColumnLetter(int columnIndex)
    {
        string letter = "";
        while (columnIndex > 0)
        {
            int mod = (columnIndex - 1) % 26;
            letter = (char)('A' + mod) + letter;
            columnIndex = (columnIndex - 1) / 26;
        }
        return letter;
    }

    /// <summary>
    /// Parse amount string to decimal
    /// </summary>
    private decimal ParseAmount(string? amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr))
            return 0;

        var cleaned = amountStr
            .Replace(",", "")
            .Replace("฿", "")
            .Replace("THB", "")
            .Replace("บาท", "")
            .Trim();

        return decimal.TryParse(cleaned, out var amount) ? amount : 0;
    }

    /// <summary>
    /// Generate suggested filename
    /// </summary>
    public string GenerateFilename(DateTime? dateFrom, DateTime? dateTo)
    {
        var fromStr = dateFrom?.ToString("yyyy-MM-dd") ?? "start";
        var toStr = dateTo?.ToString("yyyy-MM-dd") ?? "end";
        return $"BankSlips_{fromStr}_to_{toStr}.xlsx";
    }
}

/// <summary>
/// Category column definition
/// </summary>
public class CategoryColumn
{
    public string CategoryName { get; set; } = "";
    public string? SubCategoryName { get; set; }
    public string DisplayName { get; set; } = "";
}

/// <summary>
/// Export settings
/// </summary>
public class ExportSettings
{
    public string? SheetName { get; set; }
    public bool IncludeVat { get; set; } = true;
    public bool IncludeBill { get; set; } = true;
    public bool IncludeLocation { get; set; } = false;
    public bool IncludePerson { get; set; } = false;
    public List<string>? SelectedCategories { get; set; }
}