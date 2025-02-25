using NewwaysAdmin.WebAdmin.Services.Auth;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    public static class ApplicationInitializationExtensions
    {
        public static async Task InitializeApplicationDataAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();

            // Initialize default admin user
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            await authService.InitializeDefaultAdminAsync();

            app.Logger.LogInformation("Application data initialized successfully");
        }
    }
}