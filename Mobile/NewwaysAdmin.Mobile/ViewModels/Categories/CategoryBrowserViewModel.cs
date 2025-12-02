// File: Mobile/NewwaysAdmin.Mobile/ViewModels/CategoryBrowserViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.Mobile.Services.Categories;

/* Unmerged change from project 'NewwaysAdmin.Mobile (net8.0-windows10.0.19041.0)'
Added:
using NewwaysAdmin;
using NewwaysAdmin.Mobile;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.ViewModels.Categories;
*/

namespace NewwaysAdmin.Mobile.ViewModels.Categories
{
    public class CategoryBrowserViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<CategoryBrowserViewModel> _logger;
        private readonly ConnectionState _connectionState;
        private readonly CategoryDataService _categoryDataService;
        private readonly CategoryHubConnector _hubConnector;

        private bool _isLoading;
        private Color _connectionDotColor = Colors.Gray;
        private LocationDisplayItem? _selectedLocation;
        private PersonDisplayItem? _selectedPerson;
        private string _syncStatusText = "";

        private bool _isHeaderExpanded = false;

        public CategoryBrowserViewModel(
            ILogger<CategoryBrowserViewModel> logger,
            ConnectionState connectionState,
            CategoryDataService categoryDataService,
            CategoryHubConnector hubConnector)
        {
            _logger = logger;
            _connectionState = connectionState;
            _categoryDataService = categoryDataService;
            _hubConnector = hubConnector;

            // Commands
            ToggleCategoryCommand = new Command<CategoryDisplayItem>(ToggleCategory);
            SelectSubCategoryCommand = new Command<SubCategoryDisplayItem>(SelectSubCategory);
            ModuleTappedCommand = new Command(OnModuleTapped);
            RefreshCommand = new Command(async () => await RefreshDataAsync());
            ToggleHeaderCommand = new Command(ToggleHeader);
            NavigateToEditCommand = new Command(async () => await NavigateToEditAsync());

            // Subscribe to connection changes
            _connectionState.OnConnectionChanged += OnConnectionStateChanged;

            // Subscribe to data updates
            _categoryDataService.DataUpdated += OnDataUpdated;

            UpdateConnectionDot();
        }

        #region Properties

        public ObservableCollection<CategoryDisplayItem> Categories { get; } = new();
        public ObservableCollection<LocationDisplayItem> Locations { get; } = new();
        public ObservableCollection<PersonDisplayItem> Persons { get; } = new();

        public LocationDisplayItem? SelectedLocation
        {
            get => _selectedLocation;
            set
            {
                _selectedLocation = value;
                OnPropertyChanged();
                _logger.LogDebug("Location selected: {Location}", value?.Name ?? "None");
            }
        }
        public bool IsHeaderExpanded
        {
            get => _isHeaderExpanded;
            set
            {
                _isHeaderExpanded = value;
                OnPropertyChanged();
            }
        }

        public PersonDisplayItem? SelectedPerson
        {
            get => _selectedPerson;
            set
            {
                _selectedPerson = value;
                OnPropertyChanged();
                _logger.LogDebug("Person selected: {Person}", value?.Name ?? "None");
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCategories));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }

        public bool HasCategories => !IsLoading && Categories.Count > 0;
        public bool ShowEmptyState => !IsLoading && Categories.Count == 0;

        public Color ConnectionDotColor
        {
            get => _connectionDotColor;
            set
            {
                _connectionDotColor = value;
                OnPropertyChanged();
            }
        }

        public string SyncStatusText
        {
            get => _syncStatusText;
            set
            {
                _syncStatusText = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Commands

        public ICommand ToggleCategoryCommand { get; }
        public ICommand SelectSubCategoryCommand { get; }
        public ICommand ModuleTappedCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ToggleHeaderCommand { get; }
        public ICommand NavigateToEditCommand { get; }
        #endregion

        #region Methods

        private void ToggleHeader()
        {
            IsHeaderExpanded = !IsHeaderExpanded;
            _logger.LogDebug("Header expanded: {IsExpanded}", IsHeaderExpanded);
        }

        private async Task NavigateToEditAsync()
        {
            IsHeaderExpanded = false; // Collapse menu
            await Shell.Current.GoToAsync("CategoryManagementPage");
        }

        public async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                _logger.LogInformation("Loading categories...");

                // Get data from service (cache + server sync)
                var data = await _categoryDataService.GetDataAsync();

                PopulateFromData(data);

                // Update sync status
                UpdateSyncStatus();
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

        private async Task RefreshDataAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;
                _logger.LogInformation("Manual refresh triggered");
                await _hubConnector.SyncNowAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing data");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void PopulateFromData(SharedModels.Categories.FullCategoryData? data)
        {
            Categories.Clear();
            Locations.Clear();
            Persons.Clear();

            if (data == null)
            {
                _logger.LogWarning("No category data available");
                return;
            }

            // === LOCATIONS ===
            Locations.Add(new LocationDisplayItem { Id = "", Name = "None" });
            foreach (var loc in data.Locations.Where(l => l.IsActive).OrderBy(l => l.SortOrder))
            {
                Locations.Add(new LocationDisplayItem { Id = loc.Id, Name = loc.Name });
            }
            SelectedLocation = Locations[0];

            // === PERSONS ===
            Persons.Add(new PersonDisplayItem { Id = "", Name = "None" });
            foreach (var person in data.Persons.Where(p => p.IsActive).OrderBy(p => p.SortOrder))
            {
                Persons.Add(new PersonDisplayItem { Id = person.Id, Name = person.Name });
            }
            SelectedPerson = Persons[0];

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

            // Build the note string
            var locationName = string.IsNullOrEmpty(SelectedLocation?.Id) ? "None" : SelectedLocation.Name;
            var personName = string.IsNullOrEmpty(SelectedPerson?.Id) ? "None" : SelectedPerson.Name;
            var categoryPath = $"{subCategory.ParentCategoryName} > {subCategory.Name}";

            // Added "| Memo: " at the end so user can optionally add their own note
            var noteText = $"Location: {locationName} | Person: {personName} | Category: {categoryPath} | Memo: ";

            _logger.LogInformation("Generated note: {Note}", noteText);

            // Copy to clipboard
            await Clipboard.Default.SetTextAsync(noteText);

            // Show confirmation
            await Application.Current!.MainPage!.DisplayAlert(
                "Copied!",
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

        private void OnDataUpdated(object? sender, SharedModels.Categories.FullCategoryData data)
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

    #region Display Models

    public class CategoryDisplayItem : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpanderIcon));
            }
        }

        public string ExpanderIcon => IsExpanded ? "v" : ">";

        public ObservableCollection<SubCategoryDisplayItem> SubCategories { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SubCategoryDisplayItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ParentCategoryId { get; set; } = string.Empty;
        public string ParentCategoryName { get; set; } = string.Empty;
        public bool HasVAT { get; set; }
    }

    public class LocationDisplayItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class PersonDisplayItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}