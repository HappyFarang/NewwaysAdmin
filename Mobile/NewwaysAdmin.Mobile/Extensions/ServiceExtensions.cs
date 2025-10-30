// File: Mobile/NewwaysAdmin.Mobile/Extensions/ServiceExtensions.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.Infrastructure.Storage;

namespace NewwaysAdmin.Mobile.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddIOManagerServices(this IServiceCollection services)
        {
            // Set mobile base path BEFORE creating EnhancedStorageFactory
            var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
            StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

            // Now create EnhancedStorageFactory - it will use the mobile path
            services.AddSingleton<EnhancedStorageFactory>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
                return new EnhancedStorageFactory(logger);
            });

            return services;
        }

        public static IServiceCollection AddMobileServices(this IServiceCollection services)
        {
            // Mobile storage manager (configures the storage factory)
            services.AddSingleton<MobileStorageManager>();

            // Core mobile services that depend on storage
            services.AddSingleton<CredentialStorageService>();

            // Authentication services with interfaces (only where they make sense!)
            services.AddTransient<IMauiAuthService, MauiAuthService>();
            services.AddTransient<IConnectionService, ConnectionService>();

            // SignalR services (Singleton - maintain connection throughout app lifetime)
            services.AddSingleton<NewwaysAdmin.Mobile.Services.SignalR.SignalRConnection>();
            services.AddSingleton<NewwaysAdmin.Mobile.Services.SignalR.SignalRMessageSender>();
            services.AddSingleton<NewwaysAdmin.Mobile.Services.SignalR.SignalRAppRegistration>();
            services.AddSingleton<NewwaysAdmin.Mobile.Services.SignalR.SignalREventListener>();

            // Cache services (Singleton - maintain cache throughout app lifetime)
            services.AddSingleton<NewwaysAdmin.Mobile.Services.Cache.CacheStorage>();
            services.AddSingleton<NewwaysAdmin.Mobile.Services.Cache.CacheManager>();

            // Permissions cache (Singleton - maintain permissions throughout app lifetime)
            services.AddSingleton<NewwaysAdmin.Mobile.Services.Auth.PermissionsCache>();

            // Coordinators (Singleton - orchestrate app-wide operations)
            services.AddSingleton<NewwaysAdmin.Mobile.Services.Sync.SyncCoordinator>();
            services.AddSingleton<NewwaysAdmin.Mobile.Services.Startup.StartupCoordinator>();

            // Business services (Singleton - maintain data throughout app lifetime)
            services.AddSingleton<NewwaysAdmin.Mobile.Services.Categories.CategoryMobileService>();

            return services;
        }

        public static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            // All ViewModels - keeping them as classes, not interfaces
            services.AddTransient<LoginViewModel>();
            services.AddTransient<SimpleLoginViewModel>();
            services.AddTransient<MainMenuViewModel>();
            services.AddTransient<CategoryListViewModel>();
            services.AddTransient<SubCategoryListViewModel>();

            // Future ViewModels will go here:
            // services.AddTransient<PhotoUploadViewModel>();

            return services;
        }

        public static IServiceCollection AddPages(this IServiceCollection services)
        {
            // All Pages - keeping them as classes, not interfaces
            services.AddTransient<LoginPage>();
            services.AddTransient<SimpleLoginPage>();
            services.AddTransient<MainMenuPage>();
            services.AddTransient<CategoryListPage>();
            services.AddTransient<SubCategoryListPage>();

            // Future Pages will go here:
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