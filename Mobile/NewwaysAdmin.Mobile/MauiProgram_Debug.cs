// File: Mobile/NewwaysAdmin.Mobile/MauiProgram_Debug.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Mobile.Extensions;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.Mobile.IOConfiguration;
using NewwaysAdmin.Mobile.Services;

namespace NewwaysAdmin.Mobile;

public static class MauiProgram_Debug
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
            // MINIMAL SERVICES ONLY - add one by one to find the culprit

            // Step 1: EnhancedStorageFactory (this works - we can see it in logs)
            builder.Services.AddSingleton<EnhancedStorageFactory>(sp =>
            {
                var mobileBasePath = Path.Combine(FileSystem.AppDataDirectory, "NewwaysAdmin");
                StorageConfiguration.DEFAULT_BASE_DIRECTORY = mobileBasePath;

                var logger = sp.GetRequiredService<ILogger<EnhancedStorageFactory>>();
                var factory = new EnhancedStorageFactory(logger);

                MobileStorageFolderConfiguration.ConfigureStorageFolders(factory);

                return factory;
            });

            // Step 2: Add ONLY CredentialStorageService to test
            builder.Services.AddSingleton<CredentialStorageService>();

            // COMMENT OUT everything else for now
            // builder.Services.AddMobileServices()        
            // .AddViewModels()
            // .AddPages()
            // .AddHttpClients();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            // TRY TO RESOLVE CredentialStorageService TO SEE IF IT WORKS
            var credentialService = app.Services.GetRequiredService<CredentialStorageService>();

            return app;
        }
        catch (Exception ex)
        {
            // CAPTURE THE EXACT ERROR FOR US TO SEE
            System.Diagnostics.Debug.WriteLine("=== DEPENDENCY INJECTION ERROR ===");
            System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Type: {ex.GetType().Name}");

            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Inner: {ex.InnerException.Message}");
            }

            System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine("================================");

            // Re-throw so you can see it in Visual Studio output
            throw;
        }
    }
}