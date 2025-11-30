// File: Mobile/NewwaysAdmin.Mobile/MauiProgram.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.IOConfiguration;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Auth;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.Services.Connectivity;

namespace NewwaysAdmin.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // ===== STORAGE =====
        builder.Services.AddSingleton<EnhancedStorageFactory>(sp =>
        {
            var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
            StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

            var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
            var factory = new EnhancedStorageFactory(logger);

            MobileStorageFolderConfiguration.ConfigureStorageFolders(factory);

            return factory;
        });

        // ===== CORE SERVICES =====
        builder.Services.AddSingleton<CredentialStorageService>();
        builder.Services.AddSingleton<PermissionsCache>();  // <-- This was missing!

        // ===== HTTP CLIENTS + AUTH/CONNECTION SERVICES =====
        builder.Services.AddHttpClient<IMauiAuthService, MauiAuthService>(client =>
        {
            client.BaseAddress = new Uri("http://localhost:5080/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        builder.Services.AddHttpClient<IConnectionService, ConnectionService>(client =>
        {
            client.BaseAddress = new Uri("http://localhost:5080/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // ===== VIEWMODELS =====
        builder.Services.AddTransient<SimpleLoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<CategoryBrowserViewModel>();

        // ===== CONNECTIVITY =====
        builder.Services.AddSingleton<ConnectionState>();
        builder.Services.AddSingleton<ConnectionMonitor>();

        // ===== PAGES =====
        builder.Services.AddTransient<SimpleLoginPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<CategoryBrowserPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}