using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.IOConfiguration;
using NewwaysAdmin.Mobile.Services;

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

        try
        {
            // ABSOLUTE MINIMAL - just the factory
            builder.Services.AddSingleton<EnhancedStorageFactory>(sp =>
            {
                var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
                StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

                var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
                var factory = new EnhancedStorageFactory(logger);

                MobileStorageFolderConfiguration.ConfigureStorageFolders(factory);

                return factory;
            });

            // COMMENT OUT CredentialStorageService for now to test just the factory
            // builder.Services.AddSingleton<CredentialStorageService>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            // TRY TO RESOLVE JUST THE FACTORY
            var factory = app.Services.GetRequiredService<EnhancedStorageFactory>();
            System.Diagnostics.Debug.WriteLine("SUCCESS: EnhancedStorageFactory resolved successfully!");

            return app;
        }
        catch (Exception ex)
        {
            // CAPTURE THE EXACT ERROR
            System.Diagnostics.Debug.WriteLine("=== DEPENDENCY INJECTION ERROR ===");
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Type: {ex.GetType().Name}");

            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner: {ex.InnerException.Message}");
                System.Diagnostics.Debug.WriteLine($"Inner Type: {ex.InnerException.GetType().Name}");
            }

            System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine("================================");

            // Re-throw so you can see it in Visual Studio output
            throw;
        }
    }
}