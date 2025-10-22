// File: NewwaysAdmin.WebAdmin/Registration/ModuleServiceExtensions.cs
using NewwaysAdmin.WebAdmin.Services.Modules;
using NewwaysAdmin.WebAdmin.Services.Background;
using NewwaysAdmin.WebAdmin.Services.Workers;
using NewwaysAdmin.WebAdmin.Extensions;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class ModuleServiceExtensions
    {
        public static IServiceCollection AddModuleServices(this IServiceCollection services)
        {
            // ===== MODULE SYSTEM =====
            services.AddModuleRegistry();

            // ===== EXTERNAL FILE PROCESSING =====
            services.AddExternalFileProcessing();
            services.AddPassThroughSyncService();

            // ===== WORKER ACTIVITY SERVICES =====
            services.AddScoped<WorkerDashboardService>();
            services.AddScoped<WorkerSettingsService>();
            services.AddScoped<WorkerPaymentCalculator>();
            services.AddScoped<WorkerWeeklyService>();
            services.AddScoped<AdjustmentService>();

            // ===== WORKER DATA SERVICES =====
            services.AddScoped<WorkerDataService>();
            services.AddScoped<IWeeklyTableCalculationService, WeeklyTableCalculationService>();
            services.AddScoped<IColumnDefinitionService, ColumnDefinitionService>();

            return services;
        }
    }
}