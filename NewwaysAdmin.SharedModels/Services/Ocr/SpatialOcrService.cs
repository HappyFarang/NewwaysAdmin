// NewwaysAdmin.SharedModels/Services/Ocr/SpatialOcrService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing; // NEW: Add this for Image class
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.SharedModels.BankSlips;
using NewwaysAdmin.SharedModels.Models.Ocr.Core;

namespace NewwaysAdmin.SharedModels.Models.Ocr.Core
{
    /// <summary>
    /// Spatial OCR service that extracts text with bounding box coordinates
    /// Uses Google Cloud Vision API to get word positions for advanced text processing
    /// </summary>
    public class SpatialOcrService : ISpatialOcrService
    {
        private readonly ILogger<SpatialOcrService> _logger;
        private ImageAnnotatorClient? _visionClient;
        private readonly string _defaultCredentialsPath = @"C:\Keys\purrfectocr-db2d9d796b58.json";

        public SpatialOcrService(ILogger<SpatialOcrService> logger)
        {
            _logger = logger;
        }

        public async Task<OcrExtractionResult> ExtractSpatialTextAsync(string imagePath, SlipCollection? collection = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new OcrExtractionResult
            {
                SourceImagePath = imagePath,
                ProcessedAt = DateTime.UtcNow
            };

            try
            {
                _logger.LogDebug("Starting spatial OCR extraction for {ImagePath}", Path.GetFileName(imagePath));

                // Setup Google Vision client
                if (!await SetupVisionClientAsync(collection, result))
                {
                    return result; // Error already set in result
                }

                // Validate image file
                if (!ValidateImageFile(imagePath, result))
                {
                    return result; // Error already set in result
                }

                // Extract spatial text data
                var document = await ExtractSpatialDocumentAsync(imagePath, collection, result);
                if (document != null)
                {
                    result.SetSuccess(document);
                    _logger.LogInformation("Successfully extracted {WordCount} words from {ImagePath}",
                        document.WordCount, Path.GetFileName(imagePath));
                }

                return result;
            }
            catch (Exception ex)
            {
                result.SetError($"Unexpected error during spatial OCR extraction: {ex.Message}");
                _logger.LogError(ex, "Error in spatial OCR extraction for {ImagePath}", imagePath);
                return result;
            }
            finally
            {
                stopwatch.Stop();
                result.ProcessingTime = stopwatch.Elapsed;
                _logger.LogDebug("Spatial OCR extraction completed in {ElapsedMs}ms",
                    stopwatch.ElapsedMilliseconds);
            }
        }

        public async Task<OcrExtractionResult> ExtractSpatialTextBasicAsync(string imagePath)
        {
            return await ExtractSpatialTextAsync(imagePath, null);
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await SetupVisionClientAsync(null, new OcrExtractionResult());

                if (_visionClient == null)
                    return false;

                // Try a simple operation to test connection
                var testImage = Google.Cloud.Vision.V1.Image.FromUri("gs://cloud-samples-data/vision/text/sign.jpg");
                var response = await _visionClient.DetectTextAsync(testImage);

                return response != null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Google Vision API connection test failed: {Error}", ex.Message);
                return false;
            }
        }

        public async Task<SpatialOcrServiceInfo> GetServiceInfoAsync()
        {
            var info = new SpatialOcrServiceInfo
            {
                CredentialsPath = _defaultCredentialsPath,
                CredentialsFileExists = File.Exists(_defaultCredentialsPath),
                LastTested = DateTime.UtcNow
            };

            try
            {
                info.IsConfigured = info.CredentialsFileExists;

                if (info.IsConfigured)
                {
                    info.CanConnectToGoogleVision = await TestConnectionAsync();
                    info.GoogleVisionApiVersion = "3.7.0"; // Current package version
                }
            }
            catch (Exception ex)
            {
                info.LastError = ex.Message;
                _logger.LogError(ex, "Error getting service info");
            }

            return info;
        }

