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
            services.AddTransient<MauiAuthService>();

            return services;
        }

        public static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            // All ViewModels
            services.AddTransient<LoginViewModel>();
            // Future ViewModels will go here:
            // services.AddTransient<MainViewModel>();
            // services.AddTransient<PhotoUploadViewModel>();

            return services;
        }

        public static IServiceCollection AddPages(this IServiceCollection services)
        {
            // All Pages
            services.AddTransient<LoginPage>();
            // Future Pages will go here:
            // services.AddTransient<MainPage>();
            // services.AddTransient<PhotoUploadPage>();

            return services;
        }

        public static IServiceCollection AddHttpClients(this IServiceCollection services)
        {
            // HTTP client for server communication
            services.AddHttpClient<MauiAuthService>(client =>
            {
                client.BaseAddress = new Uri("http://localhost:5080/");  // Changed to match WebAdmin port
                // client.BaseAddress = new Uri("https://newwaysadmin.hopto.org:5080/");
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // Future HTTP clients can go here if needed

            return services;
        }
    }
}