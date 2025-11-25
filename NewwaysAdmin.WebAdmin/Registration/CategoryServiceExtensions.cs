// File: NewwaysAdmin.WebAdmin/Registration/CategoryServiceExtensions.cs
using NewwaysAdmin.WebAdmin.Services.Categories;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class CategoryServiceExtensions
    {
        public static IServiceCollection AddCategoryServices(this IServiceCollection services)
        {
            // ===== CORE CATEGORY SERVICES =====

            // Storage layer - handles all persistence
            services.AddScoped<CategoryStorageService>();

            // Business logic services
            services.AddScoped<CategoryUsageService>();
            services.AddScoped<BusinessLocationService>();
            services.AddScoped<MobileSyncService>();

            // Main orchestrator service (depends on others)
            services.AddScoped<CategoryService>();

            // ===== FUTURE CATEGORY SERVICES =====
            // services.AddScoped<CategoryAnalyticsService>();  
            // services.AddScoped<CategoryImportExportService>();
            // services.AddScoped<CategoryValidationService>();

            return services;
        }
    }
}