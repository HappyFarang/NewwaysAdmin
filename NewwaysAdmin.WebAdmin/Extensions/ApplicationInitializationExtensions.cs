using NewwaysAdmin.WebAdmin.Services.Auth;

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

            app.Logger.LogInformation("Application data initialized successfully");
        }
    }
}