// File: Mobile/NewwaysAdmin.Mobile/Pages/CategoryBrowserPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class CategoryBrowserPage : ContentPage
    {
        private readonly CategoryBrowserViewModel _viewModel;

        public CategoryBrowserPage(CategoryBrowserViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.LoadCategoriesAsync();
        }
    }
}