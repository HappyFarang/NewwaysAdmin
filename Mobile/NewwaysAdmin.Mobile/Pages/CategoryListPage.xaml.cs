// File: Mobile/NewwaysAdmin.Mobile/Pages/CategoryListPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class CategoryListPage : ContentPage
    {
        private readonly CategoryListViewModel _viewModel;

        public CategoryListPage(CategoryListViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await _viewModel.InitializeAsync();
        }
    }
}
