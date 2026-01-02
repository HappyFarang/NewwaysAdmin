// File: NewwaysAdmin.Mobile/ViewModels/Categories/CategoryBrowserViewModel.cs
// UPDATED: Added VAT toggle at transaction level (not fixed to subcategory)

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.SharedModels.Categories;

namespace NewwaysAdmin.Mobile.ViewModels.Categories
{
    public class CategoryBrowserViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ILogger<CategoryBrowserViewModel> _logger;
        private readonly CategoryDataService _categoryDataService;
        private readonly ConnectionState _connectionState;

        private bool _isLoading;
        private string _syncStatusText = "Loading...";
        private Color _connectionDotColor = Colors.Gray;
        private LocationItem? _selectedLocation;
        private PersonItem? _selectedPerson;
        private bool _includeVat = true;  // NEW: VAT toggle state

        public CategoryBrowserViewModel(
            ILogger<CategoryBrowserViewModel> logger,
            CategoryDataService categoryDataService,
            ConnectionState connectionState)
        {
            _logger = logger;
            _categoryDataService = categoryDataService;
            _connectionState = connectionState;

            // Initialize collections
            Categories = new ObservableCollection<CategoryDisplayItem>();
            Locations = new ObservableCollection<LocationItem>();
            Persons = new ObservableCollection<PersonItem>();

            // Commands
            ToggleCategoryCommand = new Command<CategoryDisplayItem?>(ToggleCategory);
            SelectSubCategoryCommand = new Command<SubCategoryDisplayItem?>(SelectSubCategory);
            RefreshCommand = new Command(async () => await LoadCategoriesAsync());
            ModuleTappedCommand = new Command(OnModuleTapped);

            // Subscribe to events
            _connectionState.OnConnectionChanged += OnConnectionStateChanged;
            _categoryDataService.DataUpdated += OnDataUpdated;
        }

