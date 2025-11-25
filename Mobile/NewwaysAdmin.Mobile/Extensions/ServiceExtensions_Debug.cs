// File: Mobile/NewwaysAdmin.Mobile/Extensions/ServiceExtensions_Debug.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.Infrastructure.Storage;

namespace NewwaysAdmin.Mobile.Extensions
{
    /// <summary>
    /// DEBUG VERSION: Minimal services to isolate the dependency injection issue
    /// </summary>
    public static class ServiceExtensions_Debug
    {
        public static IServiceCollection AddIOManagerServices_Debug(this IServiceCollection services)
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

        public static IServiceCollection AddMobileServices_Debug(this IServiceCollection services)
        {
            // Only register the minimal services to test

            // SKIP MobileStorageManager for now - this might be the problem
            // services.AddSingleton<MobileStorageManager>();

            // Core mobile services that depend on storage
            services.AddSingleton<CredentialStorageService>();

            // Authentication services with interfaces (only where they make sense!)
            services.AddTransient<IMauiAuthService, MauiAuthService>();
            services.AddTransient<IConnectionService, ConnectionService>();

            return services;
        }

        public static IServiceCollection AddViewModels_Debug(this IServiceCollection services)
        {
            // All ViewModels - keeping them as classes, not interfaces
            services.AddTransient<LoginViewModel>();
            services.AddTransient<SimpleLoginViewModel>();

            return services;
        }

        public static IServiceCollection AddPages_Debug(this IServiceCollection services)
        {
            // All Pages - keeping them as classes, not interfaces
            services.AddTransient<LoginPage>();
            services.AddTransient<SimpleLoginPage>();

            return services;
        }

        public static IServiceCollection AddHttpClients_Debug(this IServiceCollection services)
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

            return services;
        }
    }
}