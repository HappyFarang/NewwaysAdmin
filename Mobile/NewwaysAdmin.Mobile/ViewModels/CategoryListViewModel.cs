// File: Mobile/NewwaysAdmin.Mobile/ViewModels/CategoryListViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.Mobile.Services.Categories;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace NewwaysAdmin.Mobile.ViewModels
{
    /// <summary>
    /// ViewModel for the category list page
    /// </summary>
    public partial class CategoryListViewModel : ObservableObject
    {
        private readonly CategoryMobileService _categoryService;
        private readonly ILogger<CategoryListViewModel> _logger;

        [ObservableProperty]
        private ObservableCollection<MobileCategoryItem> categories = new();

        [ObservableProperty]
        private ObservableCollection<LocationItemWrapper> locations = new();

        [ObservableProperty]
        private LocationItemWrapper? selectedLocation;

        [ObservableProperty]
        private bool isLoading = true;

        [ObservableProperty]
        private bool hasData = false;

        [ObservableProperty]
        private string statusMessage = "Loading categories...";

        [ObservableProperty]
        private string lastSyncDisplay = "Never";

        public CategoryListViewModel(
            CategoryMobileService categoryService,
            ILogger<CategoryListViewModel> logger)
        {
            _categoryService = categoryService;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            await LoadCategoriesAsync();
        }

        [RelayCommand]
        private async Task LoadCategoriesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading categories...";

                _logger.LogInformation("Loading categories from service...");

                // Initialize service if needed
                await _categoryService.InitializeAsync();

                // Get categories from service (cached or from server)
                var syncData = await _categoryService.GetCategoriesAsync();

                if (syncData != null)
                {
                    // Update categories
                    Categories.Clear();
                    foreach (var category in syncData.Categories.OrderBy(c => c.SortOrder))
                    {
                        Categories.Add(category);
                    }

                    // Update locations (add "None" as default if not present)
                    Locations.Clear();

                    // Add "None" location first
                    var noneLocation = syncData.Locations.FirstOrDefault(l => l.Name.Equals("None", StringComparison.OrdinalIgnoreCase));
                    if (noneLocation != null)
                    {
                        var noneWrapper = new LocationItemWrapper(noneLocation, true);
                        Locations.Add(noneWrapper);
                        SelectedLocation = noneWrapper; // Default selection
                    }
                    else
                    {
                        // Create default "None" location
                        var defaultNone = new MobileLocationItem
                        {
                            Id = "none",
                            Name = "None",
                            SortOrder = -1
                        };
                        var noneWrapper = new LocationItemWrapper(defaultNone, true);
                        Locations.Add(noneWrapper);
                        SelectedLocation = noneWrapper;
                    }

                    // Add other locations
                    foreach (var location in syncData.Locations
                        .Where(l => !l.Name.Equals("None", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(l => l.SortOrder))
                    {
                        Locations.Add(new LocationItemWrapper(location, false));
                    }

                    HasData = true;
                    StatusMessage = $"Loaded {Categories.Count} categories";

                    // Update last sync time
                    var lastSync = _categoryService.GetLastSyncTime();
                    if (lastSync.HasValue)
                    {
                        var timeAgo = DateTime.UtcNow - lastSync.Value;
                        if (timeAgo.TotalMinutes < 1)
                        {
                            LastSyncDisplay = "Just now";
                        }
                        else if (timeAgo.TotalHours < 1)
                        {
                            LastSyncDisplay = $"{(int)timeAgo.TotalMinutes} minutes ago";
                        }
                        else if (timeAgo.TotalDays < 1)
                        {
                            LastSyncDisplay = $"{(int)timeAgo.TotalHours} hours ago";
                        }
                        else
                        {
                            LastSyncDisplay = $"{(int)timeAgo.TotalDays} days ago";
                        }
                    }
                    else
                    {
                        LastSyncDisplay = "Never";
                    }

                    _logger.LogInformation("Loaded {Count} categories and {LocationCount} locations",
                        Categories.Count, Locations.Count);
                }
                else
                {
                    // No data available
                    HasData = false;
                    StatusMessage = "No categories available. Connect to the server to download categories.";
                    _logger.LogWarning("No category data available");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading categories");
                HasData = false;
                StatusMessage = $"Error loading categories: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task RefreshAsync()
        {
            _logger.LogInformation("Manual refresh requested");
            await _categoryService.RequestCategorySyncAsync();
            await Task.Delay(1000); // Wait for sync
            await LoadCategoriesAsync();
        }

        [RelayCommand]
        private void SelectLocation(LocationItemWrapper locationWrapper)
        {
            // Unselect previous location
            if (SelectedLocation != null)
            {
                SelectedLocation.IsSelected = false;
            }

            // Select new location
            SelectedLocation = locationWrapper;
            locationWrapper.IsSelected = true;

            _logger.LogInformation("Location selected: {LocationName}", locationWrapper.Name);
        }

        [RelayCommand]
        private async Task NavigateToSubCategoriesAsync(MobileCategoryItem category)
        {
            if (SelectedLocation == null)
            {
                _logger.LogWarning("No location selected");
                return;
            }

            _logger.LogInformation("Navigating to subcategories for: {CategoryName}", category.Name);

            // Pass category and location to the subcategory page
            var navigationParameter = new Dictionary<string, object>
            {
                { "Category", category },
                { "Location", SelectedLocation.Location }
            };

            await Shell.Current.GoToAsync("SubCategoryListPage", navigationParameter);
        }
    }
}
