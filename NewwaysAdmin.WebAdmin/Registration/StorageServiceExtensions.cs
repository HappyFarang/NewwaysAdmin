// File: NewwaysAdmin.WebAdmin/Registration/StorageServiceExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;
using NewwaysAdmin.SharedModels.Sales;
using NewwaysAdmin.Shared.IO.Structure;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class StorageServiceExtensions
    {
        public static IServiceCollection AddStorageAndDataServices(this IServiceCollection services)
        {
            // StorageManager - Uses IOManager from Foundation layer
            services.AddSingleton<StorageManager>();

            // SalesDataProvider - Uses EnhancedStorageFactory
            services.AddScoped<SalesDataProvider>(sp =>
            {
                var factory = sp.GetRequiredService<EnhancedStorageFactory>();
                return new SalesDataProvider(factory);
            });

            // Note: There might be an existing AddStorageServices() extension method
            // If so, we'll need to integrate it here or rename this method

            return services;
        }
    }
}