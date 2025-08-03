// NewwaysAdmin.WebAdmin/Services/Testing/OcrTestingService.cs
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.WebAdmin.Services.BankSlips;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;

namespace NewwaysAdmin.WebAdmin.Services.Testing
{
    public class OcrTestingService : IOcrTestingService
    {
        private readonly ILogger<OcrTestingService> _logger;
        private readonly IBankSlipOcrService _bankSlipOcrService;
        private readonly BankSlipImageProcessor _imageProcessor;

        public OcrTestingService(
            ILogger<OcrTestingService> logger,
            IBankSlipOcrService bankSlipOcrService,
            BankSlipImageProcessor imageProcessor)
        {
            _logger = logger;
            _bankSlipOcrService = bankSlipOcrService;
            _imageProcessor = imageProcessor;
        }

        public async Task<OcrTestResult> ProcessImageWithExistingPipelineAsync(string imagePath, SlipCollection? collection = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new OcrTestResult
            {
                ImagePath = imagePath,
                FileSizeBytes = new FileInfo(imagePath).Length
            };

            try
            {
                // Get image dimensions
                try
                {
                    using var img = Image.FromFile(imagePath);
                    result.ImageDimensions = $"{img.Width}x{img.Height}";
                }
                catch
                {
                    result.ImageDimensions = "Unknown";
                }

                // Use provided collection or create a test one
                collection ??= await CreateTestCollectionAsync();
                result.CollectionUsed = collection.Name;
                result.UsedSettings = collection.ProcessingSettings;

                _logger.LogDebug("Processing image {FileName} using existing OCR pipeline with collection {CollectionName}",
                    Path.GetFileName(imagePath), collection.Name);

                // Extract text directly using the same pipeline components but skip validation
                result = await ExtractTextUsingExistingPipeline(imagePath, collection, result);
                result.Success = !string.IsNullOrEmpty(result.ExtractedText);

                if (result.Success)
                {
                    _logger.LogDebug("Successfully extracted {CharCount} characters using existing pipeline",
                        result.ExtractedText.Length);
                }
                else
                {
                    result.ErrorMessage = "No text could be extracted from the image";
                    _logger.LogWarning("No text extracted from {FileName}",
                        Path.GetFileName(imagePath));
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error in OCR processing pipeline: {ex.Message}";
                _logger.LogError(ex, "Error processing image {FileName} through existing pipeline",
                    Path.GetFileName(imagePath));
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
            }

            return result;
        }

        private async Task<OcrTestResult> ExtractTextUsingExistingPipeline(string imagePath, SlipCollection collection, OcrTestResult result)
        {
            try
            {
                _logger.LogDebug("Starting text extraction using existing image processing pipeline");

                // Step 1: Use the injected image processor (same as bank slip system)
                var processedImagePath = await _imageProcessor.ProcessImageAsync(imagePath, collection.ProcessingSettings);
                result.ProcessedImagePath = processedImagePath ?? imagePath;

                _logger.LogDebug("Image processing completed. Using image: {ImagePath}",
                    Path.GetFileName(result.ProcessedImagePath));

                // Step 2: Extract text using Google Vision API (same setup as bank slip system)
                await SetupVisionClientAndExtractText(result.ProcessedImagePath, collection, result);

                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Error in text extraction pipeline: {ex.Message}";
                _logger.LogError(ex, "Error extracting text using existing pipeline");
                return result;
            }
        }

        private async Task SetupVisionClientAndExtractText(string imagePath, SlipCollection collection, OcrTestResult result)
        {
            try
            {
                _logger.LogDebug("Setting up Google Vision client with collection credentials");

                // Set up credentials the same way as BankSlipOcrService
                if (File.Exists(collection.CredentialsPath))
                {
                    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", collection.CredentialsPath);
                    _logger.LogDebug("Using collection credentials: {CredentialsPath}", collection.CredentialsPath);
                }
                else
                {
                    var defaultPath = @"C:\Keys\purrfectocr-db2d9d796b58.json";
                    if (File.Exists(defaultPath))
                    {
                        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", defaultPath);
                        _logger.LogDebug("Using default credentials: {CredentialsPath}", defaultPath);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No valid Google Vision credentials found. Checked: {collection.CredentialsPath}, {defaultPath}");
                    }
                }

                // Use Google Vision API the same way as the existing service
                var client = Google.Cloud.Vision.V1.ImageAnnotatorClient.Create();
                var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);

                _logger.LogDebug("Calling Google Vision API for text detection");
                var response = await client.DetectTextAsync(image);

                if (response.Any())
                {
                    // Extract all text annotations (same as bank slip service)
                    result.ExtractedText = string.Join("\n", response.Select(r => r.Description));
                    _logger.LogInformation("Successfully extracted {CharCount} characters from {FileName}",
                        result.ExtractedText.Length, Path.GetFileName(imagePath));

                    // Log first 200 characters for debugging
                    var preview = result.ExtractedText.Length > 200
                        ? result.ExtractedText.Substring(0, 200) + "..."
                        : result.ExtractedText;
                    _logger.LogDebug("Text preview: {TextPreview}", preview);
                }
                else
                {
                    result.ErrorMessage = "Google Vision API returned no text annotations";
                    _logger.LogWarning("No text detected by Google Vision API for {FileName}", Path.GetFileName(imagePath));
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Vision API error: {ex.Message}";
                _logger.LogError(ex, "Error using Google Vision API for text extraction from {FileName}", Path.GetFileName(imagePath));
            }
        }

        public async Task<List<RegexTestResult>> TestRegexPatterns(string text, List<string> patterns)
        {
            return await Task.Run(() =>
            {
                var results = new List<RegexTestResult>();

                foreach (var pattern in patterns.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    var testResult = new RegexTestResult { Pattern = pattern };

                    try
                    {
                        var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                        var matches = regex.Matches(text);

                        testResult.IsValid = true;
                        testResult.MatchCount = matches.Count;

                        foreach (Match match in matches)
                        {
                            var regexMatch = new RegexMatch
                            {
                                Value = match.Value,
                                StartIndex = match.Index,
                                Length = match.Length
                            };

                            // Extract numbered groups
                            for (int i = 1; i < match.Groups.Count; i++)
                            {
                                var group = match.Groups[i];
                                if (group.Success)
                                {
                                    regexMatch.Groups[$"Group{i}"] = group.Value;
                                }
                            }

                            // Extract named groups
                            foreach (string groupName in regex.GetGroupNames())
                            {
                                if (groupName != "0" && !int.TryParse(groupName, out _))
                                {
                                    var group = match.Groups[groupName];
                                    if (group.Success)
                                    {
                                        regexMatch.Groups[groupName] = group.Value;
                                    }
                                }
                            }

                            testResult.Matches.Add(regexMatch);
                        }
                    }
                    catch (Exception ex)
                    {
                        testResult.IsValid = false;
                        testResult.ErrorMessage = ex.Message;
                    }

                    results.Add(testResult);
                }

                return results;
            });
        }

        public async Task<List<SlipCollection>> GetAvailableCollectionsAsync()
        {
            try
            {
                return await _bankSlipOcrService.GetAllCollectionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available collections");
                return new List<SlipCollection>();
            }
        }

        public async Task<SlipCollection> CreateTestCollectionAsync(ProcessingParameters? customSettings = null)
        {
            return await Task.FromResult(new SlipCollection
            {
                Id = "ocr-test-collection",
                Name = "OCR Test Collection",
                Description = "Temporary collection for OCR testing",
                SourceDirectory = Path.GetTempPath(),
                OutputDirectory = Path.GetTempPath(),
                CredentialsPath = @"C:\Keys\purrfectocr-db2d9d796b58.json",
                CreatedBy = "ocr-analyzer",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                ProcessingSettings = customSettings ?? new ProcessingParameters
                {
                    GaussianSigma = 0.5,
                    BinarizationWindow = 15,
                    BinarizationK = 0.2,
                    PreserveGrays = true,
                    BorderSize = 20,
                    ProcessingPasses = new[] { ProcessingPass.Default, ProcessingPass.Fallback },
                    UseEnhancedDateValidation = true,
                    ExtractDualLanguageNames = false,
                    ValidateAccountFormat = true
                }
            });
        }
    }
}