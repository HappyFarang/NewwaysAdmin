// File: Mobile/NewwaysAdmin.Mobile/Extensions/ServiceExtensions_Fixed.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.Infrastructure.Storage;
using NewwaysAdmin.Mobile.IOConfiguration;

namespace NewwaysAdmin.Mobile.Extensions
{
    public static class ServiceExtensions_Fixed
    {
        public static IServiceCollection AddIOManagerServices_Fixed(this IServiceCollection services)
        {
            // Set mobile base path BEFORE creating EnhancedStorageFactory
            var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
            StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

            // Create EnhancedStorageFactory AND configure mobile folders immediately
            services.AddSingleton<EnhancedStorageFactory>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
                var factory = new EnhancedStorageFactory(logger);

                // CRITICAL: Configure storage folders IMMEDIATELY during creation
                MobileStorageFolderConfiguration.ConfigureStorageFolders(factory);

                return factory;
            });

            return services;
        }

        public static IServiceCollection AddMobileServices_Fixed(this IServiceCollection services)
        {
            // Core mobile services that depend on storage
            services.AddSingleton<CredentialStorageService>();

            // Authentication services with interfaces (only where they make sense!)
            services.AddTransient<IMauiAuthService, MauiAuthService>();
            services.AddTransient<IConnectionService, ConnectionService>();

            return services;
        }

        public static IServiceCollection AddViewModels_Fixed(this IServiceCollection services)
        {
            // All ViewModels - keeping them as classes, not interfaces
            services.AddTransient<LoginViewModel>();
            services.AddTransient<SimpleLoginViewModel>();

            return services;
        }

        public static IServiceCollection AddPages_Fixed(this IServiceCollection services)
        {
            // All Pages - keeping them as classes, not interfaces
            services.AddTransient<LoginPage>();
            services.AddTransient<SimpleLoginPage>();

            return services;
        }

        public static IServiceCollection AddHttpClients_Fixed(this IServiceCollection services)
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