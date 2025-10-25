// File: Mobile/NewwaysAdmin.Mobile/Services/SignalR/SignalRMessageSender.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Universal.Models;
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
    }
}