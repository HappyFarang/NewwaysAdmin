// File: NewwaysAdmin.WebAdmin/Components/Features/Accounting/BankSlipProjects/Modals/ReviewModal.razor.cs

using Microsoft.AspNetCore.Components;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;
using NewwaysAdmin.WebAdmin.Services.Categories;

namespace NewwaysAdmin.WebAdmin.Components.Features.Accounting.BankSlipProjects.Modals;

public partial class ReviewModal : ComponentBase
{
    [Inject] private BankSlipProjectService ProjectService { get; set; } = default!;
    [Inject] private BankSlipImageService ImageService { get; set; } = default!;
    [Inject] private CategoryService CategoryService { get; set; } = default!;
    [Inject] private ILogger<ReviewModal> Logger { get; set; } = default!;

    [Parameter] public EventCallback<BankSlipProject> OnSaved { get; set; }
    [Parameter] public EventCallback<string> OnViewFullImage { get; set; }

    // Visibility state
    private bool isVisible = false;
    private bool isSaving = false;
    private bool isLoadingImage = false;
    private bool isRescanning = false;

    // Current project
    private BankSlipProject? project;
    private string? imagePreviewUrl;

    // Edit fields
    private string editLocation = "";
    private string editPerson = "";
    private string editCategory = "";
    private string editSubCategory = "";
    private string editMemo = "";
    private bool? editHasVat;
    private bool editIsPrivate;
    private bool editHasBill;

    // Category dropdowns
    private List<CategoryItem> categories = new();
    private List<string> subCategories = new();
    private List<LocationItem> locations = new();
    private List<PersonItem> persons = new();

    public async Task OpenAsync(BankSlipProject projectToEdit)
    {
        project = projectToEdit;

        // Reset edit fields from project
        editLocation = project.StructuredMemo?.LocationName ?? "";
        editPerson = project.StructuredMemo?.PersonName ?? "";
        editCategory = project.StructuredMemo?.CategoryName ?? "";
        editSubCategory = project.StructuredMemo?.SubCategoryName ?? "";
        editMemo = project.StructuredMemo?.Memo ?? "";
        editHasVat = project.HasVat;
        editIsPrivate = project.IsPrivate;
        editHasBill = project.HasBill;

        isVisible = true;
        imagePreviewUrl = null;
        StateHasChanged();

        // Load dropdown data and image in parallel
        await Task.WhenAll(
            LoadDropdownDataAsync(),
            LoadThumbnailAsync()
        );

        // Update subcategories based on selected category
        UpdateSubCategories();

        StateHasChanged();
    }

    private async Task LoadDropdownDataAsync()
    {
        try
        {
            // Load all category data at once
            var fullData = await CategoryService.GetFullDataAsync();

            // Load categories
            categories = fullData.Categories
                .Where(c => c.IsActive)
                .Select(c => new CategoryItem
                {
                    Name = c.Name,
                    SubCategories = c.SubCategories
                        .Where(s => s.IsActive)
                        .Select(s => s.Name)
                        .ToList()
                })
                .ToList();

            // Load locations
            locations = fullData.Locations
                .Where(l => l.IsActive)
                .Select(l => new LocationItem { Name = l.Name })
                .ToList();

            // Load persons
            persons = fullData.Persons
                .Where(p => p.IsActive)
                .Select(p => new PersonItem { Name = p.Name })
                .ToList();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading dropdown data");
        }
    }

    private async Task LoadThumbnailAsync()
    {
        if (project == null) return;

        isLoadingImage = true;
        StateHasChanged();

        try
        {
            imagePreviewUrl = await ImageService.GetBankSlipImageAsync(project.ProjectId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading thumbnail for: {ProjectId}", project.ProjectId);
            imagePreviewUrl = null;
        }
        finally
        {
            isLoadingImage = false;
            StateHasChanged();
        }
    }

    private void UpdateSubCategories()
    {
        var cat = categories.FirstOrDefault(c => c.Name == editCategory);
        subCategories = cat?.SubCategories ?? new List<string>();

        // Clear subcategory if not valid for new category
        if (!subCategories.Contains(editSubCategory))
        {
            editSubCategory = "";
        }
    }

    private void OnCategoryChanged(ChangeEventArgs e)
    {
        editCategory = e.Value?.ToString() ?? "";
        UpdateSubCategories();
        StateHasChanged();
    }

    private void OnSubCategoryChanged(ChangeEventArgs e)
    {
        editSubCategory = e.Value?.ToString() ?? "";
    }

    private void OnLocationChanged(ChangeEventArgs e)
    {
        editLocation = e.Value?.ToString() ?? "";
    }

    private void OnPersonChanged(ChangeEventArgs e)
    {
        editPerson = e.Value?.ToString() ?? "";
    }

    private void OnVatChanged(ChangeEventArgs e)
    {
        var val = e.Value?.ToString();
        editHasVat = val switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };
    }