        private async Task<bool> SetupVisionClientAsync(SlipCollection? collection, OcrExtractionResult result)
        {
            try
            {
                string credentialsPath = collection?.CredentialsPath ?? _defaultCredentialsPath;

                if (File.Exists(credentialsPath))
                {
                    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
                    _visionClient = ImageAnnotatorClient.Create();
                    _logger.LogDebug("Vision client configured with credentials: {CredentialsPath}", credentialsPath);

                    result.Metadata.CollectionName = collection?.Name ?? "Default";
                    result.Metadata.GoogleVisionApiVersion = "3.7.0";

                    return true;
                }
                else
                {
                    result.SetError($"Google Vision credentials file not found: {credentialsPath}");
                    _logger.LogError("Credentials file not found: {CredentialsPath}", credentialsPath);
                    return false;
                }
            }
            catch (Exception ex)
            {
                result.SetError($"Failed to setup Google Vision client: {ex.Message}");
                _logger.LogError(ex, "Error setting up Vision client");
                return false;
            }
        }

        private bool ValidateImageFile(string imagePath, OcrExtractionResult result)
        {
            if (string.IsNullOrEmpty(imagePath))
            {
                result.SetError("Image path is null or empty");
                return false;
            }

            if (!File.Exists(imagePath))
            {
                result.SetError($"Image file not found: {imagePath}");
                return false;
            }

            var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff" };
            var extension = Path.GetExtension(imagePath).ToLowerInvariant();

            if (!supportedExtensions.Contains(extension))
            {
                result.SetError($"Unsupported image format: {extension}");
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(imagePath);
                if (fileInfo.Length == 0)
                {
                    result.SetError("Image file is empty");
                    return false;
                }

                if (fileInfo.Length > 50 * 1024 * 1024) // 50MB limit
                {
                    result.AddWarning($"Large image file: {fileInfo.Length / 1024 / 1024}MB");
                }
            }
            catch (Exception ex)
            {
                result.SetError($"Error validating image file: {ex.Message}");
                return false;
            }

            return true;
        }

        private async Task<SpatialDocument?> ExtractSpatialDocumentAsync(string imagePath, SlipCollection? collection, OcrExtractionResult result)
        {
            try
            {
                _logger.LogDebug("Loading image from {ImagePath}", imagePath);

                var image = Google.Cloud.Vision.V1.Image.FromFile(imagePath);
                result.ProcessedImagePath = imagePath; // For now, same as source (no preprocessing yet)

                _logger.LogDebug("Calling Google Vision API for text detection");
                var response = await _visionClient!.DetectTextAsync(image);

                if (!response.Any())
                {
                    result.AddWarning("Google Vision API returned no text annotations");
                    _logger.LogWarning("No text detected for {ImagePath}", Path.GetFileName(imagePath));
                    return null;
                }

                _logger.LogDebug("Processing {AnnotationCount} text annotations", response.Count);

                // Create spatial document from annotations
                var document = await ProcessAnnotationsToSpatialDocument(response, imagePath, result);

                return document;
            }
            catch (Exception ex)
            {
                result.SetError($"Error during Google Vision API call: {ex.Message}");
                _logger.LogError(ex, "Vision API error for {ImagePath}", imagePath);
                return null;
            }
        }

        private async Task<SpatialDocument> ProcessAnnotationsToSpatialDocument(
            IReadOnlyList<EntityAnnotation> annotations,
            string imagePath,
            OcrExtractionResult result)
        {
            var document = new SpatialDocument
            {
                SourceImagePath = imagePath,
                CreatedAt = DateTime.UtcNow
            };

            result.Metadata.RawAnnotationsCount = annotations.Count;

            // Get image dimensions if possible
            var imageInfo = await GetImageDimensions(imagePath);
            document.DocumentWidth = imageInfo.Width;
            document.DocumentHeight = imageInfo.Height;

            result.Metadata.OriginalImageWidth = imageInfo.Width;
            result.Metadata.OriginalImageHeight = imageInfo.Height;
            result.Metadata.ProcessedImageWidth = imageInfo.Width;
            result.Metadata.ProcessedImageHeight = imageInfo.Height;

            var words = new List<WordBoundingBox>();
            int wordIndex = 0;

            foreach (var annotation in annotations)
            {
                // Skip the first annotation if it's the full document text (common in Google Vision)
                if (wordIndex == 0 && IsFullDocumentAnnotation(annotation, annotations))
                {
                    _logger.LogDebug("Skipping full document annotation");
                    wordIndex++;
                    continue;
                }

                var word = CreateWordBoundingBox(annotation, wordIndex, document.DocumentWidth, document.DocumentHeight);
                if (word != null)
                {
                    words.Add(word);
                    _logger.LogTrace("Created word: {Word}", word.ToString());
                }

                wordIndex++;
            }

            document.Words = words;
            result.Metadata.FilteredWordsCount = words.Count;

            _logger.LogDebug("Created spatial document with {WordCount} words from {AnnotationCount} annotations",
                words.Count, annotations.Count);

            // Add some basic metadata
            document.AddMetadata("ExtractionMethod", "GoogleVisionAPI");
            document.AddMetadata("ProcessedAt", DateTime.UtcNow.ToString("O"));
            document.AddMetadata("SourceImage", Path.GetFileName(imagePath));

            return document;
        }

