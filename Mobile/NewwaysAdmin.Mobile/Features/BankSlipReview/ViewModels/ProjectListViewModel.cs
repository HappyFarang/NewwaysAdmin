// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/ViewModels/ProjectListViewModel.cs
// ViewModel for bank slip project list with filtering and sorting

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Features.BankSlipReview.Services;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.ViewModels;

public class ProjectListViewModel : INotifyPropertyChanged
{
    private readonly ILogger<ProjectListViewModel> _logger;
    private readonly BankSlipReviewSyncService _syncService;
    private readonly BankSlipLocalStorage _localStorage;
    private readonly ConnectionState _connectionState;

    // Backing fields
    private bool _isLoading;
    private bool _isRefreshing;
    private bool _isFilterExpanded;
    private string _syncStatusText = "";
    private Color _connectionDotColor = Colors.Gray;
    private string _selectedFilter = "All";
    private string _selectedSort = "Date";
    private string? _selectedPerson;
    private int _totalCount;
    private int _filteredCount;

    // All projects (unfiltered)
    private List<BankSlipProject> _allProjects = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectListViewModel(
        ILogger<ProjectListViewModel> logger,
        BankSlipReviewSyncService syncService,
        BankSlipLocalStorage localStorage,
        ConnectionState connectionState)
    {
        _logger = logger;
        _syncService = syncService;
        _localStorage = localStorage;
        _connectionState = connectionState;

        // Commands
        RefreshCommand = new Command(async () => await RefreshAsync());
        ToggleFilterCommand = new Command(() => IsFilterExpanded = !IsFilterExpanded);
        SetFilterCommand = new Command<string>(SetFilter);
        SetSortCommand = new Command<string>(SetSort);
        SetPersonFilterCommand = new Command<string?>(SetPersonFilter);
        OpenProjectCommand = new Command<ProjectListItem>(async (p) => await OpenProjectAsync(p));
        SyncNowCommand = new Command(async () => await SyncNowAsync());

        // Subscribe to events
        _connectionState.OnConnectionChanged += OnConnectionStateChanged;
        _syncService.SyncProgress += OnSyncProgress;
        _syncService.SyncComplete += OnSyncComplete;
        _syncService.ProjectChanged += OnProjectChanged;

        UpdateConnectionDot();
    }

    #region Properties

    public ObservableCollection<ProjectListItem> Projects { get; } = new();
    public ObservableCollection<string> AvailablePersons { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set { _isRefreshing = value; OnPropertyChanged(); }
    }

