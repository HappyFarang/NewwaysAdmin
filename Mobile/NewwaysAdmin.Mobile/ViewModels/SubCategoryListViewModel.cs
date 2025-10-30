// File: Mobile/NewwaysAdmin.Mobile/ViewModels/SubCategoryListViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewwaysAdmin.SharedModels.Categories;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace NewwaysAdmin.Mobile.ViewModels
{
    /// <summary>
    /// ViewModel for the subcategory list page
    /// </summary>
    [QueryProperty(nameof(Category), "Category")]
    [QueryProperty(nameof(Location), "Location")]
    public partial class SubCategoryListViewModel : ObservableObject
    {
        private readonly ILogger<SubCategoryListViewModel> _logger;

        [ObservableProperty]
        private MobileCategoryItem? category;

        [ObservableProperty]
        private MobileLocationItem? location;

        [ObservableProperty]
        private ObservableCollection<MobileSubCategoryItem> subCategories = new();

        [ObservableProperty]
        private string categoryName = string.Empty;

        [ObservableProperty]
        private string locationName = string.Empty;

        [ObservableProperty]
        private string lastCopiedText = string.Empty;

        public SubCategoryListViewModel(ILogger<SubCategoryListViewModel> logger)
        {
            _logger = logger;
        }

        partial void OnCategoryChanged(MobileCategoryItem? value)
        {
            if (value != null)
            {
                CategoryName = value.Name;

                // Populate subcategories
                SubCategories.Clear();
                foreach (var subCategory in value.SubCategories.OrderBy(sc => sc.SortOrder))
                {
                    SubCategories.Add(subCategory);
                }

                _logger.LogInformation("Loaded {Count} subcategories for category: {CategoryName}",
                    SubCategories.Count, value.Name);
            }
        }

        partial void OnLocationChanged(MobileLocationItem? value)
        {
            if (value != null)
            {
                LocationName = value.Name;
                _logger.LogInformation("Location set to: {LocationName}", value.Name);
            }
        }

        [RelayCommand]
        private async Task CopyToClipboardAsync(MobileSubCategoryItem subCategory)
        {
            try
            {
                // Copy the FullPath to clipboard
                await Clipboard.SetTextAsync(subCategory.FullPath);

                LastCopiedText = subCategory.FullPath;

                _logger.LogInformation("Copied to clipboard: {FullPath}", subCategory.FullPath);

                // Show toast notification
                await ShowToastAsync($"✓ Copied: {subCategory.FullPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying to clipboard");
                await ShowToastAsync("❌ Failed to copy to clipboard");
            }
        }

        private async Task ShowToastAsync(string message)
        {
            // MAUI doesn't have built-in toast, so we'll use a simple alert for now
            // In production, you could use a community package like CommunityToolkit.Maui.Alerts
            var toast = Microsoft.Maui.Controls.Application.Current?.MainPage;
            if (toast != null)
            {
                await toast.DisplayAlert("", message, "OK");
            }
        }
    }
}
