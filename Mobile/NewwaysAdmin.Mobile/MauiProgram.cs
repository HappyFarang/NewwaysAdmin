using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Extensions;

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

        // Register all services using extension methods
        builder.Services
            .AddIOManagerServices()
            .AddMobileServices()        // This should register MobileStorageManager
            .AddViewModels()
            .AddPages()
            .AddHttpClients();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}