// File: NewwaysAdmin.WebAdmin/Program.cs
using NewwaysAdmin.WebAdmin.Extensions;
using NewwaysAdmin.WebAdmin.Registration;
using NewwaysAdmin.WebAdmin.Middleware;
using NewwaysAdmin.WebAdmin.Services.Background;
using NewwaysAdmin.WebAdmin.Security;  // <-- Added for AuthGateMiddleware

namespace NewwaysAdmin.WebAdmin;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // 🚀 CLEAN DEPENDENCY INJECTION - NO MORE SPAGHETTI! 
        ConfigureServices(builder.Services, builder.Configuration);

        // Build the application
        WebApplication app;
        try
        {
            app = builder.Build();
        }
        catch (Exception ex)
        {
            Console.WriteLine("=== DEPENDENCY INJECTION ERROR ===");
            Console.WriteLine($"Error: {ex.Message}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }

            if (ex is AggregateException aggEx)
            {
                Console.WriteLine("=== ALL INNER EXCEPTIONS ===");
                foreach (var innerEx in aggEx.InnerExceptions)
                {
                    Console.WriteLine($"- {innerEx.Message}");
                    if (innerEx.InnerException != null)
                    {
                        Console.WriteLine($"  -> {innerEx.InnerException.Message}");
                    }
                }
            }

            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            throw;
        }

        // Configure post-build services
        app.Services.ConfigureExternalFileProcessors();
        app.Services.ConfigurePassThroughSyncPaths();

        // Configure the application pipeline
        await ConfigureApplication(app);

        app.Run();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Clean dependency chain - proper order
        services
            .AddFoundationServices(configuration)       // IOManager, Storage Factory, Config
            .AddStorageAndDataServices()                // Storage Manager, Sales Data
            .AddAuthenticationServices()                // Auth, Authorization, Security
            .AddModuleServices()                        // Modules, Workers, External Processing
            .AddBankSlipServices(configuration)         // OCR, Patterns, Bank Slip Processing
            .AddGoogleSheetsServices()                  // Google Sheets Integration
            .AddSignalRServices()                       // SignalR Hub Communication
            .AddCategoryServices()                      // Category Management System
            .AddMobileApiServices()                     // Mobile API Controllers
            .AddBackgroundServices();                   // Blazor Server, Background Workers

        // HttpContextAccessor - needed for Login.razor to get client IP
        services.AddHttpContextAccessor();
    }

    private static async Task ConfigureApplication(WebApplication app)
    {
        // ===== DEVELOPMENT SETTINGS =====
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        // ===== MIDDLEWARE PIPELINE =====
        app.UseMiddleware<MobileApiKeyMiddleware>();
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();

        // ===== AUTHENTICATION & SECURITY =====
        app.UseAuthentication();
        app.UseMiddleware<AuthGateMiddleware>();  // Auth gate AFTER authentication
        app.UseAuthorization();

        // ===== API CONTROLLERS =====
        app.MapMobileApiEndpoints();

        // ===== BLAZOR CONFIGURATION =====
        app.MapRazorPages();
        app.MapBlazorHub();
        app.MapFallbackToPage("/_Host");

        // ===== SIGNALR HUBS =====
        app.MapSignalRHubs();

        // ===== APPLICATION INITIALIZATION =====
        await app.InitializeApplicationDataAsync();

        app.Logger.LogInformation("🚀 NewwaysAdmin.WebAdmin started successfully!");
    }
}