// File: NewwaysAdmin.WebAdmin/Registration/GoogleSheetsServiceExtensions.cs
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.GoogleSheets.Models;
using NewwaysAdmin.GoogleSheets.Extensions;
using NewwaysAdmin.GoogleSheets.Layouts;
using NewwaysAdmin.GoogleSheets.Interfaces;
using NewwaysAdmin.IO.Manager;
using NewwaysAdmin.WebAdmin.Infrastructure.Storage;

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class GoogleSheetsServiceExtensions
    {
        public static IServiceCollection AddGoogleSheetsServices(this IServiceCollection services)
        {
            // ===== GOOGLE SHEETS CONFIGURATION =====
            var googleSheetsConfig = new GoogleSheetsConfig
            {
                CredentialsPath = "",
                PersonalAccountOAuthPath = @"C:\Keys\oauth2-credentials.json",
                ApplicationName = "NewwaysAdmin Google Sheets Integration",
                DefaultShareEmail = "superfox75@gmail.com"
            };

            // ===== GOOGLE SHEETS CORE SERVICES =====
            services.AddGoogleSheetsServices(googleSheetsConfig);
            services.AddSingleton<ModuleColumnRegistry>();

            // ===== SHEET LAYOUTS =====
            services.AddSheetLayout(new BankSlipSheetLayout());

            // ===== USER SHEET CONFIGURATION SERVICES =====
            services.AddScoped<UserSheetConfigService>(sp =>
            {
                var storageManager = sp.GetRequiredService<StorageManager>();
                var logger = sp.GetRequiredService<ILogger<UserSheetConfigService>>();
                var userConfigStorage = storageManager.GetStorageSync<List<UserSheetConfig>>("GoogleSheets_UserConfigs");
                var adminConfigStorage = storageManager.GetStorageSync<List<AdminSheetConfig>>("GoogleSheets_AdminConfigs");
                return new UserSheetConfigService(userConfigStorage, adminConfigStorage, logger);
            });

            services.AddScoped<SheetConfigurationService>(sp =>
            {
                var columnRegistry = sp.GetRequiredService<ModuleColumnRegistry>();
                var ioManager = sp.GetRequiredService<IOManager>();
                var logger = sp.GetRequiredService<ILogger<SheetConfigurationService>>();
                var config = sp.GetRequiredService<GoogleSheetsConfig>();
                return new SheetConfigurationService(columnRegistry, ioManager, logger, config);
            });

            return services;
        }
    }
}