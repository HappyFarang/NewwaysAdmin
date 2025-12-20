// File: Mobile/NewwaysAdmin.Mobile/Services/SignalR/SignalRMessageSender.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Contracts.Models;
using System.Text.Json;

namespace NewwaysAdmin.Mobile.Services.SignalR
{
    /// <summary>
    /// SignalR message sending only
    /// Single responsibility: Send Universal messages
    /// </summary>
    public class SignalRMessageSender
    {
        private readonly ILogger<SignalRMessageSender> _logger;
        private readonly SignalRConnection _connection;

        public SignalRMessageSender(
            ILogger<SignalRMessageSender> logger,
            SignalRConnection connection)
        {
            _logger = logger;
            _connection = connection;
        }

        // ===== MESSAGE SENDING ONLY =====

        public async Task<bool> SendMessageAsync(string messageType, string targetApp, object data)
        {
            if (!_connection.IsConnected)
            {
                _logger.LogWarning("Cannot send message - not connected");
                return false;
            }

            try
            {
                var message = new UniversalMessage
                {
                    MessageType = messageType,
                    SourceApp = "MAUI_ExpenseTracker",
                    TargetApp = targetApp,
                    Data = JsonSerializer.SerializeToElement(data),
                    Timestamp = DateTime.UtcNow
                };

                var hubConnection = _connection.GetConnection();
                if (hubConnection == null)
                {
                    _logger.LogWarning("No hub connection available");
                    return false;
                }

                await hubConnection.InvokeAsync("SendMessageAsync", message);
                _logger.LogDebug("Sent message: {MessageType} to {TargetApp}", messageType, targetApp);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message: {MessageType}", messageType);
                return false;
            }
        }
        /// <summary>
        /// Send a message and wait for typed response
        /// </summary>
        public async Task<TResponse?> SendMessageWithResponseAsync<TResponse>(
            string messageType,
            string appName,
            object data) where TResponse : class
        {
            try
            {
                if (!_connection.IsConnected)
                {
                    _logger.LogWarning("Cannot send message - not connected");
                    return null;
                }

                var hubConnection = _connection.GetConnection();
                if (hubConnection == null)
                {
                    _logger.LogWarning("Cannot send message - no hub connection");
                    return null;
                }

                // Serialize data to JsonElement
                var jsonString = System.Text.Json.JsonSerializer.Serialize(data);
                var jsonElement = System.Text.Json.JsonDocument.Parse(jsonString).RootElement.Clone();

                var message = new UniversalMessage
                {
                    SourceApp = appName,
                    TargetApp = "WebAdmin",
                    MessageType = messageType,
                    Data = jsonElement,
                    UserId = GetDeviceId(),
                    Timestamp = DateTime.UtcNow
                };

                var response = await hubConnection.InvokeAsync<UniversalMessage>("SendMessage", message);

                if (response?.Data.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<TResponse>(response.Data.GetRawText());
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message with response: {MessageType}", messageType);
                return null;
            }
        }

        private string GetDeviceId()
        {
            // Use stored device ID or generate one
            var deviceId = Preferences.Get("DeviceId", string.Empty);
            if (string.IsNullOrEmpty(deviceId))
            {
                deviceId = Guid.NewGuid().ToString();
                Preferences.Set("DeviceId", deviceId);
            }
            return deviceId;
        }







    }
}