    private async Task ViewFullImage()
    {
        if (project != null)
        {
            await OnViewFullImage.InvokeAsync(project.ProjectId);
        }
    }

    
    private async Task SaveAsync()
    {
        await SaveProjectAsync(closeProject: false);
    }

    private async Task SaveAndCloseAsync()
    {
        await SaveProjectAsync(closeProject: true);
    }

    private async Task SaveProjectAsync(bool closeProject)
    {
        if (project == null) return;

        isSaving = true;
        StateHasChanged();

        try
        {
            // Update project from edit fields
            project.StructuredMemo ??= new ParsedMemo();
            project.StructuredMemo.LocationName = string.IsNullOrWhiteSpace(editLocation) ? null : editLocation;
            project.StructuredMemo.PersonName = string.IsNullOrWhiteSpace(editPerson) ? null : editPerson;
            project.StructuredMemo.CategoryName = string.IsNullOrWhiteSpace(editCategory) ? null : editCategory;
            project.StructuredMemo.SubCategoryName = string.IsNullOrWhiteSpace(editSubCategory) ? null : editSubCategory;
            project.StructuredMemo.Memo = string.IsNullOrWhiteSpace(editMemo) ? null : editMemo;

            project.HasVat = editHasVat;
            project.IsPrivate = editIsPrivate;
            project.HasBill = editHasBill;

            // Update structural note flag
            project.HasStructuralNote = !string.IsNullOrWhiteSpace(project.StructuredMemo.CategoryName);

            if (closeProject)
            {
                project.IsClosed = true;
                await ProjectService.CloseProjectAsync(project.ProjectId);
            }
            else
            {
                await ProjectService.UpdateProjectAsync(project);
            }

            await OnSaved.InvokeAsync(project);
            Logger.LogInformation("Saved project: {ProjectId} (closed: {Closed})", project.ProjectId, closeProject);

            Close();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving project");
        }
        finally
        {
            isSaving = false;
            StateHasChanged();
        }
    }

    private void Close()
    {
        isVisible = false;
        project = null;
        imagePreviewUrl = null;
        StateHasChanged();
    }

    private async Task RescanAsync()
    {
        if (project == null) return;

        isRescanning = true;
        StateHasChanged();

        try
        {
            var updatedProject = await ProjectService.RescanProjectAsync(project.ProjectId);

            if (updatedProject != null)
            {
                project = updatedProject;

                // Reset edit fields from updated project
                editLocation = project.StructuredMemo?.LocationName ?? "";
                editPerson = project.StructuredMemo?.PersonName ?? "";
                editCategory = project.StructuredMemo?.CategoryName ?? "";
                editSubCategory = project.StructuredMemo?.SubCategoryName ?? "";
                editMemo = project.StructuredMemo?.Memo ?? "";
                editHasVat = project.HasVat;
                editIsPrivate = project.IsPrivate;
                editHasBill = project.HasBill;

                UpdateSubCategories();

                Logger.LogInformation("Rescan completed for: {ProjectId}", project.ProjectId);
            }
            else
            {
                Logger.LogWarning("Rescan returned null for: {ProjectId}", project?.ProjectId);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error during rescan");
        }
        finally
        {
            isRescanning = false;
            StateHasChanged();
        }
    }
    private async Task OnBillReferencesChanged(List<string> newReferences)
    {
        if (project != null)
        {
            project.BillFileReferences = newReferences;
            StateHasChanged();
        }
    }

    private async Task OnHasBillChanged(bool hasBill)
    {
        editHasBill = hasBill;
        StateHasChanged();
    }
    // Helper classes for dropdowns
    private class CategoryItem
    {
        public string Name { get; set; } = "";
        public List<string> SubCategories { get; set; } = new();
    }

    private class LocationItem
    {
        public string Name { get; set; } = "";
    }

    private class PersonItem
    {
        public string Name { get; set; } = "";
    }
}