    public bool IsFilterExpanded
    {
        get => _isFilterExpanded;
        set { _isFilterExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilterExpandIcon)); }
    }

    public string FilterExpandIcon => IsFilterExpanded ? "▼" : "▶";

    public string SyncStatusText
    {
        get => _syncStatusText;
        set { _syncStatusText = value; OnPropertyChanged(); }
    }

    public Color ConnectionDotColor
    {
        get => _connectionDotColor;
        set { _connectionDotColor = value; OnPropertyChanged(); }
    }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set { _selectedFilter = value; OnPropertyChanged(); ApplyFiltersAndSort(); }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set { _selectedSort = value; OnPropertyChanged(); ApplyFiltersAndSort(); }
    }

    public string? SelectedPerson
    {
        get => _selectedPerson;
        set { _selectedPerson = value; OnPropertyChanged(); ApplyFiltersAndSort(); }
    }

    public int TotalCount
    {
        get => _totalCount;
        set { _totalCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(CountSummary)); }
    }

    public int FilteredCount
    {
        get => _filteredCount;
        set { _filteredCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(CountSummary)); }
    }

    public string CountSummary => TotalCount == FilteredCount
        ? $"{TotalCount} projects"
        : $"{FilteredCount} of {TotalCount} projects";

    public bool IsOnline => _connectionState.IsOnline;

    #endregion

    #region Commands

    public ICommand RefreshCommand { get; }
    public ICommand ToggleFilterCommand { get; }
    public ICommand SetFilterCommand { get; }
    public ICommand SetSortCommand { get; }
    public ICommand SetPersonFilterCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand SyncNowCommand { get; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Load data - call from OnAppearing
    /// </summary>
    public async Task LoadDataAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        SyncStatusText = "Loading...";

        try
        {
            // Load from local storage first (instant)
            await LoadLocalProjectsAsync();

            // Load available persons for filter
            await LoadAvailablePersonsAsync();

            // If online, sync with server
            if (_connectionState.IsOnline)
            {
                await SyncNowAsync();
            }
            else
            {
                SyncStatusText = "Offline - showing cached data";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project data");
            SyncStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refresh from server
    /// </summary>
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;

        try
        {
            if (_connectionState.IsOnline)
            {
                await SyncNowAsync();
            }
            else
            {
                await LoadLocalProjectsAsync();
                SyncStatusText = "Offline - refreshed from cache";
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    #endregion

    #region Private Methods - Data Loading

    private async Task LoadLocalProjectsAsync()
    {
        try
        {
            _allProjects = await _localStorage.GetAllProjectsAsync();
            TotalCount = _allProjects.Count;
            ApplyFiltersAndSort();

            _logger.LogDebug("Loaded {Count} projects from local storage", _allProjects.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading local projects");
        }
    }

    private async Task LoadAvailablePersonsAsync()
    {
        try
        {
            var persons = await _localStorage.LoadAvailablePersonsAsync();

            AvailablePersons.Clear();
            AvailablePersons.Add("All"); // First option
            foreach (var person in persons.OrderBy(p => p))
            {
                AvailablePersons.Add(person);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading available persons");
        }
    }

    private async Task SyncNowAsync()
    {
        if (_syncService.IsSyncing)
        {
            SyncStatusText = "Sync in progress...";
            return;
        }

        SyncStatusText = "Syncing...";

        var result = await _syncService.SyncWithServerAsync();

        if (result.Success)
        {
            await LoadLocalProjectsAsync();
            await LoadAvailablePersonsAsync();

            var lastSync = await _localStorage.LoadLastSyncTimeAsync();
            SyncStatusText = lastSync.HasValue
                ? $"Synced {FormatTimeAgo(lastSync.Value)}"
                : "Synced";
        }
        else if (!result.Skipped)
        {
            SyncStatusText = $"Sync failed: {result.ErrorMessage}";
        }
    }

    #endregion

    #region Private Methods - Filtering & Sorting

    private void SetFilter(string filter)
    {
        SelectedFilter = filter;
    }

    private void SetSort(string sort)
    {
        SelectedSort = sort;
    }

    private void SetPersonFilter(string? person)
    {
        SelectedPerson = person == "All" ? null : person;
    }

    private void ApplyFiltersAndSort()
    {
        var filtered = _allProjects.AsEnumerable();

        // Apply status filter
        filtered = SelectedFilter switch
        {
            "NeedsBill" => filtered.Where(p => !p.HasBill && !p.IsClosed),
            "NeedsCategory" => filtered.Where(p => !p.HasStructuralNote && !p.IsClosed),
            "Open" => filtered.Where(p => !p.IsClosed),
            "Closed" => filtered.Where(p => p.IsClosed),
            _ => filtered // "All"
        };

        // Apply person filter
        if (!string.IsNullOrEmpty(SelectedPerson))
        {
            filtered = filtered.Where(p =>
                p.StructuredMemo?.PersonName?.Equals(SelectedPerson, StringComparison.OrdinalIgnoreCase) == true);
        }

        // Apply sort
        filtered = SelectedSort switch
        {
            "Person" => filtered.OrderBy(p => p.StructuredMemo?.PersonName ?? "zzz")
                                .ThenByDescending(p => p.TransactionTimestamp),
            "Amount" => filtered.OrderByDescending(p => ParseAmount(p)),
            "Recipient" => filtered.OrderBy(p => p.GetRecipient() ?? "zzz")
                                   .ThenByDescending(p => p.TransactionTimestamp),
            _ => filtered.OrderByDescending(p => p.TransactionTimestamp) // "Date" default
        };

        // Convert to display items
        var items = filtered.Select(p => new ProjectListItem(p)).ToList();
        FilteredCount = items.Count;

        // Update observable collection
        Projects.Clear();
        foreach (var item in items)
        {
            Projects.Add(item);
        }
    }

    private decimal ParseAmount(BankSlipProject project)
    {
        var totalStr = project.GetTotal();
        if (string.IsNullOrEmpty(totalStr)) return 0;

        var clean = totalStr.Replace(",", "").Replace("฿", "").Replace("THB", "").Trim();
        return decimal.TryParse(clean, out var amount) ? amount : 0;
    }

    #endregion

    #region Private Methods - Navigation

    private async Task OpenProjectAsync(ProjectListItem item)
    {
        if (item == null) return;

        try
        {
            // Navigate to detail page with project ID
            await Shell.Current.GoToAsync($"projectDetail?projectId={item.ProjectId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to project detail");
        }
    }

    #endregion

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool isOnline)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateConnectionDot();
            OnPropertyChanged(nameof(IsOnline));

            if (isOnline && !_syncService.IsSyncing)
            {
                _ = SyncNowAsync();
            }
        });
    }

    private void OnSyncProgress(object? sender, SyncProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            SyncStatusText = e.Message;
        });
    }

    private void OnSyncComplete(object? sender, SyncCompleteEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (e.Result.Success)
            {
                await LoadLocalProjectsAsync();
            }
        });
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Reload to reflect changes
            await LoadLocalProjectsAsync();
        });
    }

    private void UpdateConnectionDot()
    {
        ConnectionDotColor = _connectionState.IsOnline ? Colors.Green : Colors.Red;
    }

    #endregion

    #region Helpers

    private string FormatTimeAgo(DateTime time)
    {
        var diff = DateTime.UtcNow - time;

        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

#region Display Item

/// <summary>
/// Display item for project list - wraps BankSlipProject with UI-friendly properties
/// </summary>
public class ProjectListItem : INotifyPropertyChanged
{
    private readonly BankSlipProject _project;

    public ProjectListItem(BankSlipProject project)
    {
        _project = project;
    }

    public string ProjectId => _project.ProjectId;
    public DateTime TransactionDate => _project.TransactionTimestamp;
    public string DateDisplay => _project.TransactionTimestamp.ToString("dd MMM yyyy HH:mm");
    public string? Recipient => _project.GetRecipient();
    public string? Amount => _project.GetTotal();
    public string? PersonName => _project.StructuredMemo?.PersonName;
    public string? CategoryName => _project.StructuredMemo?.CategoryName;
    public string? SubCategoryName => _project.StructuredMemo?.SubCategoryName;
    public string? LocationName => _project.StructuredMemo?.LocationName;

    // Status flags
    public bool HasBill => _project.HasBill;
    public bool HasStructuralNote => _project.HasStructuralNote;
    public bool IsClosed => _project.IsClosed;
    public bool IsPrivate => _project.IsPrivate;
    public bool? HasVat => _project.HasVat;

    // Display helpers
    public string CategoryDisplay => string.IsNullOrEmpty(CategoryName)
        ? "No category"
        : string.IsNullOrEmpty(SubCategoryName)
            ? CategoryName
            : $"{CategoryName} → {SubCategoryName}";

    public string PersonDisplay => string.IsNullOrEmpty(PersonName) ? "-" : PersonName;

    public string StatusIcons
    {
        get
        {
            var icons = new List<string>();
            if (IsClosed) icons.Add("✓");
            if (!HasBill) icons.Add("📷");
            if (!HasStructuralNote) icons.Add("⚠");
            if (IsPrivate) icons.Add("🔒");
            return string.Join(" ", icons);
        }
    }

    public Color StatusColor
    {
        get
        {
            if (IsClosed) return Colors.Green;
            if (!HasBill || !HasStructuralNote) return Colors.Orange;
            return Colors.Gray;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

#endregion