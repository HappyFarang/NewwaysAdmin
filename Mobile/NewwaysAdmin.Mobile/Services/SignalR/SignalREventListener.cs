// File: Mobile/NewwaysAdmin.Mobile/Services/SignalR/SignalREventListener.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SignalR.Contracts.Interfaces;
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.Mobile.Services.SignalR
{
    /// <summary>
    /// SignalR event listening only
    /// Single responsibility: Listen for and handle incoming Universal SignalR events
    /// </summary>
    public class SignalREventListener : IUniversalCommHubClient
    {
        private readonly ILogger<SignalREventListener> _logger;
        private readonly SignalRConnection _connection;

        // Events for other services to subscribe to
        public event Func<object, Task>? OnInitialDataReceived;
        public event Func<object, Task>? OnMessageResponseReceived;
        public event Func<object, Task>? OnBroadcastMessageReceived;
        public event Func<string, Task>? OnRegistrationError;
        public event Func<string, Task>? OnAuthenticationError;

        public SignalREventListener(
            ILogger<SignalREventListener> logger,
            SignalRConnection connection)
        {
            _logger = logger;
            _connection = connection;
        }

        // ===== EVENT REGISTRATION =====

        public void RegisterEvents()
        {
            var hubConnection = _connection.GetConnection();
            if (hubConnection == null)
            {
                _logger.LogWarning("Cannot register events - no connection");
                return;
            }

            try
            {
                hubConnection.On<object>("InitialData", InitialData);
                hubConnection.On<object>("RegistrationComplete", RegistrationComplete);
                hubConnection.On<string>("RegistrationError", RegistrationError);
                hubConnection.On<object>("AuthenticationComplete", AuthenticationComplete);
                hubConnection.On<string>("AuthenticationError", AuthenticationError);
                hubConnection.On<object>("MessageResponse", MessageResponse);
                hubConnection.On<MessageAck>("MessageAck", MessageAck);
                hubConnection.On<object>("BroadcastMessage", BroadcastMessage);
                hubConnection.On<DateTime>("HeartbeatAck", HeartbeatAck);
                hubConnection.On<AppConnection>("ConnectionInfo", ConnectionInfo);
                hubConnection.On<object>("ServerStats", ServerStats);

                _logger.LogInformation("SignalR event handlers registered");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering SignalR event handlers");
            }
        }

        // ===== IUniversalCommHubClient IMPLEMENTATION =====

        public Task InitialData(object data)
        {
            _logger.LogDebug("Received initial data from server");
            return OnInitialDataReceived?.Invoke(data) ?? Task.CompletedTask;
        }

        public Task RegistrationComplete(object registrationInfo)
        {
            _logger.LogInformation("App registration completed: {Info}", registrationInfo);
            return Task.CompletedTask;
        }

        public Task RegistrationError(string error)
        {
            _logger.LogError("App registration failed: {Error}", error);
            return OnRegistrationError?.Invoke(error) ?? Task.CompletedTask;
        }

        public Task AuthenticationComplete(object authInfo)
        {
            _logger.LogInformation("Authentication completed: {Info}", authInfo);
            return Task.CompletedTask;
        }

        public Task AuthenticationError(string error)
        {
            _logger.LogError("Authentication failed: {Error}", error);
            return OnAuthenticationError?.Invoke(error) ?? Task.CompletedTask;
        }

        public Task MessageResponse(object response)
        {
            _logger.LogDebug("Received message response from server");
            return OnMessageResponseReceived?.Invoke(response) ?? Task.CompletedTask;
        }

        public Task MessageAck(MessageAck ack)
        {
            _logger.LogDebug("Received message acknowledgment: {MessageId}", ack.MessageId);
            return Task.CompletedTask;
        }

        public Task BroadcastMessage(object message)
        {
            _logger.LogDebug("Received broadcast message from server");
            return OnBroadcastMessageReceived?.Invoke(message) ?? Task.CompletedTask;
        }

        public Task HeartbeatAck(DateTime serverTime)
        {
            _logger.LogDebug("Received heartbeat ack from server");
            return Task.CompletedTask;
        }

        public Task ConnectionInfo(AppConnection connectionInfo)
        {
            _logger.LogDebug("Received connection info: {ConnectionId}", connectionInfo.ConnectionId);
            return Task.CompletedTask;
        }

        public Task ServerStats(object stats)
        {
            _logger.LogDebug("Received server stats from server");
            return Task.CompletedTask;
        }
    }
}