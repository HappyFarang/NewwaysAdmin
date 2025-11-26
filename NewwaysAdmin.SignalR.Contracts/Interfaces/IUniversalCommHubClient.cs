// File: NewwaysAdmin.SignalR.Contracts/Interfaces/IUniversalCommHubClient.cs
using NewwaysAdmin.SignalR.Contracts.Models;

namespace NewwaysAdmin.SignalR.Contracts.Interfaces
{
    /// <summary>
    /// Client-side interface for strongly-typed SignalR connections
    /// Implement this on your client apps for better intellisense
    /// </summary>
    public interface IUniversalCommHubClient
    {
        Task InitialData(object data);
        Task RegistrationComplete(object registrationInfo);
        Task RegistrationError(string error);
        Task AuthenticationComplete(object authInfo);
        Task AuthenticationError(string error);
        Task MessageResponse(object response);
        Task MessageAck(MessageAck ack);
        Task BroadcastMessage(object message);
        Task HeartbeatAck(DateTime serverTime);
        Task ConnectionInfo(AppConnection connectionInfo);
        Task ServerStats(object stats);
    }
}