// File: Mobile/NewwaysAdmin.Mobile/MauiProgram.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.IOConfiguration;
using NewwaysAdmin.Mobile.Services;
using NewwaysAdmin.Mobile.Services.Auth;
using NewwaysAdmin.Mobile.ViewModels;
using NewwaysAdmin.Mobile.Pages;
using NewwaysAdmin.Mobile.Pages.Categories;
using NewwaysAdmin.Mobile.Services.Connectivity;
using NewwaysAdmin.Mobile.Services.Categories;
using NewwaysAdmin.Mobile.ViewModels.Categories;
using NewwaysAdmin.Mobile.Config;


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
            client.BaseAddress = new Uri(AppConfig.ServerUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("X-Mobile-Api-Key", AppConfig.MobileApiKey);
        });

        builder.Services.AddHttpClient<IConnectionService, ConnectionService>(client =>
        {
            client.BaseAddress = new Uri(AppConfig.ServerUrl + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("X-Mobile-Api-Key", AppConfig.MobileApiKey);
        });


        // ===== CONNECTIVITY =====
        builder.Services.AddSingleton<ConnectionState>();
        builder.Services.AddSingleton<ConnectionMonitor>();
        builder.Services.AddSingleton<MobileSessionState>();

        // ===== CATEGORY SYNC SERVICES =====
        builder.Services.AddSingleton<SyncState>();
        builder.Services.AddSingleton<CategoryDataService>();
        builder.Services.AddSingleton<CategoryHubConnector>();


        // ===== VIEWMODELS =====
        builder.Services.AddTransient<SimpleLoginViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<CategoryBrowserViewModel>();
        builder.Services.AddTransient<CategoryManagementViewModel>();
        // builder.Services.AddTransient<SettingsViewModel>();

        // ===== PAGES =====
        builder.Services.AddTransient<SimpleLoginPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<CategoryBrowserPage>();
        builder.Services.AddTransient<CategoryManagementPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}