        private bool IsFullDocumentAnnotation(EntityAnnotation annotation, IReadOnlyList<EntityAnnotation> allAnnotations)
        {
            // Google Vision often returns the full document text as the first annotation
            // We can detect this by checking if this annotation's text contains most other annotations
            if (allAnnotations.Count < 3) return false;

            var fullText = annotation.Description ?? "";
            var otherTexts = allAnnotations.Skip(1).Take(5).Select(a => a.Description ?? "").ToList();

            int containedCount = otherTexts.Count(text => !string.IsNullOrEmpty(text) && fullText.Contains(text));

            return containedCount >= Math.Min(3, otherTexts.Count); // If it contains most of the other texts
        }

        private WordBoundingBox? CreateWordBoundingBox(EntityAnnotation annotation, int index, int docWidth, int docHeight)
        {
            if (string.IsNullOrWhiteSpace(annotation.Description) || annotation.BoundingPoly?.Vertices == null)
            {
                _logger.LogTrace("Skipping annotation {Index} - no text or bounding box", index);
                return null;
            }

            var vertices = annotation.BoundingPoly.Vertices;
            if (vertices.Count < 4)
            {
                _logger.LogTrace("Skipping annotation {Index} - insufficient vertices", index);
                return null;
            }

            // Calculate bounding rectangle from vertices
            var minX = vertices.Min(v => v.X);
            var maxX = vertices.Max(v => v.X);
            var minY = vertices.Min(v => v.Y);
            var maxY = vertices.Max(v => v.Y);

            // Calculate normalized coordinates (0.0 to 1.0)
            float normX1 = docWidth > 0 ? (float)minX / docWidth : 0.0f;
            float normY1 = docHeight > 0 ? (float)minY / docHeight : 0.0f;
            float normX2 = docWidth > 0 ? (float)maxX / docWidth : 0.0f;
            float normY2 = docHeight > 0 ? (float)maxY / docHeight : 0.0f;

            var word = new WordBoundingBox
            {
                Text = annotation.Description.Trim(),
                Confidence = annotation.Confidence * 100.0f, // Convert to percentage (0-100)
                RawX1 = minX,
                RawY1 = minY,
                RawX2 = maxX,
                RawY2 = maxY,
                NormX1 = normX1,
                NormY1 = normY1,
                NormX2 = normX2,
                NormY2 = normY2,
                OriginalIndex = index
            };

            _logger.LogTrace("Word: '{Text}' at ({X1},{Y1})-({X2},{Y2}), confidence: {Confidence}",
                word.Text, minX, minY, maxX, maxY, annotation.Confidence);

            return word;
        }

        private async Task<(int Width, int Height)> GetImageDimensions(string imagePath)
        {
            try
            {
                // Use System.Drawing.Image explicitly to avoid conflict with Google.Cloud.Vision.V1.Image
                using var image = System.Drawing.Image.FromFile(imagePath);
                _logger.LogDebug("Image dimensions: {Width}x{Height}", image.Width, image.Height);
                return (image.Width, image.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not determine image dimensions for {ImagePath}: {Error}",
                    Path.GetFileName(imagePath), ex.Message);
                return (0, 0);
            }
        }
    }
}