// NewwaysAdmin.WebAdmin/Extensions/ServiceCollectionExtensions.cs
// 🔥 UPDATED: Ditched the old rigid parsers, embracing the new pattern-based future!

using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.GoogleSheets.Services;
using NewwaysAdmin.WebAdmin.Services.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips.Parsers;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;
using NewwaysAdmin.SharedModels.Services.Ocr; // NEW: Pattern services
using NewwaysAdmin.SharedModels.Models.Documents;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    public static class BankSlipServiceExtensions
    {
        /// <summary>
        /// Registers all bank slip processing services with dependency injection
        /// 🚀 MODERN VERSION: Pattern-based parsing, no more hardcoded nonsense!
        /// </summary>
        public static IServiceCollection AddBankSlipServices(this IServiceCollection services)
        {
            // Core OCR service (orchestrator)
            services.AddScoped<IBankSlipOcrService, BankSlipOcrService>();

            // Supporting services
            services.AddScoped<BankSlipImageProcessor>();
            services.AddScoped<BankSlipValidator>();

            // 🔥 NEW: Modern pattern-based parser factory and parser
            services.AddScoped<BankSlipParserFactory>();
            services.AddScoped<PatternBasedBankSlipParser>();

            // 🗑️ REMOVED: Old rigid parsers - GOODBYE FOREVER!
            // services.AddScoped<OriginalSlipParser>();  // 🔥 DELETED
            // services.AddScoped<KBizSlipParser>();      // 🔥 DELETED

            // Export services (keep these for Google Sheets integration)
            services.AddScoped<BankSlipExportService>();
            services.AddScoped<SimpleEmailStorageService>();

            // Spatial OCR services
            services.AddScoped<ISpatialOcrService, SpatialOcrService>();

            // 🎯 NEW: Pattern management services (should already be registered in Program.cs)
            // These are the core of our new system:
            // - PatternManagementService (already registered in Program.cs)
            // - PatternLoaderService (already registered in Program.cs)

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
    /// 🎯 MODERN VERSION: Pattern-based configuration
    /// </summary>
    public class BankSlipServiceOptions
    {
        /// <summary>
        /// Default Google Vision API credentials path
        /// </summary>
        public string DefaultCredentialsPath { get; set; } = @"C:\Keys\purrfectocr-db2d9d796b58.json";

        /// <summary>
        /// Enable enhanced validation (now pattern-aware)
        /// </summary>
        public bool EnableEnhancedValidation { get; set; } = true;

        /// <summary>
        /// Enable automatic format detection (now pattern-based)
        /// </summary>
        public bool EnableAutoFormatDetection { get; set; } = true;

        /// <summary>
        /// Maximum file size in bytes for processing
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 50_000_000; // 50MB

        /// <summary>
        /// 🆕 NEW: Default document type for bank slip processing
        /// </summary>
        public string DefaultDocumentType { get; set; } = "BankSlips";

        /// <summary>
        /// 🆕 NEW: Enable pattern debugging (stores extra processing info)
        /// </summary>
        public bool EnablePatternDebugging { get; set; } = true;
    }
}