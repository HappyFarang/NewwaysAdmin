// File: Mobile/NewwaysAdmin.Mobile/MauiProgram.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.Configuration;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.Services; // Add this using statement

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

        // Add required dependencies for IOManager in correct order
        builder.Services.AddSingleton<MachineConfigProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MachineConfigProvider>>();
            return new MachineConfigProvider(logger);
        });

        builder.Services.AddSingleton<EnhancedStorageFactory>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
            return new EnhancedStorageFactory(logger);
        });

        builder.Services.AddSingleton<IOManagerOptions>(sp =>
        {
            var appDataPath = FileSystem.AppDataDirectory;
            var mobileDataPath = Path.Combine(appDataPath, "NewwaysAdmin");

            return new IOManagerOptions
            {
                LocalBaseFolder = mobileDataPath,
                ServerDefinitionsPath = "", // Not needed for mobile
                ApplicationName = "NewwaysAdmin.Mobile"
            };
        });

        // Now IOManager has all its dependencies
        builder.Services.AddSingleton<IOManager>();

        // Register mobile services
        builder.Services.AddSingleton<CredentialStorageService>();
        builder.Services.AddTransient<MauiAuthService>();

        // HTTP client for server communication
        builder.Services.AddHttpClient<MauiAuthService>(client =>
        {
            client.BaseAddress = new Uri("https://localhost:5001/");
            // client.BaseAddress = new Uri("https://newwaysadmin.hopto.org:5080/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}