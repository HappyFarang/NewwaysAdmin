// File: Mobile/NewwaysAdmin.Mobile/Pages/SubCategoryListPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels;

namespace NewwaysAdmin.Mobile.Pages
{
    public partial class SubCategoryListPage : ContentPage
    {
        private readonly SubCategoryListViewModel _viewModel;

        public SubCategoryListPage(SubCategoryListViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
    }
}
