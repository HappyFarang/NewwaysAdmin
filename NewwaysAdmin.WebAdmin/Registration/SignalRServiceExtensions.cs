// File: NewwaysAdmin.WebAdmin/Registration/SignalRServiceExtensions.cs
using Microsoft.AspNetCore.SignalR;
using NewwaysAdmin.WebAdmin.Hubs;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class SignalRServiceExtensions
    {
        public static IServiceCollection AddSignalRServices(this IServiceCollection services)
        {
            // ===== SIGNALR CORE =====
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true; // For development
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
                options.HandshakeTimeout = TimeSpan.FromSeconds(30);
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB max message
            });

            // ===== COMMUNICATION HUBS =====
            // Multi-app communication hub for MAUI, future face scanning app, etc.
            services.AddScoped<MobileCommHub>();

            return services;
        }

        /// <summary>
        /// Configure SignalR endpoints in the application pipeline
        /// </summary>
        public static void MapSignalRHubs(this IEndpointRouteBuilder endpoints)
        {
            // Multi-app communication hub
            endpoints.MapHub<MobileCommHub>("/hubs/mobile");

            // Future hubs can be added here:
            // endpoints.MapHub<FaceScanningHub>("/hubs/facescanning");
            // endpoints.MapHub<NotificationHub>("/hubs/notifications");
        }
    }
}