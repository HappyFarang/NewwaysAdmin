// NewwaysAdmin.WebAdmin/Extensions/ServiceCollectionExtensions.cs
// 🔥 UPDATED: Added our new DocumentParser!

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
        /// 🚀 MODERN VERSION: Direct dictionary results, no legacy parsers!
        /// </summary>
        public static IServiceCollection AddBankSlipServices(this IServiceCollection services)
        {
            // Core OCR service (orchestrator) - now uses DocumentParser directly
            services.AddScoped<IBankSlipOcrService, BankSlipOcrService>();

            // Supporting services
            services.AddScoped<BankSlipImageProcessor>();

            // 🚀 MODERN: Our lean DocumentParser (core functionality)
            services.AddScoped<DocumentParser>();

            // Export services (for Google Sheets integration)
            services.AddScoped<BankSlipExportService>();
            services.AddScoped<SimpleEmailStorageService>();

            // Spatial OCR services
            services.AddScoped<ISpatialOcrService, SpatialOcrService>();

            // 🗑️ REMOVED: All legacy parser infrastructure
            // - BankSlipParserFactory ❌
            // - PatternBasedBankSlipParser ❌  
            // - IBankSlipParser interface ❌
            // - BankSlipValidator ❌

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