// File: NewwaysAdmin.WebAdmin/Components/Features/Accounting/BankSlipProjects/Modals/ExportModal.razor.cs

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Export;

namespace NewwaysAdmin.WebAdmin.Components.Features.Accounting.BankSlipProjects.Modals;

public partial class ExportModal : ComponentBase
{
    [Inject] private BankSlipExcelExportService ExcelService { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ILogger<ExportModal> Logger { get; set; } = default!;

    [Parameter] public List<BankSlipProject> Projects { get; set; } = new();
    [Parameter] public List<CategoryInfo> AvailableCategories { get; set; } = new();

    // State
    private bool isVisible = false;
    private bool isExporting = false;
    private string? exportError = null;

    // Date range
    private DateTime? dateFrom;
    private DateTime? dateTo;

    // Include options
    private bool includeVat = true;
    private bool includeBill = true;
    private bool includePrivate = false;
    private bool includeLocation = false;
    private bool includePerson = false;

    // Category selections
    private List<CategorySelection> categorySelections = new();

    #region Public Methods

    public Task OpenAsync(DateTime? defaultDateFrom, DateTime? defaultDateTo)
    {
        dateFrom = defaultDateFrom;
        dateTo = defaultDateTo;
        exportError = null;

        BuildCategorySelections();

        isVisible = true;
        StateHasChanged();

        return Task.CompletedTask;
    }

    #endregion

    #region UI Event Handlers

    private void Close()
    {
        isVisible = false;
        StateHasChanged();
    }

    private async Task ExportAsync()
    {
        isExporting = true;
        exportError = null;
        StateHasChanged();

        try
        {
            var projectsToExport = GetProjectsForExport();

            if (projectsToExport.Count == 0)
            {
                exportError = "No transactions to export with current filters.";
                return;
            }

            var selectedCategoryNames = categorySelections
                .Where(c => c.IsSelected)
                .Select(c => c.Name)
                .ToList();

            // Build export settings
            var settings = new ExportSettings
            {
                SheetName = $"BankSlips {dateFrom:yyyy-MM-dd}",
                IncludeVat = includeVat,
                IncludeBill = includeBill,
                IncludeLocation = includeLocation,
                IncludePerson = includePerson,
                SelectedCategories = selectedCategoryNames
            };

            // Generate Excel file
            var excelBytes = ExcelService.ExportToExcel(projectsToExport, settings);

            // Generate filename
            var filename = ExcelService.GenerateFilename(dateFrom, dateTo);

            // Download via JS interop
            await DownloadFileAsync(filename, excelBytes);

            Logger.LogInformation("Exported {Count} projects to {Filename}",
                projectsToExport.Count, filename);

            Close();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error exporting to Excel");
            exportError = $"Export failed: {ex.Message}";
        }
        finally
        {
            isExporting = false;
            StateHasChanged();
        }
    }

    private void ToggleCategory(CategorySelection cat)
    {
        cat.IsExpanded = !cat.IsExpanded;
    }

    private void ToggleCategorySelection(CategorySelection cat, bool selected)
    {
        cat.IsSelected = selected;
        foreach (var sub in cat.SubCategories)
        {
            sub.IsSelected = selected;
        }
    }

    private void SelectAllCategories()
    {
        foreach (var cat in categorySelections)
        {
            cat.IsSelected = true;
            foreach (var sub in cat.SubCategories)
            {
                sub.IsSelected = true;
            }
        }
    }

    private void SelectNoneCategories()
    {
        foreach (var cat in categorySelections)
        {
            cat.IsSelected = false;
            foreach (var sub in cat.SubCategories)
            {
                sub.IsSelected = false;
            }
        }
    }

    #endregion

    #region Helper Methods

    private async Task DownloadFileAsync(string filename, byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        var mimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        await JS.InvokeVoidAsync("downloadFileFromBase64", filename, base64, mimeType);
    }

    private string GetSafeId(string name)
    {
        return "cat_" + name.Replace(" ", "_").Replace("(", "").Replace(")", "");
    }

    private void BuildCategorySelections()
    {
        categorySelections.Clear();

        var projectsInRange = GetFilteredProjects();

        var categoryGroups = projectsInRange
            .Where(p => p.StructuredMemo?.CategoryName != null)
            .GroupBy(p => p.StructuredMemo!.CategoryName!)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var catInfo in AvailableCategories.OrderBy(c => c.Name))
        {
            var projectsInCat = categoryGroups.ContainsKey(catInfo.Name)
                ? categoryGroups[catInfo.Name]
                : new List<BankSlipProject>();

            var selection = new CategorySelection
            {
                Name = catInfo.Name,
                IsSelected = true,
                IsExpanded = false,
                ProjectCount = projectsInCat.Count
            };

            foreach (var subName in catInfo.SubCategories)
            {
                var subCount = projectsInCat.Count(p => p.StructuredMemo?.SubCategoryName == subName);
                selection.SubCategories.Add(new SubCategorySelection
                {
                    Name = subName,
                    IsSelected = true,
                    ProjectCount = subCount
                });
            }

            categorySelections.Add(selection);
        }

        var uncategorizedCount = projectsInRange.Count(p =>
            p.StructuredMemo == null || string.IsNullOrEmpty(p.StructuredMemo.CategoryName));

        if (uncategorizedCount > 0)
        {
            categorySelections.Add(new CategorySelection
            {
                Name = "(Uncategorized)",
                IsSelected = true,
                IsExpanded = false,
                ProjectCount = uncategorizedCount
            });
        }
    }

