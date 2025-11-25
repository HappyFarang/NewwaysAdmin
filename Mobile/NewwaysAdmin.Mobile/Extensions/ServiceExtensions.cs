// File: Mobile/NewwaysAdmin.Mobile/Extensions/ServiceExtensions.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Auth;  // ← Add this for PermissionsCache
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.Services.SignalR;
using NewwaysAdmin.Mobile.Services.Cache;
using NewwaysAdmin.Mobile.Services.Sync;
using NewwaysAdmin.Mobile.Services.Startup;
using NewwaysAdmin.Mobile.Services.Categories;

namespace NewwaysAdmin.Mobile.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddMobileServices(this IServiceCollection services)
        {
            // ===== STORAGE SERVICES (Singletons - shared state) =====
            services.AddSingleton<CredentialStorageService>();
            services.AddSingleton<PermissionsCache>();  // ← ADD THIS - was missing!

            // ===== SIGNALR SERVICES (Singletons - maintain connection state) =====
            services.AddSingleton<SignalRConnection>();
            services.AddSingleton<SignalRMessageSender>();
            services.AddSingleton<SignalRAppRegistration>();
            services.AddSingleton<SignalREventListener>();

            // ===== CACHE SERVICES (Singletons - shared cache) =====
            services.AddSingleton<CacheStorage>();
            services.AddSingleton<CacheManager>();

            // ===== SYNC COORDINATORS (Singletons - maintain sync state) =====
            services.AddSingleton<SyncCoordinator>();
            services.AddSingleton<StartupCoordinator>();

            // ===== CATEGORY SERVICES =====
            services.AddSingleton<MobileCategoryService>();

            // ===== AUTH SERVICES (registered via AddHttpClients) =====
            // IMauiAuthService and IConnectionService are registered in AddHttpClients()

            return services;
        }

        public static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            services.AddTransient<LoginViewModel>();
            services.AddTransient<SimpleLoginViewModel>();
            return services;
        }

        public static IServiceCollection AddPages(this IServiceCollection services)
        {
            services.AddTransient<LoginPage>();
            services.AddTransient<SimpleLoginPage>();
            return services;
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services)
        {
            // HTTP client for auth service
            services.AddHttpClient<IMauiAuthService, MauiAuthService>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5080/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // HTTP client for connection testing
            services.AddHttpClient<IConnectionService, ConnectionService>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5080/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            return services;
        }
    }
}