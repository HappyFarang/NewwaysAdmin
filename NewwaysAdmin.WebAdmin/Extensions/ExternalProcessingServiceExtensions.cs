// NewwaysAdmin.WebAdmin/Extensions/ExternalProcessingServiceExtensions.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NewwaysAdmin.Shared.Services.FileProcessing;
using NewwaysAdmin.WebAdmin.Services.Background;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    /// <summary>
    /// Extension methods for registering external file processing services
    /// </summary>
    public static class ExternalProcessingServiceExtensions
    {
        /// <summary>
        /// Add external file processing services to the DI container
        /// </summary>
        public static IServiceCollection AddExternalFileProcessing(this IServiceCollection services)
        {
            // Background service should be singleton (application-level, not user-specific)
            services.AddSingleton<ExternalFileProcessingService>();
            services.AddHostedService(provider => provider.GetRequiredService<ExternalFileProcessingService>());

            // File processors can be scoped since they're used within service scopes
            services.AddScoped<BankSlipFileProcessor>();

            // File indexing services (matching your existing pattern)
            services.AddScoped<NewwaysAdmin.Shared.IO.FileIndexing.Core.ExternalIndexManager>();
            services.AddScoped<NewwaysAdmin.Shared.IO.FileIndexing.Core.FileIndexEngine>();
            services.AddScoped<NewwaysAdmin.Shared.IO.FileIndexing.Core.FileIndexManager>();
            services.AddScoped<NewwaysAdmin.Shared.IO.FileIndexing.Core.FileIndexProcessingManager>();

            return services;
        }

        /// <summary>
        /// Configure external file processors (called after DI container is built)
        /// </summary>
        public static void ConfigureExternalFileProcessors(this IServiceProvider serviceProvider)
        {
            var processingService = serviceProvider.GetRequiredService<ExternalFileProcessingService>();

            // Register all processors using service scopes (since processors are scoped)
            using var scope = serviceProvider.CreateScope();
            var bankSlipProcessor = scope.ServiceProvider.GetRequiredService<BankSlipFileProcessor>();

            processingService.RegisterProcessor(bankSlipProcessor);

            // Future processors can be registered here:
            // var invoiceProcessor = scope.ServiceProvider.GetRequiredService<InvoiceFileProcessor>();
            // processingService.RegisterProcessor(invoiceProcessor);
        }
    }
}