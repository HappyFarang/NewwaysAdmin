// File: NewwaysAdmin.WebAdmin/Services/SignalR/ExpenseTrackerSyncHandler.cs
// Combined handler for all MAUI ExpenseTracker messages (categories + documents)
// Replaces CategorySyncHandler - handles both category sync AND document uploads

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Contracts.Models;
using NewwaysAdmin.SignalR.Universal.Services;
using NewwaysAdmin.WebAdmin.Services.Categories;
using NewwaysAdmin.WebAdmin.Services.Documents;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;
using NewwaysAdmin.SharedModels.Categories;
using System.Text.Json;
using System.Linq;
using NewwaysAdmin.WebAdmin.Services.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.SignalR
{
    /// <summary>
    /// Combined handler for MAUI ExpenseTracker app
    /// Handles both category sync and document uploads
    /// 
    /// Note: SignalR router only supports ONE handler per AppName,
    /// so we combine all message types here.
    /// </summary>
    public class ExpenseTrackerSyncHandler : IAppMessageHandler
    {
        private readonly CategoryService _categoryService;
        private readonly DocumentStorageService _documentStorageService;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ExpenseTrackerSyncHandler> _logger;
        private readonly BillUploadService _billUploadService;
        public string AppName => "MAUI_ExpenseTracker";

        public IEnumerable<string> SupportedMessageTypes => new[]
        {
            // ===== CATEGORY SYNC (from old CategorySyncHandler) =====
            "VersionExchange",
            "RequestFullData",
            "RequestVersion",
            "UploadData",
            "HeartbeatCheck",
            
            // ===== DOCUMENT SYNC (new) =====
            "UploadDocument",
            "AttachToProject",
            "GetSourceMappings",
            "GetUploadStatus",
            "UploadBill",
            "DeleteBill",
            "GetBills",
        };

        public ExpenseTrackerSyncHandler(
            CategoryService categoryService,
            DocumentStorageService documentStorageService,
            BillUploadService billUploadService,
            IServiceProvider serviceProvider,
            ILogger<ExpenseTrackerSyncHandler> logger)
        {
            _categoryService = categoryService;
            _documentStorageService = documentStorageService;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _billUploadService = billUploadService;
        }

        // ===== MESSAGE ROUTING =====

        public async Task<MessageHandlerResult> HandleMessageAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                _logger.LogDebug("ExpenseTrackerSync handling {MessageType} from {ConnectionId}",
                    message.MessageType, connectionId);

                return message.MessageType switch
                {
                    // Category messages
                    "VersionExchange" => await HandleVersionExchangeAsync(message, connectionId),
                    "RequestFullData" => await HandleRequestFullDataAsync(message, connectionId),
                    "RequestVersion" => await HandleRequestVersionAsync(message, connectionId),
                    "UploadData" => await HandleUploadDataAsync(message, connectionId),
                    "HeartbeatCheck" => await HandleHeartbeatAsync(message, connectionId),

                    // Document messages
                    "UploadDocument" => await HandleUploadDocumentAsync(message, connectionId),
                    "GetSourceMappings" => await HandleGetSourceMappingsAsync(message, connectionId),
                    "AttachToProject" => await HandleAttachToProjectAsync(message, connectionId),

                    // Bill messages
                    "UploadBill" => await HandleUploadBillAsync(message, connectionId),
                    "DeleteBill" => await HandleDeleteBillAsync(message, connectionId),
                    "GetBills" => await HandleGetBillsAsync(message, connectionId),

                    _ => MessageHandlerResult.CreateError($"Unsupported message type: {message.MessageType}")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling {MessageType} from {ConnectionId}",
                    message.MessageType, connectionId);
                return MessageHandlerResult.CreateError($"Internal error: {ex.Message}");
            }
        }

        // ============================================================
        // BILL UPLOAD METHODS
        // ============================================================

        private async Task<MessageHandlerResult> HandleUploadBillAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var request = JsonSerializer.Deserialize<BillUploadRequest>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return MessageHandlerResult.CreateError("Invalid bill upload request format");
                }

                _logger.LogInformation(
                    "Received bill upload for project {ProjectId} from {Username}",
                    request.ProjectId, request.Username ?? "unknown");

                // Decode base64 image data
                byte[] imageData;
                try
                {
                    imageData = Convert.FromBase64String(request.ImageDataBase64);
                }
                catch (FormatException)
                {
                    return MessageHandlerResult.CreateError("Invalid base64 image data");
                }

                // Upload via service
                var result = await _billUploadService.UploadBillAsync(
                    request.ProjectId,
                    imageData,
                    request.OriginalFilename);

                if (result.IsSuccess)
                {
                    _logger.LogInformation(
                        "✅ Bill uploaded: {BillFile} (bill #{Number}) for project {ProjectId}",
                        result.BillFilename, result.BillNumber, request.ProjectId);

                    return MessageHandlerResult.CreateSuccess(
                        BillUploadResponse.FromSuccess(result.BillFilename!, result.BillNumber));
                }
                else
                {
                    _logger.LogWarning(
                        "❌ Bill upload failed for project {ProjectId}: {Error}",
                        request.ProjectId, result.ErrorMessage);

                    return MessageHandlerResult.CreateError(result.ErrorMessage ?? "Upload failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling bill upload");
                return MessageHandlerResult.CreateError($"Upload error: {ex.Message}");
            }
        }

        private async Task<MessageHandlerResult> HandleDeleteBillAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var request = JsonSerializer.Deserialize<BillDeleteRequest>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return MessageHandlerResult.CreateError("Invalid delete request format");
                }

                _logger.LogInformation(
                    "Received bill delete request: {BillId} from project {ProjectId}",
                    request.BillId, request.ProjectId);

                var success = await _billUploadService.DeleteBillAsync(request.ProjectId, request.BillId);

                return MessageHandlerResult.CreateSuccess(new BillDeleteResponse
                {
                    Success = success,
                    ErrorMessage = success ? null : "Failed to delete bill"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling bill delete");
                return MessageHandlerResult.CreateError($"Delete error: {ex.Message}");
            }
        }

        private async Task<MessageHandlerResult> HandleGetBillsAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var request = JsonSerializer.Deserialize<GetBillsRequest>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null || string.IsNullOrEmpty(request.ProjectId))
                {
                    return MessageHandlerResult.CreateError("Invalid request: ProjectId required");
                }

                var billIds = await _billUploadService.GetBillReferencesAsync(request.ProjectId);

                return MessageHandlerResult.CreateSuccess(new GetBillsResponse
                {
                    Success = true,
                    BillIds = billIds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting bills");
                return MessageHandlerResult.CreateError($"Error: {ex.Message}");
            }
        }

        // ============================================================
        // CATEGORY SYNC METHODS (from CategorySyncHandler)
        // ============================================================

        private async Task<MessageHandlerResult> HandleVersionExchangeAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var request = JsonSerializer.Deserialize<VersionExchangeRequest>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return MessageHandlerResult.CreateError("Invalid version exchange format");
                }

                var serverVersion = await _categoryService.GetCurrentVersionAsync();

                var response = new VersionExchangeResponse
                {
                    ServerVersion = serverVersion,
                    YouNeedUpdate = request.MyVersion < serverVersion,
                    ServerNeedsYourData = request.MyVersion > serverVersion
                };

                if (response.YouNeedUpdate)
                {
                    var fullData = await _categoryService.GetFullDataAsync();
                    response.FullData = fullData;

                    _logger.LogInformation(
                        "Client v{ClientVersion} needs update to v{ServerVersion}. Sending full data.",
                        request.MyVersion, serverVersion);
                }
                else if (response.ServerNeedsYourData)
                {
                    _logger.LogInformation(
                        "Server needs update from client: v{ServerVersion} -> v{ClientVersion}.",
                        serverVersion, request.MyVersion);
                }
                else
                {
                    _logger.LogDebug("Versions match (v{Version}), no sync needed", serverVersion);
                }

                return MessageHandlerResult.CreateSuccess(response);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in version exchange message");
                return MessageHandlerResult.CreateError("Invalid message format");
            }
        }

        private async Task<MessageHandlerResult> HandleRequestFullDataAsync(UniversalMessage message, string connectionId)
        {
            _logger.LogDebug("Full data requested by connection {ConnectionId}", connectionId);

            var data = await _categoryService.GetFullDataAsync();

            _logger.LogInformation(
                "Sending full data (v{Version}): {CatCount} categories, {LocCount} locations, {PerCount} persons",
                data.DataVersion, data.Categories.Count, data.Locations.Count, data.Persons.Count);

            return MessageHandlerResult.CreateSuccess(data);
        }

        private async Task<MessageHandlerResult> HandleRequestVersionAsync(UniversalMessage message, string connectionId)
        {
            var version = await _categoryService.GetCurrentVersionAsync();

            _logger.LogDebug("Version requested by {ConnectionId}: v{Version}", connectionId, version);

            return MessageHandlerResult.CreateSuccess(new { Version = version });
        }

        private async Task<MessageHandlerResult> HandleUploadDataAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var uploadedData = JsonSerializer.Deserialize<FullCategoryData>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (uploadedData == null)
                {
                    return MessageHandlerResult.CreateError("Invalid data format");
                }

                _logger.LogInformation(
                    "Receiving data upload from MAUI: v{Version} - {CatCount} categories, {LocCount} locations, {PerCount} persons",
                    uploadedData.DataVersion,
                    uploadedData.Categories.Count,
                    uploadedData.Locations.Count,
                    uploadedData.Persons.Count);

                await _categoryService.SaveFullDataAsync(uploadedData);

                _logger.LogInformation("Data saved from MAUI upload. Server now at v{Version}", uploadedData.DataVersion);

                return MessageHandlerResult.CreateSuccess(new
                {
                    success = true,
                    newVersion = uploadedData.DataVersion
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling data upload");
                return MessageHandlerResult.CreateError($"Upload failed: {ex.Message}");
            }
        }

        private async Task<MessageHandlerResult> HandleHeartbeatAsync(UniversalMessage message, string connectionId)
        {
            var version = await _categoryService.GetCurrentVersionAsync();

            return MessageHandlerResult.CreateSuccess(new
            {
                ServerTime = DateTime.UtcNow,
                ConnectionId = connectionId,
                CurrentVersion = version
            });
        }

        // ============================================================
        // DOCUMENT SYNC METHODS (new)
        // ============================================================

        private async Task<MessageHandlerResult> HandleUploadDocumentAsync(UniversalMessage message, string connectionId)
        {
            try
            {
                var request = JsonSerializer.Deserialize<DocumentUploadRequest>(
                    message.Data.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return MessageHandlerResult.CreateError("Invalid upload request format");
                }

                _logger.LogInformation(
                    "Received document upload: {FileName} from {Username} via {SourceFolder}",
                    request.FileName, request.Username, request.SourceFolder);

                var saveResult = await _documentStorageService.SaveDocumentAsync(request);

                if (saveResult.Success)
                {
                    _logger.LogInformation(
                        "Document saved: {DocumentId} -> {Path}",
                        saveResult.DocumentId, saveResult.StoragePath);

                    // Trigger OCR processing immediately
                    await ProcessBankSlipAsync(saveResult.StoragePath!);

                    return MessageHandlerResult.CreateSuccess(
                        DocumentUploadResponse.CreateSuccess(saveResult.DocumentId!, saveResult.StoragePath!));
                }
                else
                {
                    _logger.LogWarning("Document save failed: {Error}", saveResult.ErrorMessage);
                    return MessageHandlerResult.CreateSuccess(
                        DocumentUploadResponse.CreateError("Upload failed", saveResult.ErrorMessage));
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON in upload request");
                return MessageHandlerResult.CreateError("Invalid request format");
            }
        }

        /// <summary>
        /// Process a newly uploaded bank slip through OCR and project creation
        /// </summary>
        private async Task ProcessBankSlipAsync(string filePath)
        {
            try
            {
                _logger.LogInformation("🔄 Triggering OCR processing for: {FilePath}", Path.GetFileName(filePath));

                // Use a scope since BankSlipProjectService is scoped
                using var scope = _serviceProvider.CreateScope();
                var projectService = scope.ServiceProvider.GetRequiredService<BankSlipProjectService>();

                var project = await projectService.ProcessBankSlipAsync(filePath);

                if (project != null)
                {
                    _logger.LogInformation(
                        "✅ Bank slip processed: {ProjectId} (HasNote: {HasNote}, VAT: {Vat})",
                        project.ProjectId, project.HasStructuralNote, project.HasVat);

                    // TODO: Future - notify mobile via SignalR that project is ready for review
                    // await NotifyProjectReadyAsync(project);
                }
                else
                {
                    _logger.LogWarning("⚠️ Bank slip processing returned null (may already exist or failed)");
                }
            }
            catch (Exception ex)
            {
                // Don't fail the upload if OCR processing fails
                // The startup scanner will catch it later
                _logger.LogError(ex, "❌ Error processing bank slip (upload still succeeded): {FilePath}", filePath);
            }
        }

        private async Task<MessageHandlerResult> HandleGetSourceMappingsAsync(UniversalMessage message, string connectionId)
        {
            // No predefined mappings - mobile uses whatever pattern name user types
            _logger.LogDebug("GetSourceMappings called - returning empty (patterns are user-defined)");

            return MessageHandlerResult.CreateSuccess(new
            {
                Mappings = new List<DocumentSourceMapping>(),
                Timestamp = DateTime.UtcNow
            });
        }

        private async Task<MessageHandlerResult> HandleAttachToProjectAsync(UniversalMessage message, string connectionId)
        {
            _logger.LogInformation("AttachToProject called - not yet implemented");
            return MessageHandlerResult.CreateError("AttachToProject not yet implemented");
        }

        // ============================================================
        // CONNECTION LIFECYCLE
        // ============================================================

        public async Task OnAppConnectedAsync(AppConnection connection)
        {
            _logger.LogInformation(
                "ExpenseTracker connected: Device {DeviceId} ({DeviceType}) - Version {AppVersion}",
                connection.DeviceId, connection.DeviceType, connection.AppVersion);
        }

        public async Task OnAppDisconnectedAsync(AppConnection connection)
        {
            _logger.LogInformation("ExpenseTracker disconnected: Device {DeviceId}", connection.DeviceId);
        }

        public async Task<bool> ValidateMessageAsync(UniversalMessage message)
        {
            if (string.IsNullOrEmpty(message.MessageType))
            {
                _logger.LogWarning("Message missing MessageType");
                return false;
            }
            return true;
        }

        public async Task<object?> GetInitialDataAsync(AppConnection connection)
        {
            _logger.LogDebug("Getting initial data for device {DeviceId}", connection.DeviceId);

            var categoryData = await _categoryService.GetFullDataAsync();

            return new
            {
                MessageType = "InitialData",
                Version = categoryData.DataVersion,
                CategoryData = categoryData,
                DocumentMappings = new List<DocumentSourceMapping>()  // Empty - patterns are user-defined
            };
        }
    }

    // ===== HELPER CLASSES =====

    public class VersionExchangeRequest
    {
        public int MyVersion { get; set; }
    }

    public class VersionExchangeResponse
    {
        public int ServerVersion { get; set; }
        public bool YouNeedUpdate { get; set; }
        public bool ServerNeedsYourData { get; set; }
        public FullCategoryData? FullData { get; set; }
    }

    // Note: DocumentUploadRequest and DocumentUploadResponse are in SignalR.Contracts
}