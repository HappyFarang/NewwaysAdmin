using Microsoft.Extensions.DependencyInjection;
using NewwaysAdmin.WebAdmin.Services.Testing;

namespace NewwaysAdmin.WebAdmin.Extensions
{
    public static class TestingServiceExtensions
    {
        /// <summary>
        /// Registers testing services for OCR and regex pattern development
        /// </summary>
        public static IServiceCollection AddTestingServices(this IServiceCollection services)
        {
            // OCR Testing Service
            services.AddScoped<IOcrTestingService, OcrTestingService>();

            return services;
        }

        /// <summary>
        /// Registers testing services with custom configuration
        /// </summary>
        public static IServiceCollection AddTestingServices(
            this IServiceCollection services,
            Action<TestingServiceOptions> configureOptions)
        {
            services.Configure(configureOptions);
            return services.AddTestingServices();
        }
    }

    /// <summary>
    /// Configuration options for testing services
    /// </summary>
    public class TestingServiceOptions
    {
        /// <summary>
        /// Default Google Vision API credentials path
        /// </summary>
        public string DefaultCredentialsPath { get; set; } = @"C:\Keys\purrfectocr-db2d9d796b58.json";

        /// <summary>
        /// Maximum file size for OCR processing (in bytes)
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB

        /// <summary>
        /// Supported image file extensions
        /// </summary>
        public string[] SupportedExtensions { get; set; } = new[]
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff"
        };

        /// <summary>
        /// Maximum number of regex matches to display per pattern
        /// </summary>
        public int MaxMatchesPerPattern { get; set; } = 10;

        /// <summary>
        /// Timeout for OCR processing in seconds
        /// </summary>
        public int OcrTimeoutSeconds { get; set; } = 30;
    }
}