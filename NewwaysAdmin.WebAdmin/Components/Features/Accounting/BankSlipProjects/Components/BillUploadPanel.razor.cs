// File: NewwaysAdmin.WebAdmin/Components/Features/Accounting/BankSlipProjects/Components/BillUploadPanel.razor.cs

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using NewwaysAdmin.WebAdmin.Services.BankSlips;

namespace NewwaysAdmin.WebAdmin.Components.Features.Accounting.BankSlipProjects.Components;

public partial class BillUploadPanel : ComponentBase
{
    [Inject] private BillUploadService BillService { get; set; } = default!;
    [Inject] private ILogger<BillUploadPanel> Logger { get; set; } = default!;

    /// <summary>
    /// The project ID to upload bills for
    /// </summary>
    [Parameter, EditorRequired]
    public string ProjectId { get; set; } = "";

    /// <summary>
    /// Current list of bill references
    /// </summary>
    [Parameter]
    public List<string> BillReferences { get; set; } = new();

    /// <summary>
    /// Callback when a bill is uploaded or deleted
    /// </summary>
    [Parameter]
    public EventCallback<List<string>> BillReferencesChanged { get; set; }

    /// <summary>
    /// Callback when HasBill should be updated
    /// </summary>
    [Parameter]
    public EventCallback<bool> OnHasBillChanged { get; set; }

    // State
    private bool isUploading = false;
    private string? previewUrl = null;
    private byte[]? pendingImageData = null;
    private string? pendingFilename = null;
    private string? errorMessage = null;
    private string? viewingBillUrl = null;

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        errorMessage = null;

        try
        {
            var file = e.File;

            // Validate file type
            if (!file.ContentType.StartsWith("image/"))
            {
                errorMessage = "Please select an image file";
                return;
            }

            // Validate file size
            if (file.Size > MaxFileSizeBytes)
            {
                errorMessage = $"File too large. Max size is {MaxFileSizeBytes / (1024 * 1024)}MB";
                return;
            }

            // Read file data
            using var stream = file.OpenReadStream(MaxFileSizeBytes);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            pendingImageData = ms.ToArray();
            pendingFilename = file.Name;

            // Create preview
            previewUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(pendingImageData)}";

            Logger.LogInformation("File selected for upload: {Filename} ({Size} bytes)",
                file.Name, pendingImageData.Length);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error selecting file");
            errorMessage = "Error reading file";
            ClearPending();
        }
    }

    private async Task ConfirmUpload()
    {
        if (pendingImageData == null || string.IsNullOrEmpty(ProjectId))
            return;

        errorMessage = null;
        isUploading = true;
        StateHasChanged();

        try
        {
            var result = await BillService.UploadBillAsync(ProjectId, pendingImageData, pendingFilename);

            if (result.IsSuccess)
            {
                Logger.LogInformation("Bill uploaded successfully: {BillFilename}", result.BillFilename);

                // Update the references list
                var updatedRefs = new List<string>(BillReferences) { result.BillFilename! };
                await BillReferencesChanged.InvokeAsync(updatedRefs);
                await OnHasBillChanged.InvokeAsync(true);

                ClearPending();
            }
            else
            {
                errorMessage = result.ErrorMessage ?? "Upload failed";
                Logger.LogWarning("Bill upload failed: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error uploading bill");
            errorMessage = "Upload error occurred";
        }
        finally
        {
            isUploading = false;
        }
    }

    private void CancelPreview()
    {
        ClearPending();
    }

    private void ClearPending()
    {
        previewUrl = null;
        pendingImageData = null;
        pendingFilename = null;
    }

    private async Task ViewBill(string billFilename)
    {
        try
        {
            var imageUrl = await BillService.GetBillImageAsync(billFilename);
            if (imageUrl != null)
            {
                viewingBillUrl = imageUrl;
            }
            else
            {
                errorMessage = "Could not load bill image";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error viewing bill: {BillFilename}", billFilename);
            errorMessage = "Error loading bill";
        }
    }

    private void CloseBillViewer()
    {
        viewingBillUrl = null;
    }

    private async Task DeleteBill(string billFilename)
    {
        try
        {
            var success = await BillService.DeleteBillAsync(ProjectId, billFilename);

            if (success)
            {
                Logger.LogInformation("Bill deleted: {BillFilename}", billFilename);

                var updatedRefs = BillReferences.Where(b => b != billFilename).ToList();
                await BillReferencesChanged.InvokeAsync(updatedRefs);
                await OnHasBillChanged.InvokeAsync(updatedRefs.Any());
            }
            else
            {
                errorMessage = "Could not delete bill";
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting bill: {BillFilename}", billFilename);
            errorMessage = "Error deleting bill";
        }
    }

    private string GetBillDisplayName(string billFilename)
    {
        // Show just the bill number part: "KBIZ_Amy_001.jpg" -> "Bill 1"
        var name = Path.GetFileNameWithoutExtension(billFilename);
        var parts = name.Split('_');
        if (parts.Length > 0 && int.TryParse(parts[^1], out var num))
        {
            return $"Bill {num}";
        }
        return Path.GetFileName(billFilename);
    }
}