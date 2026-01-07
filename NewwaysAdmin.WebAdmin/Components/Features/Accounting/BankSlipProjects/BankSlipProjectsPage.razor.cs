// File: NewwaysAdmin.WebAdmin/Components/Features/Accounting/BankSlipProjects/BankSlipProjectsPage.razor.cs

using Microsoft.AspNetCore.Components;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;
using NewwaysAdmin.WebAdmin.Components.Features.Accounting.BankSlipProjects.Components;
using NewwaysAdmin.WebAdmin.Components.Features.Accounting.BankSlipProjects.Modals;

namespace NewwaysAdmin.WebAdmin.Components.Features.Accounting.BankSlipProjects;

public partial class BankSlipProjectsPage : ComponentBase
{
    [Inject] private BankSlipProjectService ProjectService { get; set; } = default!;
    [Inject] private ILogger<BankSlipProjectsPage> Logger { get; set; } = default!;

    // Modal references
    private ReviewModal? reviewModal;
    private ExportModal? exportModal;
    private ImageViewerModal? imageViewerModal;

    // Data
    private List<BankSlipProject> allProjects = new();
    private List<BankSlipProject> filteredProjects = new();
    private List<string> availableUsers = new();
    private List<CategoryInfo> availableCategories = new();

    // Filters
    private DateTime? filterDateFrom;
    private DateTime? filterDateTo;
    private string filterStatus = "all";
    private string filterUser = "all";
    private string filterCategory = "all";
    private string filterSearch = "";
    private bool showPrivate = false;

    // Sorting
    private string sortColumn = "date";
    private bool sortAscending = false;

    // Stats
    private int reviewCount;
    private decimal totalAmount;

    // State
    private bool isLoading = true;

    protected override async Task OnInitializedAsync()
    {
        await LoadProjectsAsync();
    }

