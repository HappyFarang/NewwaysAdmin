// File: NewwaysAdmin.WebAdmin/Registration/SignalRServiceExtensions.cs
// UPDATED: Now uses ExpenseTrackerSyncHandler which combines categories + documents

using Microsoft.AspNetCore.SignalR;
using NewwaysAdmin.SignalR.Universal.Extensions;
using NewwaysAdmin.WebAdmin.Services.SignalR;
using NewwaysAdmin.WebAdmin.Services.Documents;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class SignalRServiceExtensions
    {
        public static IServiceCollection AddSignalRServices(this IServiceCollection services)
        {
            // ===== UNIVERSAL SIGNALR SYSTEM =====
            services.AddUniversalSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
                options.HandshakeTimeout = TimeSpan.FromSeconds(30);
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB for document uploads
                options.EnableConnectionCleanup = true;
                options.ConnectionCleanupInterval = TimeSpan.FromMinutes(5);
                options.MaxConnectionAge = TimeSpan.FromMinutes(30);
            });

            // ===== DOCUMENT SERVICES =====
            // Must be registered before handlers that depend on them
            services.AddSingleton<DocumentStorageService>();

            // ===== APP-SPECIFIC MESSAGE HANDLERS =====

            // MAUI Expense Tracker - combined handler for categories + documents
            // Note: Replaces old CategorySyncHandler
            services.AddMessageHandler<ExpenseTrackerSyncHandler>("MAUI_ExpenseTracker");

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
            // Universal communication hub
            endpoints.MapUniversalSignalR("/hubs/universal");

            // Keep old endpoint for backward compatibility
            endpoints.MapUniversalSignalR("/hubs/mobile");
        }
    }
}