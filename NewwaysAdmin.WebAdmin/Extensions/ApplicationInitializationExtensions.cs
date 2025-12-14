// File: NewwaysAdmin.WebAdmin/Extensions/ApplicationInitializationExtensions.cs

using NewwaysAdmin.WebAdmin.Services.Auth;
using NewwaysAdmin.WebAdmin.Services.Modules;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    public static class ApplicationInitializationExtensions
    {
        public static async Task InitializeApplicationDataAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();

            // Initialize default users
            var userInitService = scope.ServiceProvider.GetRequiredService<UserInitializationService>();
            await userInitService.EnsureAdminUserExistsAsync();

            // Initialize navigation modules from ModuleDefinitions
            var moduleRegistry = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
            await moduleRegistry.InitializeModulesAsync();

            app.Logger.LogInformation("Application data initialized successfully");
        }
    }
}