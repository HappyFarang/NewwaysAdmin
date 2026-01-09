// File: NewwaysAdmin.WebAdmin/Registration/BankSlipServiceExtensions.cs
using NewwaysAdmin.WebAdmin.Services.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Templates;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SharedModels.Services.Ocr;
using NewwaysAdmin.SharedModels.Services.Parsing;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;
using NewwaysAdmin.WebAdmin.Extensions;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Processing;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Export;
// Email service - check actual namespace when we see the error

namespace NewwaysAdmin.WebAdmin.Registration
{
    public static class BankSlipServiceExtensions
    {
        public static IServiceCollection AddBankSlipServices(this IServiceCollection services, IConfiguration configuration)
        {
            // ===== BANKSLIP OPTIONS CONFIGURATION =====
            services.Configure<BankSlipServiceOptions>(options =>
            {
                options.DefaultCredentialsPath = configuration.GetValue<string>("BankSlips:DefaultCredentialsPath")
                    ?? @"C:\Keys\purrfectocr-db2d9d796b58.json";
                options.MaxFileSizeBytes = 50_000_000; // 50MB
                options.EnableEnhancedValidation = true;
                options.EnableAutoFormatDetection = true;
            });

            // ===== CORE BANKSLIP SERVICES =====
            services.AddScoped<BankSlipOcrService>();
            services.AddScoped<BankSlipImageProcessor>();
            services.AddScoped<DocumentParser>();
            services.AddScoped<BankSlipExportService>();
            services.AddScoped<BankSlipCollectionExtensions>();
            services.AddScoped<BankSlipImageService>();
            services.AddScoped<BankSlipExcelExportService>();
            services.AddScoped<BillUploadService>();
            services.AddScoped<BankSlipReviewSyncService>();

            // ===== OCR & SPATIAL SERVICES =====
            services.AddScoped<ISpatialOcrService, SpatialOcrService>();
            services.AddScoped<OcrFieldAnalyzerService>();
            services.AddScoped<SpatialPatternMatcher>();
            services.AddScoped<TimeParsingService>();
            services.AddScoped<DateParsingService>();
            services.AddScoped<NumberParsingService>();
            services.AddScoped<SpatialResultParser>();

            // ===== PATTERN MANAGEMENT SERVICES =====
            services.AddScoped<PatternManagementService>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<PatternManagementService>>();
                return new PatternManagementService(sp, logger);  // Pass IServiceProvider
            });

            services.AddScoped<PatternLoaderService>(sp =>
            {
                var patternManagement = sp.GetRequiredService<PatternManagementService>();
                var logger = sp.GetRequiredService<ILogger<PatternLoaderService>>();
                return new PatternLoaderService(patternManagement, logger);
            });

            // ===== TABLE PARSING & STORAGE =====
            services.AddScoped<NewwaysAdmin.Shared.Tables.DictionaryTableParser>();
            services.AddScoped<CustomColumnStorageService>();
            services.AddScoped<SimpleEmailStorageService>();

            return services;
        }
    }
}