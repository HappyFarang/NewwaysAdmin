// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/Pages/ProjectListPage.xaml.cs

using NewwaysAdmin.Mobile.Features.BankSlipReview.ViewModels;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.Pages;

public partial class ProjectListPage : ContentPage
{
    private readonly ProjectListViewModel _viewModel;

    public ProjectListPage(ProjectListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDataAsync();
    }
}