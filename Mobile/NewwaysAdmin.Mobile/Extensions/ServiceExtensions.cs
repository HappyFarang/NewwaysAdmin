// File: Mobile/NewwaysAdmin.Mobile/Extensions/ServiceExtensions.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.Services;
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
            // Core mobile services that depend on storage (EnhancedStorageFactory already configured in MauiProgram)
            services.AddSingleton<CredentialStorageService>();

            // ===== SIGNALR SERVICES =====
            services.AddScoped<SignalRConnection>();
            services.AddScoped<SignalRMessageSender>();
            services.AddScoped<SignalRAppRegistration>();
            services.AddScoped<SignalREventListener>();

            // ===== CACHE SERVICES =====
            services.AddScoped<CacheStorage>();
            services.AddScoped<CacheManager>();

            // ===== SYNC COORDINATORS =====
            services.AddScoped<SyncCoordinator>();
            services.AddScoped<StartupCoordinator>();

            // ===== CATEGORY SERVICES =====
            services.AddScoped<MobileCategoryService>();

            // Authentication services with interfaces (only where they make sense!)
            services.AddTransient<IMauiAuthService, MauiAuthService>();
            services.AddTransient<IConnectionService, ConnectionService>();

            return services;
        }

        public static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            // All ViewModels - keeping them as classes, not interfaces
            services.AddTransient<LoginViewModel>();
            services.AddTransient<SimpleLoginViewModel>();

            // Future ViewModels will go here:
            // services.AddTransient<MainViewModel>();
            // services.AddTransient<PhotoUploadViewModel>();

            return services;
        }

        public static IServiceCollection AddPages(this IServiceCollection services)
        {
            // All Pages - keeping them as classes, not interfaces
            services.AddTransient<LoginPage>();
            services.AddTransient<SimpleLoginPage>();

            // Future Pages will go here:
            // services.AddTransient<MainPage>();
            // services.AddTransient<PhotoUploadPage>();

            return services;
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services)
        {
            // HTTP client for server communication - BOTH services use the same client
            services.AddHttpClient<IMauiAuthService, MauiAuthService>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5080/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            services.AddHttpClient<IConnectionService, ConnectionService>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5080/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // Future HTTP clients can go here if needed

            return services;
        }
    }
}