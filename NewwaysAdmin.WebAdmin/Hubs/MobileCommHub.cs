// File: NewwaysAdmin.WebAdmin/Hubs/MobileCommHub.cs
using Microsoft.AspNetCore.SignalR;
using NewwaysAdmin.SharedModels.Categories;
using NewwaysAdmin.WebAdmin.Services.Categories;

namespace NewwaysAdmin.WebAdmin.Hubs
{
    /// <summary>
    /// Multi-app communication hub for MAUI, future face scanning app, and other mobile clients
    /// </summary>
    public class MobileCommHub : Hub
    {
        private readonly CategoryService _categoryService;
        private readonly ILogger<MobileCommHub> _logger;

        public MobileCommHub(CategoryService categoryService, ILogger<MobileCommHub> logger)
        {
            _categoryService = categoryService;
            _logger = logger;
        }

        // ===== CONNECTION MANAGEMENT =====

        public override async Task OnConnectedAsync()
        {
            var userAgent = Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString() ?? "Unknown";
            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            _logger.LogInformation("Mobile client connected: {ConnectionId} from {IpAddress} - {UserAgent}",
                Context.ConnectionId, ipAddress, userAgent);

            await Groups.AddToGroupAsync(Context.ConnectionId, "MobileClients");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (exception != null)
            {
                _logger.LogWarning(exception, "Mobile client {ConnectionId} disconnected with error", Context.ConnectionId);
            }
            else
            {
                _logger.LogInformation("Mobile client {ConnectionId} disconnected normally", Context.ConnectionId);
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "MobileClients");
            await base.OnDisconnectedAsync(exception);
        }

        // ===== CLIENT REGISTRATION =====

        public async Task RegisterDevice(string deviceType, string deviceId, string appVersion)
        {
            var userAgent = Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString() ?? "Unknown";

            _logger.LogInformation("Device registered: {DeviceType} - {DeviceId} - Version: {AppVersion} - UA: {UserAgent}",
                deviceType, deviceId, appVersion, userAgent);

            await Groups.AddToGroupAsync(Context.ConnectionId, $"Device_{deviceType}");

            Context.Items["DeviceType"] = deviceType;
            Context.Items["DeviceId"] = deviceId;
            Context.Items["AppVersion"] = appVersion;

            await SendInitialDataToClient();
        }

        // ===== CATEGORY SYNC METHODS =====

        public async Task RequestCategorySync()
        {
            try
            {
                _logger.LogDebug("Category sync requested by {ConnectionId}", Context.ConnectionId);

                var syncData = await _categoryService.GetFullDataAsync();
                await Clients.Caller.SendAsync("CategorySyncData", syncData);

                _logger.LogDebug("Category sync data sent to {ConnectionId}", Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending category sync data to {ConnectionId}", Context.ConnectionId);
                await Clients.Caller.SendAsync("SyncError", "Failed to load category data");
            }
        }

        // Note: RecordCategoryUsage removed - usage tracking now happens in project files

        // ===== FUTURE: RECEIPT UPLOAD METHODS =====

        public async Task NotifyReceiptUploaded(string projectId, string fileName)
        {
            _logger.LogInformation("Receipt uploaded: {FileName} for project {ProjectId}", fileName, projectId);
            await Clients.OthersInGroup("MobileClients").SendAsync("ReceiptUploaded", new { projectId, fileName });
        }

        // ===== GENERAL MESSAGING =====

        public async Task SendPing()
        {
            await Clients.Caller.SendAsync("Pong", DateTime.UtcNow);
        }

        public async Task JoinUserGroup(string userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
            _logger.LogDebug("Client {ConnectionId} joined user group: {UserId}", Context.ConnectionId, userId);
        }

        // ===== PRIVATE METHODS =====

        private async Task SendInitialDataToClient()
        {
            try
            {
                var syncData = await _categoryService.GetFullDataAsync();
                await Clients.Caller.SendAsync("InitialCategoryData", syncData);

                await Clients.Caller.SendAsync("ConnectionEstablished", new
                {
                    timestamp = DateTime.UtcNow,
                    serverVersion = "1.0.0",
                    supportedFeatures = new[] { "categories", "receipts", "realtime_sync" }
                });

                _logger.LogDebug("Initial data sent to {ConnectionId}", Context.ConnectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending initial data to {ConnectionId}", Context.ConnectionId);
            }
        }
    }

    public static class MobileCommHubExtensions
    {
        public static void MapMobileCommHub(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapHub<MobileCommHub>("/hubs/mobile");
        }
    }
}