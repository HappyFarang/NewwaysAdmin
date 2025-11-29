// File: NewwaysAdmin.WebAdmin/Registration/MobileApiServiceExtensions.cs

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class MobileApiServiceExtensions
    {
        /// <summary>
        /// Add API controller services for mobile endpoints
        /// </summary>
        public static IServiceCollection AddMobileApiServices(this IServiceCollection services)
        {
            // Enable API Controllers (required for MobileController)
            services.AddControllers();

            return services;
        }

        /// <summary>
        /// Map API controller endpoints - MUST be called before MapFallbackToPage
        /// </summary>
        public static void MapMobileApiEndpoints(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapControllers();
        }
    }
}