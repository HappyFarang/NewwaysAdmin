// NewwaysAdmin.WebAdmin/Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.WebAdmin.Services.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    public static class BankSlipServiceExtensions
    {
        /// <summary>
        /// Registers all bank slip processing services with dependency injection
        /// </summary>
        public static IServiceCollection AddBankSlipServices(this IServiceCollection services)
        {
            // Core OCR service (orchestrator)
            services.AddScoped<IBankSlipOcrService, BankSlipOcrService>();

            // Supporting services
            services.AddScoped<BankSlipImageProcessor>();
            services.AddScoped<BankSlipValidator>();

            // Parser factory and parsers
            services.AddScoped<BankSlipParserFactory>();
            services.AddScoped<OriginalSlipParser>();
            services.AddScoped<KBizSlipParser>();

            // ✅ FIXED: Re-add export services (these should be registered here for bank slips)
            services.AddScoped<BankSlipExportService>();
            services.AddScoped<SimpleEmailStorageService>();  // For user email storage

            return services;
        }

        /// <summary>
        /// Registers bank slip services with custom configuration
        /// </summary>
        public static IServiceCollection AddBankSlipServices(
            this IServiceCollection services,
            Action<BankSlipServiceOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services.AddBankSlipServices();
        }
    }

    /// <summary>
    /// Configuration options for bank slip services
    /// </summary>
    public class BankSlipServiceOptions
    {
        /// <summary>
        /// Default Google Vision API credentials path
        /// </summary>
        public string DefaultCredentialsPath { get; set; } = @"C:\Keys\purrfectocr-db2d9d796b58.json";

        /// <summary>
        /// Enable enhanced date validation
        /// </summary>
        public bool EnableEnhancedValidation { get; set; } = true;

        /// <summary>
        /// Enable automatic K-BIZ format detection
        /// </summary>
        public bool EnableAutoFormatDetection { get; set; } = true;

        /// <summary>
        /// Maximum file size in bytes for processing
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 50_000_000; // 50MB
    }
}