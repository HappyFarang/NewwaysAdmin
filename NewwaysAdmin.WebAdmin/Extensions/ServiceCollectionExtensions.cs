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

            // Export services (existing)
            services.AddScoped<BankSlipExportService>();
            services.AddScoped<UserSheetConfigService>();
            services.AddScoped<SheetConfigurationService>();

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
        /// Maximum file size for processing (in bytes)
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB

        /// <summary>
        /// Supported image file extensions
        /// </summary>
        public string[] SupportedExtensions { get; set; } =
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff"
        };

        /// <summary>
        /// Enable enhanced validation by default
        /// </summary>
        public bool EnableEnhancedValidation { get; set; } = true;

        /// <summary>
        /// Enable automatic format detection
        /// </summary>
        public bool EnableAutoFormatDetection { get; set; } = true;

        /// <summary>
        /// Timeout for OCR processing per image (in seconds)
        /// </summary>
        public int OcrTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Number of retry attempts for failed OCR operations
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 2;
    }
}