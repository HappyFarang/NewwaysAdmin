using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Primitives;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Services.Navigation;
using NewwaysAdmin.WebAdmin.Authentication;
using NewwaysAdmin.WebAdmin.Extensions;
using NewwaysAdmin.WebAdmin.Services.Circuit;
using NewwaysAdmin.WebAdmin.Services.Modules;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.SharedModels.Config;
using NewwaysAdmin.Shared.IO.Structure;
using NewwaysAdmin.SharedModels.Sales;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NewwaysAdmin.WebAdmin;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        ConfigureServices(builder.Services, builder.Configuration, args);

        var app = builder.Build();

        await ConfigureApplication(app);

        app.Run();
    }
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration, string[] args)
    {
        // Basic Blazor services
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddAuthorizationCore();

        // Add logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
        });

        // Pass command line args to MachineConfigProvider for test modes
        services.AddSingleton<MachineConfigProvider>(sp => {
            var logger = sp.GetRequiredService<ILogger<MachineConfigProvider>>();
            return new MachineConfigProvider(logger, args);
        });

        // Add IOConfigLoader
        services.AddSingleton<IOConfigLoader>();

        // Add IOManagerOptions first because IOManager depends on it
        services.AddSingleton<IOManagerOptions>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<IOConfigLoader>>();
            var configLoader = sp.GetRequiredService<IOConfigLoader>();
            var config = configLoader.LoadConfigAsync().GetAwaiter().GetResult();

            // Check for command line args
            bool isTestServer = args.Contains("--testserver", StringComparer.OrdinalIgnoreCase);
            bool isTestClient = args.Contains("--testclient", StringComparer.OrdinalIgnoreCase);

            string baseFolder = config.LocalBaseFolder ?? "C:/NewwaysData";

            // Adjust paths for test modes
            if (isTestServer)
            {
                baseFolder = "C:/TestServer/Data";
                logger.LogInformation("Using TEST SERVER mode with base folder: {Path}", baseFolder);
            }
            else if (isTestClient)
            {
                baseFolder = "C:/TestClient/Data";
                logger.LogInformation("Using TEST CLIENT mode with base folder: {Path}", baseFolder);
            }

            return new IOManagerOptions
            {
                LocalBaseFolder = baseFolder,
                ServerDefinitionsPath = config.ServerDefinitionsPath ?? "X:/NewwaysAdmin/Definitions",
                ApplicationName = "NewwaysAdmin.WebAdmin"
            };
        });

        // Add EnhancedStorageFactory which IOManager needs
        services.AddSingleton<EnhancedStorageFactory>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<EnhancedStorageFactory>();
            return new EnhancedStorageFactory(logger);
        });

        // Now register IOManager with its dependencies satisfied
        services.AddSingleton<IOManager>();

        // ConfigSyncTracker depends on IOManager
        services.AddSingleton<ConfigSyncTracker>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
            var ioManager = sp.GetRequiredService<IOManager>();
            return new ConfigSyncTracker(ioManager.LocalBaseFolder, logger);
        });

        // Storage system - uses IOManager
        services.AddSingleton<StorageManager>();
        services.AddStorageServices();

        // Add ConfigProvider that uses IOManager
        services.AddSingleton<ConfigProvider>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ConfigProvider>();
            var ioManager = sp.GetRequiredService<IOManager>();
            return new ConfigProvider(logger, ioManager);
        });

        // Register SalesDataProvider - now depends on StorageManager and ConfigProvider
        services.AddScoped<SalesDataProvider>(sp =>
        {
            // Get the EnhancedStorageFactory directly for now
            // This is temporary until we update SalesDataProvider to use IOManager
            var factory = sp.GetRequiredService<EnhancedStorageFactory>();
            return new SalesDataProvider(factory);
        });


        // Circuit handling
        services.AddScoped<CircuitHandler, CustomCircuitHandler>();
        services.AddSingleton<ICircuitManager, CircuitManager>();

        // Authentication and navigation - now using StorageManager
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<INavigationService, NavigationService>();
        services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

        // Module system
        services.AddModuleRegistry();
    }

    /*
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Basic Blazor services
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddAuthorizationCore();

        // Add logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
        });

        // IO Manager configuration
        services.AddSingleton<IOConfigLoader>();
        services.AddSingleton<MachineConfigProvider>();

        // Add IOManagerOptions first because IOManager depends on it
        services.AddSingleton<IOManagerOptions>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<IOConfigLoader>>();
            var configLoader = new IOConfigLoader(logger);
            var config = configLoader.LoadConfigAsync().GetAwaiter().GetResult();

            return new IOManagerOptions
            {
                LocalBaseFolder = config.LocalBaseFolder ?? "C:/NewwaysData",
                ServerDefinitionsPath = config.ServerDefinitionsPath ?? "X:/NewwaysAdmin/Definitions",
                ApplicationName = "NewwaysAdmin.WebAdmin"
            };
        });

        // Now register IOManager with its dependencies satisfied
        services.AddSingleton<IOManager>();

        // ConfigSyncTracker depends on IOManager
        services.AddSingleton<ConfigSyncTracker>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<ConfigSyncTracker>>();
            var ioManager = sp.GetRequiredService<IOManager>();
            return new ConfigSyncTracker(ioManager.LocalBaseFolder, logger);
        });

        // Storage system
        services.AddStorageServices();

        // Add EnhancedStorageFactory
        services.AddSingleton<EnhancedStorageFactory>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<EnhancedStorageFactory>();
            return new EnhancedStorageFactory(logger);
        });

        // Add StorageManager with logger
        services.AddSingleton<StorageManager>();
        services.AddScoped<SalesDataProvider>();

        // Add ConfigProvider that uses IOManager
        services.AddSingleton<ConfigProvider>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ConfigProvider>();
            var ioManager = sp.GetRequiredService<IOManager>();  // Changed to use IOManager
            return new ConfigProvider(logger, ioManager);  // Pass IOManager, not EnhancedStorageFactory
        });

        // Circuit handling
        services.AddScoped<CircuitHandler, CustomCircuitHandler>();
        services.AddSingleton<ICircuitManager, CircuitManager>();

        // Authentication and navigation
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<INavigationService, NavigationService>();
        services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

        // Module system
        services.AddModuleRegistry();
    }
    */
    private static async Task ConfigureApplication(WebApplication app)
    {
        // Initialize core systems
        try
        {
            using var scope = app.Services.CreateScope();

            // Get IOManager and initialize directories
            var ioManager = scope.ServiceProvider.GetRequiredService<IOManager>();
            app.Logger.LogInformation("IOManager initialized with base folder: {Path}", ioManager.LocalBaseFolder);

            // Initialize storage manager which registers all folders
            var storageManager = scope.ServiceProvider.GetRequiredService<StorageManager>();
            await storageManager.InitializeAsync();
            app.Logger.LogInformation("Storage system initialized successfully");

            // Initialize module system
            var moduleRegistry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
            await moduleRegistry.InitializeModulesAsync();
            app.Logger.LogInformation("Module system initialized successfully");

            // Initialize default admin user
            await app.InitializeApplicationDataAsync();
            app.Logger.LogInformation("Application data initialized successfully");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "An error occurred during application initialization");
            throw;
        }

        // Configure CSP
        app.Use(async (context, next) =>
        {
            var cspValue = new StringValues(new[]
            {
                "default-src 'self';" +
                "script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval';" +
                "style-src 'self' 'unsafe-inline';" +
                "img-src 'self' data:;" +
                "font-src 'self';" +
                "connect-src 'self' ws: wss: http://localhost:* https://localhost:*"
            });

            context.Response.Headers.Remove("Content-Security-Policy");
            context.Response.Headers["Content-Security-Policy"] = cspValue;
            await next();
        });

        // Configure environment-specific settings
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // Configure middleware pipeline
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();

        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");
    }
}