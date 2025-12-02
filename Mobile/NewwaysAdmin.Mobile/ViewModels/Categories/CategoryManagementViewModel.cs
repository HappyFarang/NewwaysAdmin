// File: Mobile/NewwaysAdmin.Mobile/ViewModels/Categories/CategoryManagementViewModel.cs
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.SharedModels.Categories;


namespace NewwaysAdmin.Mobile.ViewModels.Categories
{
    public class CategoryManagementViewModel : INotifyPropertyChanged
    {
        private readonly ILogger<CategoryManagementViewModel> _logger;
        private readonly CategoryDataService _categoryDataService;
        private readonly SyncState _syncState;

        private string _syncStatusText = "";

        public CategoryManagementViewModel(
            ILogger<CategoryManagementViewModel> logger,
            CategoryDataService categoryDataService,
            SyncState syncState)
        {
            _logger = logger;
            _categoryDataService = categoryDataService;
            _syncState = syncState;

            // Commands
            GoBackCommand = new Command(async () => await GoBackAsync());
            AddPersonCommand = new Command(async () => await AddPersonAsync());
            AddLocationCommand = new Command(async () => await AddLocationAsync());
            AddCategoryCommand = new Command(async () => await AddCategoryAsync());
            AddSubCategoryCommand = new Command<CategoryEditItem>(async (cat) => await AddSubCategoryAsync(cat));
            DeletePersonCommand = new Command<PersonEditItem>(async (p) => await DeletePersonAsync(p));
            DeleteLocationCommand = new Command<LocationEditItem>(async (l) => await DeleteLocationAsync(l));
            DeleteCategoryCommand = new Command<CategoryEditItem>(async (c) => await DeleteCategoryAsync(c));
            DeleteSubCategoryCommand = new Command<SubCategoryEditItem>(async (s) => await DeleteSubCategoryAsync(s));

            // Listen for data changes
            _categoryDataService.DataUpdated += OnDataUpdated;
        }

        #region Properties

        public ObservableCollection<PersonEditItem> Persons { get; } = new();
        public ObservableCollection<LocationEditItem> Locations { get; } = new();
        public ObservableCollection<CategoryEditItem> Categories { get; } = new();

        public string SyncStatusText
        {
            get => _syncStatusText;
            set { _syncStatusText = value; OnPropertyChanged(); }
        }

        #endregion

        #region Commands

        public ICommand GoBackCommand { get; }
        public ICommand AddPersonCommand { get; }
        public ICommand AddLocationCommand { get; }
        public ICommand AddCategoryCommand { get; }
        public ICommand AddSubCategoryCommand { get; }
        public ICommand DeletePersonCommand { get; }
        public ICommand DeleteLocationCommand { get; }
        public ICommand DeleteCategoryCommand { get; }
        public ICommand DeleteSubCategoryCommand { get; }

        #endregion

        #region Public Methods

        public async Task LoadDataAsync()
        {
            _logger.LogInformation("Loading category management data...");

            try
            {
                var data = await _categoryDataService.GetDataAsync();
                if (data == null)
                {
                    _logger.LogWarning("No data available");
                    return;
                }

                // Populate Persons
                Persons.Clear();
                foreach (var person in data.Persons)
                {
                    Persons.Add(new PersonEditItem { Id = person.Id, Name = person.Name });
                }

                // Populate Locations
                Locations.Clear();
                foreach (var location in data.Locations)
                {
                    Locations.Add(new LocationEditItem { Id = location.Id, Name = location.Name });
                }

                // Populate Categories with SubCategories
                Categories.Clear();
                foreach (var category in data.Categories.OrderBy(c => c.SortOrder))
                {
                    var catItem = new CategoryEditItem
                    {
                        Id = category.Id,
                        Name = category.Name,
                        SubCategoryCount = category.SubCategories?.Count ?? 0
                    };

                    if (category.SubCategories != null)
                    {
                        foreach (var sub in category.SubCategories.OrderBy(s => s.SortOrder))
                        {
                            catItem.SubCategories.Add(new SubCategoryEditItem
                            {
                                Id = sub.Id,
                                Name = sub.Name,
                                ParentCategoryId = category.Id
                            });
                        }
                    }

                    Categories.Add(catItem);
                }

                UpdateSyncStatus();

                _logger.LogInformation("Loaded: {PersonCount} persons, {LocationCount} locations, {CategoryCount} categories",
                    Persons.Count, Locations.Count, Categories.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading data");
            }
        }

        #endregion

        #region Private Methods - Navigation

        private async Task GoBackAsync()
        {
            await Shell.Current.GoToAsync("..");
        }

        #endregion

        #region Private Methods - Add Operations

        private async Task AddPersonAsync()
        {
            string result = await Application.Current!.MainPage!.DisplayPromptAsync(
                "New Person",
                "Enter person name:",
                "Create",
                "Cancel",
                placeholder: "e.g. Thomas");

            if (string.IsNullOrWhiteSpace(result)) return;

            _logger.LogInformation("Creating person: {Name}", result);

            try
            {
                await _categoryDataService.CreatePersonAsync(result.Trim());
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating person");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to create person", "OK");
            }
        }

