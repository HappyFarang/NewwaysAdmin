// File: NewwaysAdmin.WebAdmin/Registration/CategoryServiceExtensions.cs
using NewwaysAdmin.WebAdmin.Services.Categories;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class CategoryServiceExtensions
    {
        public static IServiceCollection AddCategoryServices(this IServiceCollection services)
        {
            // Storage layer
            services.AddScoped<CategoryStorageService>();

            // Business logic services
            services.AddScoped<BusinessLocationService>();
            services.AddScoped<MobileSyncService>();

            // Main orchestrator service
            services.AddScoped<CategoryService>();

            return services;
        }
    }
}