    private async Task LoadProjectsAsync()
    {
        isLoading = true;
        StateHasChanged();

        try
        {
            allProjects = await ProjectService.GetAllProjectsAsync();
            ExtractFilterOptions();

            // Default: current month
            var now = DateTime.Now;
            filterDateFrom = new DateTime(now.Year, now.Month, 1);
            filterDateTo = filterDateFrom.Value.AddMonths(1).AddDays(-1);

            ApplyFilters();

            Logger.LogInformation("Loaded {Count} projects", allProjects.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading projects");
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    private void ExtractFilterOptions()
    {
        availableUsers = allProjects
            .Select(p => p.Username)
            .Where(u => !string.IsNullOrEmpty(u))
            .Distinct()
            .OrderBy(u => u)
            .ToList();

        var categoryDict = new Dictionary<string, HashSet<string>>();

        foreach (var project in allProjects.Where(p => p.StructuredMemo != null))
        {
            var catName = project.StructuredMemo!.CategoryName;
            var subCatName = project.StructuredMemo!.SubCategoryName;

            if (!string.IsNullOrEmpty(catName))
            {
                if (!categoryDict.ContainsKey(catName))
                    categoryDict[catName] = new HashSet<string>();

                if (!string.IsNullOrEmpty(subCatName))
                    categoryDict[catName].Add(subCatName);
            }
        }

        availableCategories = categoryDict
            .Select(kvp => new CategoryInfo
            {
                Name = kvp.Key,
                SubCategories = kvp.Value.OrderBy(s => s).ToList()
            })
            .OrderBy(c => c.Name)
            .ToList();
    }

    #region Filter Callbacks

    private void OnDateFromChanged(DateTime? value) => filterDateFrom = value;
    private void OnDateToChanged(DateTime? value) => filterDateTo = value;
    private void OnStatusFilterChanged(string value) => filterStatus = value;
    private void OnUserFilterChanged(string value) => filterUser = value;
    private void OnCategoryFilterChanged(string value) => filterCategory = value;
    private void OnSearchTextChanged(string value) => filterSearch = value;
    private void OnShowPrivateChanged(bool value) => showPrivate = value;

    private void ApplyFilters()
    {
        var query = allProjects.AsEnumerable();

        if (filterDateFrom.HasValue)
            query = query.Where(p => p.TransactionTimestamp.Date >= filterDateFrom.Value.Date);

        if (filterDateTo.HasValue)
            query = query.Where(p => p.TransactionTimestamp.Date <= filterDateTo.Value.Date);

        query = filterStatus switch
        {
            "review" => query.Where(p => !p.IsClosed),
            "closed" => query.Where(p => p.IsClosed),
            _ => query
        };

        if (filterUser != "all")
            query = query.Where(p => p.Username == filterUser);

        if (filterCategory != "all")
            query = query.Where(p => p.StructuredMemo?.CategoryName == filterCategory);

        if (!string.IsNullOrWhiteSpace(filterSearch))
        {
            var search = filterSearch.ToLower();
            query = query.Where(p =>
                p.GetRecipient().ToLower().Contains(search) ||
                p.GetNote().ToLower().Contains(search) ||
                (p.StructuredMemo?.Memo?.ToLower().Contains(search) ?? false));
        }

        if (!showPrivate)
            query = query.Where(p => !p.IsPrivate);

        query = ApplySorting(query);

        filteredProjects = query.ToList();
        reviewCount = filteredProjects.Count(p => !p.IsClosed);
        totalAmount = filteredProjects.Sum(p => ParseAmount(p.GetTotal()));

        StateHasChanged();
    }

    private IEnumerable<BankSlipProject> ApplySorting(IEnumerable<BankSlipProject> query)
    {
        return sortColumn switch
        {
            "date" => sortAscending
                ? query.OrderBy(p => p.TransactionTimestamp)
                : query.OrderByDescending(p => p.TransactionTimestamp),
            "amount" => sortAscending
                ? query.OrderBy(p => ParseAmount(p.GetTotal()))
                : query.OrderByDescending(p => ParseAmount(p.GetTotal())),
            "recipient" => sortAscending
                ? query.OrderBy(p => p.GetRecipient())
                : query.OrderByDescending(p => p.GetRecipient()),
            "category" => sortAscending
                ? query.OrderBy(p => p.StructuredMemo?.CategoryName ?? "zzz")
                : query.OrderByDescending(p => p.StructuredMemo?.CategoryName ?? ""),
            "status" => sortAscending
                ? query.OrderBy(p => p.IsClosed).ThenBy(p => p.TransactionTimestamp)
                : query.OrderByDescending(p => p.IsClosed).ThenByDescending(p => p.TransactionTimestamp),
            _ => query.OrderByDescending(p => p.TransactionTimestamp)
        };
    }

    private void HandleSortChanged((string column, bool ascending) sort)
    {
        sortColumn = sort.column;
        sortAscending = sort.ascending;
        ApplyFilters();
    }

    #endregion

    #region Modal Handlers

    private async Task OpenReviewModal(BankSlipProject project)
    {
        if (reviewModal != null)
            await reviewModal.OpenAsync(project);
    }

    private async Task OpenExportModal()
    {
        if (exportModal != null)
            await exportModal.OpenAsync(filterDateFrom, filterDateTo);
    }

    private async Task HandleViewBankSlip(string projectId)
    {
        if (imageViewerModal != null)
            await imageViewerModal.OpenBankSlipAsync(projectId);
    }

    private async Task HandleViewBills(string projectId)
    {
        if (imageViewerModal != null)
            await imageViewerModal.OpenBillsAsync(projectId);
    }

    private async Task HandleProjectSaved(BankSlipProject project)
    {
        var index = allProjects.FindIndex(p => p.ProjectId == project.ProjectId);
        if (index >= 0)
            allProjects[index] = project;

        ExtractFilterOptions();
        ApplyFilters();
    }

    #endregion

    #region Helpers

    private decimal ParseAmount(string amountStr)
    {
        if (string.IsNullOrWhiteSpace(amountStr))
            return 0;

        var cleaned = amountStr
            .Replace(",", "")
            .Replace("฿", "")
            .Replace("THB", "")
            .Trim();

        return decimal.TryParse(cleaned, out var amount) ? amount : 0;
    }

    #endregion
}

/// <summary>
/// Category info for filter dropdowns and export
/// </summary>
public class CategoryInfo
{
    public string Name { get; set; } = "";
    public List<string> SubCategories { get; set; } = new();
    public int Count { get; set; }
}