        private async Task AddLocationAsync()
        {
            string result = await Application.Current!.MainPage!.DisplayPromptAsync(
                "New Location",
                "Enter location name:",
                "Create",
                "Cancel",
                placeholder: "e.g. Office");

            if (string.IsNullOrWhiteSpace(result)) return;

            _logger.LogInformation("Creating location: {Name}", result);

            try
            {
                await _categoryDataService.CreateLocationAsync(result.Trim());
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating location");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to create location", "OK");
            }
        }

        private async Task AddCategoryAsync()
        {
            string result = await Application.Current!.MainPage!.DisplayPromptAsync(
                "New Category",
                "Enter category name:",
                "Create",
                "Cancel",
                placeholder: "e.g. Transportation");

            if (string.IsNullOrWhiteSpace(result)) return;

            _logger.LogInformation("Creating category: {Name}", result);

            try
            {
                await _categoryDataService.CreateCategoryAsync(result.Trim());
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating category");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to create category", "OK");
            }
        }

        private async Task AddSubCategoryAsync(CategoryEditItem? parentCategory)
        {
            if (parentCategory == null) return;

            string result = await Application.Current!.MainPage!.DisplayPromptAsync(
                $"New SubCategory",
                $"Add subcategory to '{parentCategory.Name}':",
                "Create",
                "Cancel",
                placeholder: "e.g. Green Buses");

            if (string.IsNullOrWhiteSpace(result)) return;

            _logger.LogInformation("Creating subcategory: {Name} under {Parent}", result, parentCategory.Name);

            try
            {
                await _categoryDataService.CreateSubCategoryAsync(parentCategory.Id, result.Trim());
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating subcategory");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to create subcategory", "OK");
            }
        }

        #endregion

        #region Private Methods - Delete Operations

        private async Task DeletePersonAsync(PersonEditItem? person)
        {
            if (person == null) return;

            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Delete Person",
                $"Delete '{person.Name}'?",
                "Delete",
                "Cancel");

            if (!confirm) return;

            _logger.LogInformation("Deleting person: {Name}", person.Name);

            try
            {
                await _categoryDataService.DeletePersonAsync(person.Id);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting person");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to delete person", "OK");
            }
        }

        private async Task DeleteLocationAsync(LocationEditItem? location)
        {
            if (location == null) return;

            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Delete Location",
                $"Delete '{location.Name}'?",
                "Delete",
                "Cancel");

            if (!confirm) return;

            _logger.LogInformation("Deleting location: {Name}", location.Name);

            try
            {
                await _categoryDataService.DeleteLocationAsync(location.Id);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting location");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to delete location", "OK");
            }
        }

        private async Task DeleteCategoryAsync(CategoryEditItem? category)
        {
            if (category == null) return;

            string message = category.SubCategoryCount > 0
                ? $"Delete '{category.Name}' and its {category.SubCategoryCount} subcategories?"
                : $"Delete '{category.Name}'?";

            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Delete Category",
                message,
                "Delete",
                "Cancel");

            if (!confirm) return;

            _logger.LogInformation("Deleting category: {Name}", category.Name);

            try
            {
                await _categoryDataService.DeleteCategoryAsync(category.Id);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting category");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to delete category", "OK");
            }
        }

        private async Task DeleteSubCategoryAsync(SubCategoryEditItem? subCategory)
        {
            if (subCategory == null) return;

            bool confirm = await Application.Current!.MainPage!.DisplayAlert(
                "Delete SubCategory",
                $"Delete '{subCategory.Name}'?",
                "Delete",
                "Cancel");

            if (!confirm) return;

            _logger.LogInformation("Deleting subcategory: {Name}", subCategory.Name);

            try
            {
                await _categoryDataService.DeleteSubCategoryAsync(subCategory.ParentCategoryId, subCategory.Id);
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting subcategory");
                await Application.Current!.MainPage!.DisplayAlert("Error", "Failed to delete subcategory", "OK");
            }
        }

        #endregion

        #region Private Methods - Helpers

        private void UpdateSyncStatus()
        {
            SyncStatusText = $"v{_syncState.LocalVersion}";
        }

        private void OnDataUpdated(object? sender, FullCategoryData data)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _logger.LogInformation("Data updated notification - refreshing");
                await LoadDataAsync();
            });
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

    public class PersonEditItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class LocationEditItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class CategoryEditItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int SubCategoryCount { get; set; }
        public ObservableCollection<SubCategoryEditItem> SubCategories { get; } = new();
    }

    public class SubCategoryEditItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ParentCategoryId { get; set; } = "";
    }

    #endregion
}