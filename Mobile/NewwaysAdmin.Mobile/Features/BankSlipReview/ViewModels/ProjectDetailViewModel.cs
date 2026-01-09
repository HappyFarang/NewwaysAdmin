// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/ViewModels/ProjectDetailViewModel.cs
// ViewModel for viewing and editing a single bank slip project

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Features.BankSlipReview.Services;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SharedModels.Categories;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.ViewModels;

[QueryProperty(nameof(ProjectId), "projectId")]
public class ProjectDetailViewModel : INotifyPropertyChanged
{
    private readonly ILogger<ProjectDetailViewModel> _logger;
    private readonly BankSlipReviewSyncService _syncService;
    private readonly BankSlipLocalStorage _localStorage;
    private readonly CategoryDataService _categoryDataService;
    private readonly ConnectionState _connectionState;

    // Category data cache
    private FullCategoryData? _categoryData;

    // Backing fields
    private string _projectId = "";
    private BankSlipProject? _project;
    private bool _isLoading;
    private bool _isSaving;
    private bool _hasChanges;
    private string _statusMessage = "";
    private byte[]? _bankSlipImage;
    private string? _selectedCategoryName;
    private string? _selectedSubCategoryName;
    private string? _selectedLocationName;
    private bool _hasVat;
    private bool _isPrivate;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectDetailViewModel(
        ILogger<ProjectDetailViewModel> logger,
        BankSlipReviewSyncService syncService,
        BankSlipLocalStorage localStorage,
        CategoryDataService categoryDataService,
        ConnectionState connectionState)
    {
        _logger = logger;
        _syncService = syncService;
        _localStorage = localStorage;
        _categoryDataService = categoryDataService;
        _connectionState = connectionState;

        // Commands
        SaveCommand = new Command(async () => await SaveAsync(), () => HasChanges && !IsSaving);
        CloseProjectCommand = new Command(async () => await CloseProjectAsync(), () => !IsClosed && !IsSaving);
        TakeBillPhotoCommand = new Command(async () => await TakeBillPhotoAsync());
        PickBillFromGalleryCommand = new Command(async () => await PickBillFromGalleryAsync());
        ViewBankSlipCommand = new Command(async () => await ViewBankSlipAsync());
        ViewBillCommand = new Command<string>(async (billId) => await ViewBillAsync(billId));
        GoBackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
        ToggleVatCommand = new Command(() => { HasVat = !HasVat; });
        TogglePrivateCommand = new Command(() => { IsPrivate = !IsPrivate; });
    }

    #region Properties - Core

    public string ProjectId
    {
        get => _projectId;
        set
        {
            _projectId = value;
            OnPropertyChanged();
            // Load when ID is set (from navigation)
            _ = LoadProjectAsync();
        }
    }

