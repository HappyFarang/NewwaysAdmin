using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Primitives;
using NewwaysAdmin.Shared.IO;
using NewwaysAdmin.WebAdmin.Models.Auth;
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
using Microsoft.AspNetCore.Authorization;
using NewwaysAdmin.WebAdmin.Authorization;
using NewwaysAdmin.WebAdmin.Services.BankSlips;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.GoogleSheets.Extensions;
using NewwaysAdmin.GoogleSheets.Layouts;
using NewwaysAdmin.GoogleSheets.Interfaces;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.GoogleSheets;




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
        services.AddServerSideBlazor(options =>
        {
            options.DetailedErrors = true; // Shows better error messages in development
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
            options.DisconnectedCircuitMaxRetained = 100;
            options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(1);
        });
        services.AddHttpContextAccessor();
        services.AddAuthorizationCore();

        // Add logging
        services.AddLogging(logging =>
        {
            logging.AddConsole();
            logging.AddDebug();
        });

        services.AddGoogleSheetsTemplateServices();

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

        // Register UserInitializationService
        services.AddScoped<UserInitializationService>();

        // Add ConfigProvider that uses IOManager
        services.AddSingleton<ConfigProvider>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<ConfigProvider>();
            var ioManager = sp.GetRequiredService<IOManager>();
            return new ConfigProvider(logger, ioManager);
        });

        // Register SalesDataProvider 
        services.AddScoped<SalesDataProvider>(sp =>
        {
            var factory = sp.GetRequiredService<EnhancedStorageFactory>();
            return new SalesDataProvider(factory);
        });

        // Enhanced Authorization
        services.AddAuthorizationCore(options =>
        {
            // Create policies for each module and access level combination
            var modules = new[] { "home", "test", "settings", "sales", "accounting", "accounting.bankslips" };
            var accessLevels = new[] { AccessLevel.Read, AccessLevel.ReadWrite };

            foreach (var module in modules)
            {
                foreach (var level in accessLevels)
                {
                    // Module policies
                    options.AddPolicy($"Module_{module}_{level}", policy =>
                        policy.Requirements.Add(new ModuleAccessRequirement(module, level)));

                    // Page policies
                    options.AddPolicy($"Page_{module}_{level}", policy =>
                        policy.Requirements.Add(new PageAccessRequirement(module, level)));
                }
            }

            // Admin only policy
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole("Admin"));
        });

        // Register authorization handlers
        services.AddScoped<IAuthorizationHandler, ModuleAccessHandler>();
        services.AddScoped<IAuthorizationHandler, PageAccessHandler>();

        // Circuit handling
        services.AddScoped<CircuitHandler, CustomCircuitHandler>();
        services.AddSingleton<ICircuitManager, CircuitManager>();

        // Authentication and navigation - FIXED: Only register once
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<INavigationService, NavigationService>();
        services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();

        // Module system
        services.AddModuleRegistry();

        // Bank slip service
        services.AddScoped<IBankSlipOcrService, BankSlipOcrService>();

        // Google Sheets Configuration
        var googleSheetsConfig = new GoogleSheetsConfig
        {
            CredentialsPath = @"C:\Keys\newwaysadmin-sheets-service.json",
            ApplicationName = "NewwaysAdmin Google Sheets Integration",
            AutoShareWithUser = true,  // Make sure this is enabled
            DefaultShareEmail = "superfox75@gmail.com"
        };

        services.AddScoped<ITemplateStorageService, TemplateStorageService>();

        // Add Google Sheets services
        services.AddGoogleSheetsServices(googleSheetsConfig);
        services.AddSingleton<ModuleColumnRegistry>();


        // Register User Sheet Config Service
        services.AddScoped<UserSheetConfigService>(sp =>
        {
            var storageManager = sp.GetRequiredService<StorageManager>();
            var logger = sp.GetRequiredService<ILogger<UserSheetConfigService>>();

            var userConfigStorage = storageManager.GetStorageSync<List<UserSheetConfig>>("GoogleSheets_UserConfigs");
            var adminConfigStorage = storageManager.GetStorageSync<List<AdminSheetConfig>>("GoogleSheets_AdminConfigs");

            return new UserSheetConfigService(userConfigStorage, adminConfigStorage, logger);
        });

        // Register Sheet Configuration Service
        services.AddScoped<SheetConfigurationService>(sp =>
        {
            var columnRegistry = sp.GetRequiredService<ModuleColumnRegistry>();
            var storageManager = sp.GetRequiredService<StorageManager>();
            var logger = sp.GetRequiredService<ILogger<SheetConfigurationService>>();

            var userConfigStorage = storageManager.GetStorageSync<UserSheetConfiguration>("GoogleSheets_UserConfigs");
            var customColumnStorage = storageManager.GetStorageSync<CustomColumnLibrary>("GoogleSheets_CustomColumns");

            return new SheetConfigurationService(columnRegistry, userConfigStorage, customColumnStorage, logger);
        });

        // Register the Bank Slip layout
        services.AddSheetLayout(new BankSlipSheetLayout());

        // Register the Bank Slip export service with explicit config
        services.AddScoped<BankSlipExportService>(sp =>
        {
            var googleSheetsService = sp.GetRequiredService<GoogleSheetsService>();
            var userConfigService = sp.GetRequiredService<UserSheetConfigService>();
            var bankSlipLayout = sp.GetRequiredService<ISheetLayout<BankSlipData>>();
            var config = sp.GetRequiredService<GoogleSheetsConfig>();
            var logger = sp.GetRequiredService<ILogger<BankSlipExportService>>();

            return new BankSlipExportService(googleSheetsService, userConfigService, bankSlipLayout, config, logger);
        });

        // Register User Sheet Config Service
        services.AddScoped<UserSheetConfigService>(sp =>
        {
            var storageManager = sp.GetRequiredService<StorageManager>();
            var logger = sp.GetRequiredService<ILogger<UserSheetConfigService>>();

            var userConfigStorage = storageManager.GetStorageSync<List<UserSheetConfig>>("GoogleSheets_UserConfigs");
            var adminConfigStorage = storageManager.GetStorageSync<List<AdminSheetConfig>>("GoogleSheets_AdminConfigs");

            return new UserSheetConfigService(userConfigStorage, adminConfigStorage, logger);
        });
    }

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