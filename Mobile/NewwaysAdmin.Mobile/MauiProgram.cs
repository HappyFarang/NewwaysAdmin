// File: Mobile/NewwaysAdmin.Mobile/MauiProgram.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.IOConfiguration;
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

        // ===== STORAGE FACTORY (must be first) =====
        builder.Services.AddSingleton<EnhancedStorageFactory>(sp =>
        {
            var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
            StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

            var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
            var factory = new EnhancedStorageFactory(logger);

            MobileStorageFolderConfiguration.ConfigureStorageFolders(factory);

            return factory;
        });

        // ===== ALL OTHER SERVICES =====
        builder.Services
            .AddMobileServices()
            .AddViewModels()
            .AddPages()
            .AddHttpClients();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}