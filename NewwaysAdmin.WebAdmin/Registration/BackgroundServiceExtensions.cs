// File: NewwaysAdmin.WebAdmin/Registration/BackgroundServiceExtensions.cs
using Microsoft.Extensions.DependencyInjection;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class BackgroundServiceExtensions
    {
        public static IServiceCollection AddBackgroundServices(this IServiceCollection services)
        {
            // ===== BLAZOR SERVER CONFIGURATION =====
            services.AddRazorPages();
            services.AddServerSideBlazor();

            // ===== API CONTROLLERS (for MobileController) =====
            services.AddControllers();

            return services;
        }
    }
}