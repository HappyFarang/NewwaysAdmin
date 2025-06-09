// NewwaysAdmin.WebAdmin/Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.WebAdmin.Services;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGoogleSheetsTemplateServices(this IServiceCollection services)
        {
            // Register the unified template service
            services.AddSingleton<IUnifiedTemplateService, UnifiedTemplateService>();

            return services;
        }
    }
}