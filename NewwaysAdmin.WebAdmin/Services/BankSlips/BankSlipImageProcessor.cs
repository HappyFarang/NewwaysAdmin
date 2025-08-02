
// NewwaysAdmin.WebAdmin/Services/BankSlips/BankSlipImageProcessor.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class BankSlipImageProcessor
    {
        private readonly ILogger<BankSlipImageProcessor> _logger;

        public BankSlipImageProcessor(ILogger<BankSlipImageProcessor> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Processes image for better OCR results based on collection settings
        /// </summary>
        public async Task<string?> ProcessImageAsync(string imagePath, ProcessingParameters settings)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    _logger.LogError("Image file not found: {ImagePath}", imagePath);
                    return null;
                }

                var fileName = Path.GetFileName(imagePath);
                var tempDir = Path.Combine(Path.GetTempPath(), "BankSlipProcessing");
                Directory.CreateDirectory(tempDir);

                var outputPath = Path.Combine(tempDir, $"processed_{fileName}.png");

                _logger.LogDebug("Processing image {ImagePath} with settings: Sigma={Sigma}, Window={Window}",
                    fileName, settings.GaussianSigma, settings.BinarizationWindow);

                // Process through multiple passes if configured
                var processedPath = imagePath;

                foreach (var pass in settings.ProcessingPasses)
                {
                    var passSettings = GetSettingsForPass(pass, settings);
                    var passOutputPath = Path.Combine(tempDir, $"pass_{pass}_{fileName}.png");

                    try
                    {
                        await ProcessSinglePassAsync(processedPath, passOutputPath, passSettings);
                        processedPath = passOutputPath;

                        _logger.LogDebug("Completed processing pass {Pass} for {ImagePath}", pass, fileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Processing pass {Pass} failed for {ImagePath}, continuing with previous result",
                            pass, fileName);
                        break;
                    }
                }

                // Copy final result to output path
                if (processedPath != imagePath && File.Exists(processedPath))
                {
                    File.Copy(processedPath, outputPath, true);
                    return outputPath;
                }

                return imagePath; // Return original if processing failed
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image {ImagePath}", imagePath);
                return imagePath; // Fallback to original
            }
        }

        private async Task ProcessSinglePassAsync(string inputPath, string outputPath, ImageProcessingSettings settings)
        {
            await Task.Run(() =>
            {
                EnhancedImageProcessor.ProcessImage(inputPath, outputPath, settings, _logger);
            });
        }

        private ImageProcessingSettings GetSettingsForPass(ProcessingPass pass, ProcessingParameters baseSettings)
        {
            return pass switch
            {
                ProcessingPass.Default => new ImageProcessingSettings
                {
                    GaussianSigma = baseSettings.GaussianSigma,
                    BinarizationWindow = baseSettings.BinarizationWindow,
                    BinarizationK = baseSettings.BinarizationK,
                    PreserveGrays = baseSettings.PreserveGrays,
                    BorderSize = baseSettings.BorderSize
                },
                ProcessingPass.Fallback => new ImageProcessingSettings
                {
                    GaussianSigma = baseSettings.GaussianSigma * 1.5,
                    BinarizationWindow = baseSettings.BinarizationWindow + 5,
                    BinarizationK = baseSettings.BinarizationK * 1.2,
                    PreserveGrays = true,
                    BorderSize = baseSettings.BorderSize + 10
                },
                ProcessingPass.Tablet => new ImageProcessingSettings
                {
                    GaussianSigma = baseSettings.GaussianSigma * 0.7,
                    BinarizationWindow = baseSettings.BinarizationWindow - 3,
                    BinarizationK = baseSettings.BinarizationK * 0.8,
                    PreserveGrays = baseSettings.PreserveGrays,
                    BorderSize = baseSettings.BorderSize + 5
                },
                _ => new ImageProcessingSettings()
            };
        }
    }
}