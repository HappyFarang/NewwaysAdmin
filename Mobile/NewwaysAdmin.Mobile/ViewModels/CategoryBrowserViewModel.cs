// File: Mobile/NewwaysAdmin.Mobile/ViewModels/CategoryBrowserViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile.ViewModels
{
    public class CategoryBrowserViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<CategoryBrowserViewModel> _logger;
        private readonly ConnectionState _connectionState;

        private bool _isLoading;
        private Color _connectionDotColor = Colors.Gray;
        private LocationDisplayItem? _selectedLocation;
        private PersonDisplayItem? _selectedPerson;

        public CategoryBrowserViewModel(
            ILogger<CategoryBrowserViewModel> logger,
            ConnectionState connectionState)
        {
            _logger = logger;
            _connectionState = connectionState;

            // Commands
            ToggleCategoryCommand = new Command<CategoryDisplayItem>(ToggleCategory);
            SelectSubCategoryCommand = new Command<SubCategoryDisplayItem>(SelectSubCategory);
            ModuleTappedCommand = new Command(OnModuleTapped);

            // Subscribe to connection changes
            _connectionState.OnConnectionChanged += OnConnectionStateChanged;
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

        #endregion

        #region Commands

        public ICommand ToggleCategoryCommand { get; }
        public ICommand SelectSubCategoryCommand { get; }
        public ICommand ModuleTappedCommand { get; }

        #endregion

        #region Methods

        public async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                _logger.LogInformation("Loading categories...");

                // Clear existing data
                Categories.Clear();
                Locations.Clear();
                Persons.Clear();

                // TODO: Load from cache or server
                // For now, add test data

                // === LOCATIONS ===
                Locations.Add(new LocationDisplayItem { Id = "", Name = "None" });
                Locations.Add(new LocationDisplayItem { Id = "1", Name = "Phrae" });
                Locations.Add(new LocationDisplayItem { Id = "2", Name = "Chiang Mai" });
                SelectedLocation = Locations[0]; // Default to "None"

                // === PERSONS ===
                Persons.Add(new PersonDisplayItem { Id = "", Name = "None" });
                Persons.Add(new PersonDisplayItem { Id = "1", Name = "Thomas" });
                Persons.Add(new PersonDisplayItem { Id = "2", Name = "Nok" });
                SelectedPerson = Persons[0]; // Default to "None"

                // === CATEGORIES ===
                var testCategory = new CategoryDisplayItem
                {
                    Id = "1",
                    Name = "Transportation",
                    IsExpanded = false
                };
                testCategory.SubCategories.Add(new SubCategoryDisplayItem
                {
                    Id = "1a",
                    Name = "Green Buses",
                    ParentCategoryId = "1",
                    ParentCategoryName = "Transportation",
                    HasVAT = true
                });
                testCategory.SubCategories.Add(new SubCategoryDisplayItem
                {
                    Id = "1b",
                    Name = "Fuel",
                    ParentCategoryId = "1",
                    ParentCategoryName = "Transportation",
                    HasVAT = true
                });
                Categories.Add(testCategory);

                var testCategory2 = new CategoryDisplayItem
                {
                    Id = "2",
                    Name = "Production",
                    IsExpanded = false
                };
                testCategory2.SubCategories.Add(new SubCategoryDisplayItem
                {
                    Id = "2a",
                    Name = "B2 Boxes",
                    ParentCategoryId = "2",
                    ParentCategoryName = "Production",
                    HasVAT = false
                });
                testCategory2.SubCategories.Add(new SubCategoryDisplayItem
                {
                    Id = "2b",
                    Name = "Raw Materials",
                    ParentCategoryId = "2",
                    ParentCategoryName = "Production",
                    HasVAT = true
                });
                Categories.Add(testCategory2);

                _logger.LogInformation("Loaded {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    Categories.Count, Locations.Count, Persons.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading categories");
            }
            finally
            {
                IsLoading = false;
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
            var locationName = SelectedLocation?.Name ?? "None";
            if (string.IsNullOrEmpty(SelectedLocation?.Id)) locationName = "None";

            var personName = SelectedPerson?.Name ?? "None";
            if (string.IsNullOrEmpty(SelectedPerson?.Id)) personName = "None";

            var categoryPath = $"{subCategory.ParentCategoryName} > {subCategory.Name}";

            var noteText = $"Location: {locationName} | Person: {personName} | Category: {categoryPath}";

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
            // TODO: Show module picker when more modules are available
        }

        private void OnConnectionStateChanged(object? sender, bool isOnline)
        {
            MainThread.BeginInvokeOnMainThread(UpdateConnectionDot);
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