// File: Mobile/NewwaysAdmin.Mobile/Features/BankSlipReview/Pages/ProjectDetailPage.xaml.cs

using NewwaysAdmin.Mobile.Features.BankSlipReview.ViewModels;

namespace NewwaysAdmin.Mobile.Features.BankSlipReview.Pages;

public partial class ProjectDetailPage : ContentPage
{
    public ProjectDetailPage(ProjectDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}