    private List<BankSlipProject> GetFilteredProjects()
    {
        var query = Projects.AsEnumerable();

        if (dateFrom.HasValue)
            query = query.Where(p => p.TransactionTimestamp.Date >= dateFrom.Value.Date);

        if (dateTo.HasValue)
            query = query.Where(p => p.TransactionTimestamp.Date <= dateTo.Value.Date);

        if (!includePrivate)
            query = query.Where(p => !p.IsPrivate);

        return query.ToList();
    }

    private int GetSelectedProjectCount()
    {
        return GetProjectsForExport().Count;
    }

    private decimal GetSelectedTotal()
    {
        return GetProjectsForExport().Sum(p => ParseAmount(p.GetTotal()));
    }

    private List<BankSlipProject> GetProjectsForExport()
    {
        var selectedCategories = categorySelections
            .Where(c => c.IsSelected)
            .Select(c => c.Name)
            .ToHashSet();

        var selectedSubCategories = categorySelections
            .SelectMany(c => c.SubCategories.Where(s => s.IsSelected).Select(s => (c.Name, s.Name)))
            .ToHashSet();

        return GetFilteredProjects()
            .Where(p =>
            {
                var catName = p.StructuredMemo?.CategoryName ?? "(Uncategorized)";
                var subName = p.StructuredMemo?.SubCategoryName;

                if (!selectedCategories.Contains(catName))
                    return false;

                if (!string.IsNullOrEmpty(subName))
                    return selectedSubCategories.Contains((catName, subName));

                return true;
            })
            .ToList();
    }

    private decimal ParseAmount(string? amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr)) return 0;
        var cleaned = amountStr.Replace(",", "").Replace("฿", "").Replace("THB", "").Trim();
        return decimal.TryParse(cleaned, out var amount) ? amount : 0;
    }

    #endregion

    #region Helper Classes

    public class CategorySelection
    {
        public string Name { get; set; } = "";
        public bool IsSelected { get; set; } = true;
        public bool IsExpanded { get; set; } = false;
        public int ProjectCount { get; set; }
        public List<SubCategorySelection> SubCategories { get; set; } = new();
    }

    public class SubCategorySelection
    {
        public string Name { get; set; } = "";
        public bool IsSelected { get; set; } = true;
        public int ProjectCount { get; set; }
    }

    #endregion
}