// File: Mobile/NewwaysAdmin.Mobile/Services/SignalR/SignalRAppRegistration.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Universal.Models;

namespace NewwaysAdmin.Mobile.Services.SignalR
{
    /// <summary>
    /// SignalR app registration only
    /// Single responsibility: Register app with Universal hub
    /// </summary>
    public class SignalRAppRegistration
    {
        private readonly ILogger<SignalRAppRegistration> _logger;
        private readonly SignalRConnection _connection;

        public SignalRAppRegistration(
            ILogger<SignalRAppRegistration> logger,
            SignalRConnection connection)
        {
            _logger = logger;
            _connection = connection;
        }

        // ===== APP REGISTRATION ONLY =====

        public async Task<bool> RegisterAsAppAsync(string appName)
        {
            if (!_connection.IsConnected)
            {
                _logger.LogWarning("Cannot register app - not connected");
                return false;
            }

            try
            {
                var registration = new AppRegistration
                {
                    AppName = appName,
                    AppVersion = AppInfo.VersionString,
                    DeviceId = await GetDeviceIdAsync(),
                    DeviceType = DeviceInfo.Platform.ToString(),
                    Metadata = new Dictionary<string, object>
                    {
                        ["DeviceModel"] = DeviceInfo.Model,
                        ["OSVersion"] = DeviceInfo.VersionString
                    }
                };

                var hubConnection = _connection.GetConnection();
                if (hubConnection == null)
                {
                    _logger.LogWarning("No hub connection available");
                    return false;
                }

                await hubConnection.InvokeAsync("RegisterAppAsync", registration);
                _logger.LogInformation("Registered as app: {AppName}", appName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register app: {AppName}", appName);
                return false;
            }
        }

        // ===== PRIVATE HELPERS =====

        private async Task<string> GetDeviceIdAsync()
        {
            try
            {
                return await SecureStorage.Default.GetAsync("DeviceId") ??
                       await CreateAndStoreDeviceIdAsync();
            }
            catch
            {
                return Guid.NewGuid().ToString();
            }
        }

        private async Task<string> CreateAndStoreDeviceIdAsync()
        {
            var deviceId = Guid.NewGuid().ToString();
            try
            {
                await SecureStorage.Default.SetAsync("DeviceId", deviceId);
            }
            catch
            {
                // Ignore storage errors
            }
            return deviceId;
        }
    }
}