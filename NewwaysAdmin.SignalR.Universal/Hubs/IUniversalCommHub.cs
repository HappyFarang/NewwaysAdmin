// File: NewwaysAdmin.SignalR.Universal/Hubs/IUniversalCommHub.cs
using NewwaysAdmin.SignalR.Universal.Models;

namespace NewwaysAdmin.SignalR.Universal.Hubs
{
    /// <summary>
    /// Interface for Universal Communication Hub
    /// Useful for testing and client-side strongly-typed proxies
    /// </summary>
    public interface IUniversalCommHub
    {
        // ===== APP REGISTRATION =====
        Task RegisterAppAsync(AppRegistration registration);
        Task AuthenticateUserAsync(string userId, string? authToken = null);

        // ===== MESSAGING =====
        Task SendMessageAsync(UniversalMessage message);
        Task SendTypedMessageAsync<T>(string messageType, string targetApp, T data, bool requiresAck = false);

        // ===== BROADCASTING =====
        Task BroadcastToAppAsync(string targetApp, string messageType, object data);
        Task BroadcastToUserAsync(string userId, string messageType, object data);

        // ===== CONNECTION HEALTH =====
        Task HeartbeatAsync();
        Task GetConnectionInfoAsync();

        // ===== MONITORING =====
        Task GetServerStatsAsync();
    }

    /// <summary>
    /// Client-side interface for strongly-typed SignalR connections
    /// Implement this on your client apps for better intellisense
    /// </summary>
    public interface IUniversalCommHubClient
    {
        // ===== SERVER TO CLIENT MESSAGES =====
        Task InitialData(object data);
        Task RegistrationComplete(object registrationInfo);
        Task RegistrationError(string error);
        Task AuthenticationComplete(object authInfo);
        Task AuthenticationError(string error);

        // ===== MESSAGE RESPONSES =====
        Task MessageResponse(object response);
        Task MessageAck(MessageAck ack);
        Task BroadcastMessage(object message);

        // ===== CONNECTION HEALTH =====
        Task HeartbeatAck(DateTime serverTime);
        Task ConnectionInfo(AppConnection connectionInfo);

        // ===== MONITORING =====
        Task ServerStats(object stats);
    }
}