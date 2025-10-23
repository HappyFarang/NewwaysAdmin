// File: NewwaysAdmin.WebAdmin/Registration/SignalRServiceExtensions.cs
using Microsoft.AspNetCore.SignalR;
using NewwaysAdmin.SignalR.Universal.Extensions;
using NewwaysAdmin.WebAdmin.Services.SignalR;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class SignalRServiceExtensions
    {
        public static IServiceCollection AddSignalRServices(this IServiceCollection services)
        {
            // ===== UNIVERSAL SIGNALR SYSTEM =====
            services.AddUniversalSignalR(options =>
            {
                options.EnableDetailedErrors = true; // For development
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
                options.HandshakeTimeout = TimeSpan.FromSeconds(30);
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB max message
                options.EnableConnectionCleanup = true;
                options.ConnectionCleanupInterval = TimeSpan.FromMinutes(5);
                options.MaxConnectionAge = TimeSpan.FromMinutes(30);
            });

            // ===== APP-SPECIFIC MESSAGE HANDLERS =====

            // MAUI Expense Tracker handler
            services.AddMessageHandler<CategorySyncHandler>("MAUI_ExpenseTracker");

            // Future handlers can be added here:
            // services.AddMessageHandler<FaceScanningHandler>("FaceScanning_App");
            // services.AddMessageHandler<WorkerAttendanceHandler>("Worker_Attendance");
            // services.AddMessageHandler<NotificationHandler>("Notification_System");

            return services;
        }

        /// <summary>
        /// Configure Universal SignalR endpoints in the application pipeline
        /// </summary>
        public static void MapSignalRHubs(this IEndpointRouteBuilder endpoints)
        {
            // Universal communication hub (replaces old MobileCommHub)
            endpoints.MapUniversalSignalR("/hubs/universal");

            // Keep old endpoint for backward compatibility during transition
            // TODO: Remove this after MAUI app is updated to use /hubs/universal
            endpoints.MapUniversalSignalR("/hubs/mobile");

            // Future specialized hubs can be added here if needed:
            // endpoints.MapHub<SpecializedHub>("/hubs/specialized");
        }
    }
}