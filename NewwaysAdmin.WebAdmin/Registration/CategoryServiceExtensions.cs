// File: NewwaysAdmin.WebAdmin/Registration/CategoryServiceExtensions.cs
using NewwaysAdmin.WebAdmin.Services.Categories;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class CategoryServiceExtensions
    {
        public static IServiceCollection AddCategoryServices(this IServiceCollection services)
        {
            // ===== CATEGORY MANAGEMENT =====
            services.AddScoped<CategoryService>();

            // ===== FUTURE CATEGORY SERVICES =====
            // services.AddScoped<CategoryAnalyticsService>();  
            // services.AddScoped<CategoryImportExportService>();

            return services;
        }
    }
}