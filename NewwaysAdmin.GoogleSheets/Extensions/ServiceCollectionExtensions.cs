using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.GoogleSheets.Extensions
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add Google Sheets services to the service collection
        /// </summary>
        public static IServiceCollection AddGoogleSheets(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Configure options
            services.Configure<GoogleSheetsOptions>(
                configuration.GetSection(GoogleSheetsOptions.SectionName));

            // Register core services
            services.AddScoped<IGoogleSheetsService, GoogleSheetsService>();
            services.AddScoped<ISheetBuilder<BankSlipData>, BankSlipSheetBuilder>();

            return services;
        }

        /// <summary>
        /// Add Google Sheets services with custom options
        /// </summary>
        public static IServiceCollection AddGoogleSheets(
            this IServiceCollection services,
            Action<GoogleSheetsOptions> configureOptions)
        {
            services.Configure(configureOptions);

            // Register core services
            services.AddScoped<IGoogleSheetsService, GoogleSheetsService>();
            services.AddScoped<ISheetBuilder<BankSlipData>, BankSlipSheetBuilder>();

            return services;
        }

        /// <summary>
        /// Add user checkbox management services with storage
        /// </summary>
        public static IServiceCollection AddUserCheckboxServices<TStorage>(
            this IServiceCollection services)
            where TStorage : class, NewwaysAdmin.Shared.IO.IDataStorage<List<UserCheckboxConfig>>
        {
            services.AddScoped<NewwaysAdmin.Shared.IO.IDataStorage<List<UserCheckboxConfig>>, TStorage>();
            services.AddScoped<IUserCheckboxService, UserCheckboxService>();

            return services;
        }
    }
}