// NewwaysAdmin.SharedModels/Models/Ocr/Core/ISpatialOcrService.cs
using System.Threading.Tasks;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Interface for spatial OCR extraction service
    /// Extracts text with bounding box coordinates instead of plain text
    /// </summary>
    public interface ISpatialOcrService
    {
        /// <summary>
        /// Extract spatial text data from an image
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <param name="collection">Processing collection with settings (optional)</param>
        /// <returns>Spatial OCR extraction result</returns>
        Task<OcrExtractionResult> ExtractSpatialTextAsync(string imagePath, SlipCollection? collection = null);

        /// <summary>
        /// Extract spatial text data from an image with minimal processing
        /// Used for testing and debugging
        /// </summary>
        /// <param name="imagePath">Path to the image file</param>
        /// <returns>Spatial OCR extraction result</returns>
        Task<OcrExtractionResult> ExtractSpatialTextBasicAsync(string imagePath);

        /// <summary>
        /// Test if the service is properly configured and can connect to Google Vision API
        /// </summary>
        /// <returns>True if service is ready, false otherwise</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Get service information and configuration status
        /// </summary>
        /// <returns>Service status information</returns>
        Task<SpatialOcrServiceInfo> GetServiceInfoAsync();
    }

    /// <summary>
    /// Information about the spatial OCR service status and configuration
    /// </summary>
    public class SpatialOcrServiceInfo
    {
        public bool IsConfigured { get; set; } = false;
        public bool CanConnectToGoogleVision { get; set; } = false;
        public string GoogleVisionApiVersion { get; set; } = string.Empty;
        public string CredentialsPath { get; set; } = string.Empty;
        public bool CredentialsFileExists { get; set; } = false;
        public string LastError { get; set; } = string.Empty;
        public DateTime LastTested { get; set; } = DateTime.MinValue;

        public string GetStatusSummary()
        {
            if (!IsConfigured)
                return "Not configured";

            if (!CredentialsFileExists)
                return "Credentials file not found";

            if (!CanConnectToGoogleVision)
                return $"Cannot connect to Google Vision API: {LastError}";

            return "Ready";
        }
    }
}