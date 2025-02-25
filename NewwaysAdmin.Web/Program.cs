using Microsoft.AspNetCore.Components.Authorization;
using NewwaysAdmin.Web.Authentication;
using NewwaysAdmin.Web.Services;
using NewwaysAdmin.IO.Manager;
using Microsoft.Extensions.Logging;
using NetEscapades.AspNetCore.SecurityHeaders;
using NewwaysAdmin.Shared.Configuration;

namespace NewwaysAdmin.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var builder = WebApplication.CreateBuilder(args);

                // Add services to the container.
                builder.Services.AddRazorPages();
                builder.Services.AddServerSideBlazor(options =>
                {
                    options.DetailedErrors = true;
                });
                builder.Services.AddSingleton<MachineConfigProvider>();

                // Configure logging
                builder.Logging.ClearProviders();
                builder.Logging.AddConsole();
                builder.Logging.AddDebug();

                // Configure IO Manager
                builder.Services.AddSingleton<IOConfigLoader>();
                builder.Services.AddSingleton<MachineConfigProvider>();
                builder.Services.AddSingleton<IOManagerOptions>(sp =>
                {
                    var configLoader = sp.GetRequiredService<IOConfigLoader>();
                    var config = configLoader.LoadConfigAsync().GetAwaiter().GetResult();
                    return new IOManagerOptions
                    {
                        LocalBaseFolder = config.LocalBaseFolder,
                        ServerDefinitionsPath = config.ServerDefinitionsPath,
                        ApplicationName = "NewwaysAdmin"
                    };
                });

                builder.Services.AddSingleton<IOManager>();

                // Add services
                builder.Services.AddSingleton<IUserService>(sp =>
                {
                    var logger = sp.GetRequiredService<ILogger<UserService>>();
                    var ioManager = sp.GetRequiredService<IOManager>();
                    return new UserService(ioManager, logger);
                });

                builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();
                builder.Services.AddAuthenticationCore();
                builder.Services.AddAuthorizationCore();

                var app = builder.Build();

                // Configure the HTTP request pipeline
                if (!app.Environment.IsDevelopment())
                {
                    app.UseExceptionHandler("/Error");
                    app.UseHsts();
                }

                // Add security headers
                app.UseSecurityHeaders(new HeaderPolicyCollection()
                    .AddDefaultSecurityHeaders()
                    .AddCustomHeader("Content-Security-Policy",
                        "default-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
                        "script-src 'self' 'unsafe-inline' 'unsafe-eval' 'wasm-unsafe-eval' https://cdn.jsdelivr.net; " +
                        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
                        "img-src 'self' data: https:; " +
                        "font-src 'self' data: https://cdn.jsdelivr.net; " +
                        "connect-src 'self' ws: wss: http: https: http://localhost:* https://localhost:*; " +
                        "frame-src 'self'; " +
                        "worker-src 'self' blob:; " +
                        "child-src 'self' blob: data:; " +
                        "base-uri 'self';"));
                app.UseHttpsRedirection();
                app.UseStaticFiles();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();

                app.MapBlazorHub();
                app.MapFallbackToPage("/_Host");

                // Create initial admin user if no users exist
                using (var scope = app.Services.CreateScope())
                {
                    try
                    {
                        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                        logger.LogInformation("Checking for admin user...");
                        if (!await userService.UserExistsAsync("admin"))
                        {
                            logger.LogInformation("Creating admin user...");
                            await userService.CreateUserAsync("admin", "password123", "Admin");
                            logger.LogInformation("Admin user created successfully");
                        }
                        else
                        {
                            logger.LogInformation("Admin user already exists");
                        }
                    }
                    catch (Exception ex)
                    {
                        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                        logger.LogError(ex, "Error initializing users");
                    }
                }

                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                throw;
            }
        }
    }
}