    public BankSlipProject? Project
    {
        get => _project;
        set { _project = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasProject)); }
    }

    public bool HasProject => Project != null;

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsSaving
    {
        get => _isSaving;
        set { _isSaving = value; OnPropertyChanged(); ((Command)SaveCommand).ChangeCanExecute(); }
    }

    public bool HasChanges
    {
        get => _hasChanges;
        set { _hasChanges = value; OnPropertyChanged(); ((Command)SaveCommand).ChangeCanExecute(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsOnline => _connectionState.IsOnline;

    #endregion

    #region Properties - Display (Read-only from project)

    public string DateDisplay => Project?.TransactionTimestamp.ToString("dd MMM yyyy HH:mm") ?? "";
    public string? Recipient => Project?.GetRecipient();
    public string? Amount => Project?.GetTotal();
    public string? Note => Project?.GetNote();
    public string? Memo => Project?.StructuredMemo?.Memo;
    public string? PersonName => Project?.StructuredMemo?.PersonName;
    public bool IsClosed => Project?.IsClosed ?? false;
    public bool HasBill => Project?.HasBill ?? false;

    #endregion

    #region Properties - Editable

    public bool HasVat
    {
        get => _hasVat;
        set
        {
            if (_hasVat != value)
            {
                _hasVat = value;
                OnPropertyChanged();
                MarkChanged();
            }
        }
    }

    public bool IsPrivate
    {
        get => _isPrivate;
        set
        {
            if (_isPrivate != value)
            {
                _isPrivate = value;
                OnPropertyChanged();
                MarkChanged();
            }
        }
    }

    public string? SelectedCategoryName
    {
        get => _selectedCategoryName;
        set
        {
            if (_selectedCategoryName != value)
            {
                _selectedCategoryName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CategoryDisplay));
                // Reset subcategory when category changes
                SelectedSubCategoryName = null;
                LoadSubCategories();
                MarkChanged();
            }
        }
    }

    public string? SelectedSubCategoryName
    {
        get => _selectedSubCategoryName;
        set
        {
            if (_selectedSubCategoryName != value)
            {
                _selectedSubCategoryName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CategoryDisplay));
                MarkChanged();
            }
        }
    }

    public string? SelectedLocationName
    {
        get => _selectedLocationName;
        set
        {
            if (_selectedLocationName != value)
            {
                _selectedLocationName = value;
                OnPropertyChanged();
                MarkChanged();
            }
        }
    }

    public string CategoryDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(SelectedCategoryName)) return "Select category...";
            if (string.IsNullOrEmpty(SelectedSubCategoryName)) return SelectedCategoryName;
            return $"{SelectedCategoryName} → {SelectedSubCategoryName}";
        }
    }

    #endregion

    #region Properties - Collections

    public ObservableCollection<string> CategoryNames { get; } = new();
    public ObservableCollection<string> SubCategoryNames { get; } = new();
    public ObservableCollection<string> LocationNames { get; } = new();
    public ObservableCollection<BillItem> Bills { get; } = new();

    public byte[]? BankSlipImage
    {
        get => _bankSlipImage;
        set { _bankSlipImage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBankSlipImage)); }
    }

    public bool HasBankSlipImage => BankSlipImage != null;

    #endregion

    #region Commands

    public ICommand SaveCommand { get; }
    public ICommand CloseProjectCommand { get; }
    public ICommand TakeBillPhotoCommand { get; }
    public ICommand PickBillFromGalleryCommand { get; }
    public ICommand ViewBankSlipCommand { get; }
    public ICommand ViewBillCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand ToggleVatCommand { get; }
    public ICommand TogglePrivateCommand { get; }

    #endregion

    #region Load Data

    private async Task LoadProjectAsync()
    {
        if (string.IsNullOrEmpty(ProjectId)) return;

        IsLoading = true;
        StatusMessage = "Loading...";

        try
        {
            // Load project from local storage
            Project = await _localStorage.LoadProjectAsync(ProjectId);

            if (Project == null)
            {
                StatusMessage = "Project not found";
                return;
            }

            // Initialize editable fields from project
            HasVat = Project.HasVat ?? false;
            IsPrivate = Project.IsPrivate;
            SelectedCategoryName = Project.StructuredMemo?.CategoryName;
            SelectedSubCategoryName = Project.StructuredMemo?.SubCategoryName;
            SelectedLocationName = Project.StructuredMemo?.LocationName;

            // Load categories for dropdowns
            await LoadCategoriesAsync();

            // Load bills
            await LoadBillsAsync();

            // Load bank slip image (async, don't block)
            _ = LoadBankSlipImageAsync();

            HasChanges = false;
            StatusMessage = "";

            // Notify all display properties
            NotifyDisplayPropertiesChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading project {ProjectId}", ProjectId);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            _categoryData = await _categoryDataService.GetDataAsync();
            if (_categoryData == null) return;

            // Populate category names
            CategoryNames.Clear();
            foreach (var cat in _categoryData.Categories.OrderBy(c => c.Name))
            {
                CategoryNames.Add(cat.Name);
            }

            // Populate location names
            LocationNames.Clear();
            foreach (var loc in _categoryData.Locations.OrderBy(l => l.Name))
            {
                LocationNames.Add(loc.Name);
            }

            // Load subcategories for current selection
            LoadSubCategories();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading categories");
        }
    }

    private void LoadSubCategories()
    {
        SubCategoryNames.Clear();

        if (string.IsNullOrEmpty(SelectedCategoryName) || _categoryData == null) return;

        try
        {
            var category = _categoryData.Categories.FirstOrDefault(
                c => c.Name.Equals(SelectedCategoryName, StringComparison.OrdinalIgnoreCase));

            if (category?.SubCategories != null)
            {
                foreach (var sub in category.SubCategories.OrderBy(s => s.Name))
                {
                    SubCategoryNames.Add(sub.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading subcategories");
        }
    }

    private async Task LoadBillsAsync()
    {
        Bills.Clear();

        var billIds = _localStorage.GetBillIdsForProject(ProjectId);

        foreach (var billId in billIds)
        {
            Bills.Add(new BillItem
            {
                BillId = billId,
                DisplayName = $"Bill {Bills.Count + 1}"
            });
        }
    }

    private async Task LoadBankSlipImageAsync()
    {
        try
        {
            BankSlipImage = await _syncService.GetBankSlipImageAsync(ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading bank slip image");
        }
    }

    #endregion

    #region Save & Close

    private async Task SaveAsync()
    {
        if (Project == null || !HasChanges) return;

        IsSaving = true;
        StatusMessage = "Saving...";

        try
        {
            // Update project with edits
            Project.HasVat = HasVat;
            Project.IsPrivate = IsPrivate;

            if (Project.StructuredMemo == null)
            {
                Project.StructuredMemo = new ParsedMemo();
            }

            // Store by name (ParsedMemo uses names, not IDs)
            Project.StructuredMemo.CategoryName = SelectedCategoryName;
            Project.StructuredMemo.SubCategoryName = SelectedSubCategoryName;
            Project.StructuredMemo.LocationName = SelectedLocationName;
            Project.HasStructuralNote = !string.IsNullOrEmpty(SelectedCategoryName);
            Project.ProcessedAt = DateTime.UtcNow;

            // Save locally
            await _localStorage.SaveProjectAsync(Project);

            // Push to server if online
            if (_connectionState.IsOnline)
            {
                var pushed = await _syncService.PushProjectAsync(ProjectId);
                StatusMessage = pushed ? "Saved & synced" : "Saved locally (will sync later)";
            }
            else
            {
                StatusMessage = "Saved locally (offline)";
            }

            HasChanges = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving project");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task CloseProjectAsync()
    {
        if (Project == null || IsClosed) return;

        var confirm = await Shell.Current.DisplayAlert(
            "Close Project",
            "Mark this project as reviewed and closed?",
            "Yes, Close",
            "Cancel");

        if (!confirm) return;

        IsSaving = true;
        StatusMessage = "Closing...";

        try
        {
            // Save any pending changes first
            if (HasChanges)
            {
                await SaveAsync();
            }

            var success = await _syncService.CloseProjectAsync(ProjectId);

            if (success)
            {
                // Reload to reflect changes
                await LoadProjectAsync();
                StatusMessage = "Project closed";

                // Navigate back after short delay
                await Task.Delay(500);
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                StatusMessage = "Failed to close project";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing project");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    #endregion

    #region Bill Photo

    private async Task TakeBillPhotoAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await Shell.Current.DisplayAlert("Error", "Camera not supported on this device", "OK");
                return;
            }

            var photo = await MediaPicker.Default.CapturePhotoAsync();
            if (photo != null)
            {
                await UploadBillAsync(photo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error taking photo");
            await Shell.Current.DisplayAlert("Error", $"Failed to take photo: {ex.Message}", "OK");
        }
    }

    private async Task PickBillFromGalleryAsync()
    {
        try
        {
            var photo = await MediaPicker.Default.PickPhotoAsync();
            if (photo != null)
            {
                await UploadBillAsync(photo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error picking photo");
            await Shell.Current.DisplayAlert("Error", $"Failed to pick photo: {ex.Message}", "OK");
        }
    }

    private async Task UploadBillAsync(FileResult photo)
    {
        StatusMessage = "Uploading bill...";

        try
        {
            using var stream = await photo.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            var result = await _syncService.UploadBillAsync(ProjectId, imageBytes, photo.FileName);

            if (result.Success)
            {
                StatusMessage = $"Bill {result.BillNumber} uploaded";
                await LoadBillsAsync();

                // Reload project to update HasBill
                var project = await _localStorage.LoadProjectAsync(ProjectId);
                if (project != null)
                {
                    Project = project;
                    NotifyDisplayPropertiesChanged();
                }
            }
            else if (result.Queued)
            {
                StatusMessage = "Bill queued (will upload when online)";
                await LoadBillsAsync();
            }
            else
            {
                StatusMessage = $"Upload failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading bill");
            StatusMessage = $"Upload error: {ex.Message}";
        }
    }

    #endregion

    #region View Images

    private async Task ViewBankSlipAsync()
    {
        if (BankSlipImage == null)
        {
            StatusMessage = "Loading image...";
            await LoadBankSlipImageAsync();
        }

        if (BankSlipImage == null)
        {
            await Shell.Current.DisplayAlert("Error", "Could not load bank slip image", "OK");
            return;
        }

        // TODO: Navigate to full-screen image viewer
        // For now, just show a simple popup
        await Shell.Current.DisplayAlert("Bank Slip", "Full-screen viewer coming soon!", "OK");
    }

    private async Task ViewBillAsync(string billId)
    {
        if (string.IsNullOrEmpty(billId)) return;

        StatusMessage = "Loading bill...";

        var billImage = await _syncService.GetBillImageAsync(billId);

        if (billImage == null)
        {
            await Shell.Current.DisplayAlert("Error", "Could not load bill image", "OK");
            StatusMessage = "";
            return;
        }

        // TODO: Navigate to full-screen image viewer
        await Shell.Current.DisplayAlert("Bill", "Full-screen viewer coming soon!", "OK");
        StatusMessage = "";
    }

    #endregion

    #region Helpers

    private void MarkChanged()
    {
        if (!IsLoading)
        {
            HasChanges = true;
        }
    }

    private void NotifyDisplayPropertiesChanged()
    {
        OnPropertyChanged(nameof(DateDisplay));
        OnPropertyChanged(nameof(Recipient));
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Memo));
        OnPropertyChanged(nameof(PersonName));
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(HasBill));
        OnPropertyChanged(nameof(CategoryDisplay));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}

#region Helper Items

public class BillItem
{
    public string BillId { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

#endregion