        #region Properties

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowEmptyState)); }
        }

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

        public ObservableCollection<CategoryDisplayItem> Categories { get; }
        public ObservableCollection<LocationItem> Locations { get; }
        public ObservableCollection<PersonItem> Persons { get; }

        public bool HasCategories => Categories.Count > 0;
        public bool ShowEmptyState => !IsLoading && !HasCategories;

        public LocationItem? SelectedLocation
        {
            get => _selectedLocation;
            set { _selectedLocation = value; OnPropertyChanged(); }
        }

        public PersonItem? SelectedPerson
        {
            get => _selectedPerson;
            set { _selectedPerson = value; OnPropertyChanged(); }
        }

        // NEW: VAT toggle for current transaction
        public bool IncludeVat
        {
            get => _includeVat;
            set
            {
                _includeVat = value;
                OnPropertyChanged();
                _logger.LogDebug("VAT toggle changed to: {IncludeVat}", value);
            }
        }

        #endregion

        #region Commands

        public ICommand ToggleCategoryCommand { get; }
        public ICommand SelectSubCategoryCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ModuleTappedCommand { get; }

        #endregion

        #region Public Methods

        public async Task InitializeAsync()
        {
            _logger.LogInformation("CategoryBrowserViewModel initializing...");
            UpdateConnectionDot();
            await LoadCategoriesAsync();
        }

        public async Task LoadCategoriesAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                _logger.LogInformation("Loading categories...");

                var data = await _categoryDataService.GetDataAsync();

                if (data != null)
                {
                    PopulateFromData(data);
                    UpdateSyncStatus();
                }
                else
                {
                    _logger.LogWarning("No category data available");
                    SyncStatusText = "No data - pull to refresh";
                }

                OnPropertyChanged(nameof(HasCategories));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading categories");
                SyncStatusText = "Error loading data";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Dispose()
        {
            _connectionState.OnConnectionChanged -= OnConnectionStateChanged;
            _categoryDataService.DataUpdated -= OnDataUpdated;
        }

        #endregion

        #region Private Methods

        private void PopulateFromData(FullCategoryData data)
        {
            Categories.Clear();
            Locations.Clear();
            Persons.Clear();

            // Add "None" options at the top
            Locations.Add(new LocationItem { Id = "", Name = "No Location" });
            Persons.Add(new PersonItem { Id = "", Name = "No Person" });

            // Set defaults
            SelectedLocation = Locations.First();
            SelectedPerson = Persons.First();

            // === LOCATIONS ===
            foreach (var loc in data.Locations.Where(l => l.IsActive).OrderBy(l => l.SortOrder))
            {
                Locations.Add(new LocationItem { Id = loc.Id, Name = loc.Name });
            }

            // === PERSONS ===
            foreach (var person in data.Persons.Where(p => p.IsActive).OrderBy(p => p.SortOrder))
            {
                Persons.Add(new PersonItem { Id = person.Id, Name = person.Name });
            }

            // === CATEGORIES ===
            foreach (var category in data.Categories.Where(c => c.IsActive).OrderBy(c => c.SortOrder))
            {
                var displayCategory = new CategoryDisplayItem
                {
                    Id = category.Id,
                    Name = category.Name,
                    IsExpanded = false
                };

                foreach (var sub in category.SubCategories.Where(s => s.IsActive).OrderBy(s => s.SortOrder))
                {
                    displayCategory.SubCategories.Add(new SubCategoryDisplayItem
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        ParentCategoryId = category.Id,
                        ParentCategoryName = category.Name,
                        HasVAT = sub.HasVAT
                    });
                }

                Categories.Add(displayCategory);
            }

            _logger.LogInformation("Populated UI: {CatCount} categories, {LocCount} locations, {PerCount} persons",
                Categories.Count, Locations.Count - 1, Persons.Count - 1);
        }

        private void UpdateSyncStatus()
        {
            var lastSync = _categoryDataService.LastSyncTime;
            var version = _categoryDataService.LocalVersion;

            if (lastSync.HasValue)
            {
                var ago = DateTime.UtcNow - lastSync.Value;
                if (ago.TotalMinutes < 1)
                    SyncStatusText = $"v{version} - synced just now";
                else if (ago.TotalHours < 1)
                    SyncStatusText = $"v{version} - synced {(int)ago.TotalMinutes}m ago";
                else if (ago.TotalDays < 1)
                    SyncStatusText = $"v{version} - synced {(int)ago.TotalHours}h ago";
                else
                    SyncStatusText = $"v{version} - synced {(int)ago.TotalDays}d ago";
            }
            else
            {
                SyncStatusText = version > 0 ? $"v{version} - cached" : "No data";
            }
        }

        private void ToggleCategory(CategoryDisplayItem? category)
        {
            if (category == null) return;

            category.IsExpanded = !category.IsExpanded;
            _logger.LogDebug("Category {Name} expanded: {IsExpanded}", category.Name, category.IsExpanded);
        }

        private async void SelectSubCategory(SubCategoryDisplayItem? subCategory)
        {
            if (subCategory == null) return;

            // NEW: Auto-set VAT toggle based on subcategory's default (as a suggestion)
            // User can still override before copying
            IncludeVat = subCategory.HasVAT;

            // Build the note string
            var locationName = string.IsNullOrEmpty(SelectedLocation?.Id) ? "None" : SelectedLocation.Name;
            var personName = string.IsNullOrEmpty(SelectedPerson?.Id) ? "None" : SelectedPerson.Name;
            var categoryPath = $"{subCategory.ParentCategoryName} + {subCategory.Name}";

            // NEW: Include VAT status in the note
            var vatStatus = IncludeVat ? "VAT" : "NoVAT";

            // Format: Location - Person - Category - VAT status - Memo placeholder
            var noteText = $"Location: {locationName} - Person: {personName} - Category: {categoryPath} - {vatStatus} - Memo: ";

            _logger.LogInformation("Generated note: {Note}", noteText);

            // Copy to clipboard
            await Clipboard.Default.SetTextAsync(noteText);

            // Show confirmation with VAT status highlighted
            var vatMessage = IncludeVat ? "✓ With VAT" : "✗ No VAT";
            await Application.Current!.MainPage!.DisplayAlert(
                $"Copied! ({vatMessage})",
                noteText,
                "OK");
        }

        private void OnModuleTapped()
        {
            _logger.LogDebug("Module tapped - future: show module picker");
        }

        private void OnConnectionStateChanged(object? sender, bool isOnline)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateConnectionDot();

                // If back online and no data, try to load
                if (isOnline && Categories.Count == 0)
                {
                    _ = LoadCategoriesAsync();
                }
            });
        }

        private void OnDataUpdated(object? sender, FullCategoryData data)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _logger.LogInformation("Data updated notification received - refreshing UI");
                PopulateFromData(data);
                UpdateSyncStatus();
            });
        }

        private void UpdateConnectionDot()
        {
            ConnectionDotColor = _connectionState.IsOnline
                ? Colors.LimeGreen
                : Colors.Red;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    // ===== Display Models =====

    public class CategoryDisplayItem : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public ObservableCollection<SubCategoryDisplayItem> SubCategories { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class SubCategoryDisplayItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ParentCategoryId { get; set; } = "";
        public string ParentCategoryName { get; set; } = "";
        public bool HasVAT { get; set; }
    }

    public class LocationItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class PersonItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }
}