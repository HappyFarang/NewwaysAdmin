// File: Mobile/NewwaysAdmin.Mobile/Pages/Categories/CategoryManagementPage.xaml.cs
using NewwaysAdmin.Mobile.ViewModels.Categories;

namespace NewwaysAdmin.Mobile.Pages.Categories;

public partial class CategoryManagementPage : ContentPage
{
    public CategoryManagementPage(CategoryManagementViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CategoryManagementViewModel vm)
        {
            await vm.LoadDataAsync();
        }
    }
}