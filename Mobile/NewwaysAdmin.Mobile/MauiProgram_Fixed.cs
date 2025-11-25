// File: Mobile/NewwaysAdmin.Mobile/MauiProgram_Fixed.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Extensions;

namespace NewwaysAdmin.Mobile;

/// <summary>
/// FIXED VERSION: Registers storage folders immediately during EnhancedStorageFactory creation
/// </summary>
public static class MauiProgram_Fixed
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

        // Register services with IMMEDIATE storage folder configuration
        builder.Services
            .AddIOManagerServices_Fixed()    // Configures folders during factory creation
            .AddMobileServices_Fixed()       // Now CredentialStorageService will work
            .AddViewModels_Fixed()
            .AddPages_Fixed()
            .AddHttpClients_Fixed();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}