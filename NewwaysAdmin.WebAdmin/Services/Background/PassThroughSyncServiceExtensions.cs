// NewwaysAdmin.WebAdmin/Services/Background/PassThroughSyncServiceExtensions.cs

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NewwaysAdmin.WebAdmin.Services.Background
{
    /// <summary>
    /// Extension methods for registering and configuring PassThroughSyncService
    /// </summary>
    public static class PassThroughSyncServiceExtensions
    {
        /// <summary>
        /// Add PassThroughSyncService to the service collection
        /// </summary>
        public static IServiceCollection AddPassThroughSyncService(this IServiceCollection services)
        {
            services.AddSingleton<PassThroughSyncService>();
            services.AddHostedService<PassThroughSyncService>(provider =>
                provider.GetRequiredService<PassThroughSyncService>());

            return services;
        }

        /// <summary>
        /// Configure PassThroughSyncService with sync paths during application startup
        /// </summary>
        public static IServiceProvider ConfigurePassThroughSyncPaths(this IServiceProvider serviceProvider)
        {
            var syncService = serviceProvider.GetRequiredService<PassThroughSyncService>();

            // Register WorkerAttendance sync
            syncService.RegisterSyncPath(
                remotePath: "N:\\WorkerAttendance",
                localFolderName: "WorkerAttendance",
                description: "Worker attendance and registration data from remote face scan machines"
            );

            // Future sync paths can be added here:
            // syncService.RegisterSyncPath(
            //     remotePath: "N:\\Reports",
            //     localFolderName: "MonthlyReports", 
            //     description: "Monthly business reports from accounting system"
            // );

            return serviceProvider;
        }
    }
}