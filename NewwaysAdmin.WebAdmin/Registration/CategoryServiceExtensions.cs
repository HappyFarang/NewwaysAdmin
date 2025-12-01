// File: NewwaysAdmin.WebAdmin/Registration/CategoryServiceExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.WebAdmin.Services.Categories;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class CategoryServiceExtensions
    {
        public static IServiceCollection AddCategoryServices(this IServiceCollection services)
        {
            services.AddSingleton<CategoryStorageService>();
            services.AddSingleton<CategoryService>();

            return services;
        }
    }
}