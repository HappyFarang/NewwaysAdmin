// NewwaysAdmin.WebAdmin/Services/BankSlips/EnhancedImageProcessor.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.BankSlips
{
    public class ImageProcessingSettings
    {
        public double GaussianSigma { get; set; } = 0.5;
        public int BinarizationWindow { get; set; } = 15;
        public double BinarizationK { get; set; } = 0.2;
        public bool PreserveGrays { get; set; } = true;
        public int BorderSize { get; set; } = 20;

        public static ImageProcessingSettings Default => new()
        {
            GaussianSigma = 0.5,
            BinarizationWindow = 15,
            BinarizationK = 0.2,
            PreserveGrays = true,
            BorderSize = 20
        };

        public static ImageProcessingSettings Fallback => new()
        {
            GaussianSigma = 0.8,
            BinarizationWindow = 30,
            BinarizationK = 0.15,
            PreserveGrays = false,
            BorderSize = 30
        };

        public static ImageProcessingSettings Tablet => new()
        {
            GaussianSigma = 0.7,
            BinarizationWindow = 20,
            BinarizationK = 0.3,
            PreserveGrays = false,
            BorderSize = 30
        };
    }

    public static class EnhancedImageProcessor
    {
        private class BitmapLock : IDisposable
        {
            public byte[] Pixels { get; private set; }
            public int Stride { get; private set; }
            public int Width { get; private set; }
            public int Height { get; private set; }
            private GCHandle Handle { get; set; }
            private BitmapData BitmapData { get; set; }
            private Bitmap Bitmap { get; set; }

            public BitmapLock(Bitmap bitmap)
            {
                Bitmap = bitmap;
                Width = bitmap.Width;
                Height = bitmap.Height;

                var bounds = new Rectangle(0, 0, Width, Height);
                BitmapData = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
                Stride = BitmapData.Stride;

                var length = Stride * Height;
                Pixels = new byte[length];
                Marshal.Copy(BitmapData.Scan0, Pixels, 0, length);
                Handle = GCHandle.Alloc(Pixels, GCHandleType.Pinned);
            }

            public void Save()
            {
                Marshal.Copy(Pixels, 0, BitmapData.Scan0, Pixels.Length);
            }

            public void Dispose()
            {
                if (Handle.IsAllocated)
                    Handle.Free();
                if (BitmapData != null && Bitmap != null)
                    Bitmap.UnlockBits(BitmapData);
            }
        }

        public static void ProcessImage(string inputPath, string outputPath, ImageProcessingSettings? settings = null, ILogger? logger = null)
        {
            settings ??= ImageProcessingSettings.Default;

            try
            {
                logger?.LogDebug("Processing image {InputPath} with settings: Sigma={Sigma}, Window={Window}, K={K}",
                    Path.GetFileName(inputPath), settings.GaussianSigma, settings.BinarizationWindow, settings.BinarizationK);

                using (var original = new Bitmap(inputPath))
                using (var result = new Bitmap(original.Width + settings.BorderSize * 2,
                                             original.Height + settings.BorderSize * 2))
                {
                    using (var g = Graphics.FromImage(result))
                    {
                        // Fill with white background including border
                        g.Clear(Color.White);
                        g.DrawImage(original, settings.BorderSize, settings.BorderSize);
                    }

                    ApplyPreprocessing(result, settings, logger);
                    ApplyAdaptiveBinarization(result, settings, logger);

                    if (settings.PreserveGrays)
                    {
                        PreserveGrayValues(result, logger);
                    }

                    // Always save as PNG to avoid JPEG compression artifacts
                    result.Save(outputPath, ImageFormat.Png);
                    logger?.LogDebug("Enhanced image saved to {OutputPath}", Path.GetFileName(outputPath));
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error processing image {InputPath}, falling back to copy", inputPath);

                // Fallback: just copy the original file but convert to PNG
                try
                {
                    using (var original = new Bitmap(inputPath))
                    {
                        original.Save(outputPath, ImageFormat.Png);
                    }
                }
                catch
                {
                    // If even that fails, just copy the file
                    File.Copy(inputPath, outputPath, true);
                }
            }
        }

        private static void ApplyPreprocessing(Bitmap image, ImageProcessingSettings settings, ILogger? logger)
        {
            try
            {
                using (var lock1 = new BitmapLock(image))
                {
                    unsafe
                    {
                        fixed (byte* ptr = lock1.Pixels)
                        {
                            ApplyGaussianBlur(ptr, lock1.Width, lock1.Height, lock1.Stride, settings.GaussianSigma);
                            ApplyLocalContrastEnhancement(ptr, lock1.Width, lock1.Height, lock1.Stride);
                        }
                    }
                    lock1.Save();
                }
                logger?.LogDebug("Preprocessing completed successfully");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error in preprocessing, continuing without");
            }
        }

        private static void ApplyAdaptiveBinarization(Bitmap image, ImageProcessingSettings settings, ILogger? logger)
        {
            try
            {
                using (var lock1 = new BitmapLock(image))
                {
                    unsafe
                    {
                        fixed (byte* ptr = lock1.Pixels)
                        {
                            int width = lock1.Width;
                            int height = lock1.Height;
                            int stride = lock1.Stride;
                            int windowSize = settings.BinarizationWindow;
                            double k = settings.BinarizationK;

                            // Create integral image for efficient window operations
                            long[,] integral = new long[width + 1, height + 1];

                            // Build integral image
                            for (int y = 1; y <= height; y++)
                            {
                                for (int x = 1; x <= width; x++)
                                {
                                    int pos = ((y - 1) * stride) + ((x - 1) * 3);
                                    long pixelSum = (ptr[pos] + ptr[pos + 1] + ptr[pos + 2]) / 3;
                                    integral[x, y] = pixelSum + integral[x - 1, y] + integral[x, y - 1] - integral[x - 1, y - 1];
                                }
                            }

                            // Apply adaptive thresholding
                            int radius = windowSize / 2;
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    int x1 = Math.Max(0, x - radius);
                                    int y1 = Math.Max(0, y - radius);
                                    int x2 = Math.Min(width - 1, x + radius);
                                    int y2 = Math.Min(height - 1, y + radius);

                                    int count = (x2 - x1 + 1) * (y2 - y1 + 1);
                                    long sum = integral[x2 + 1, y2 + 1] - integral[x2 + 1, y1] - integral[x1, y2 + 1] + integral[x1, y1];

                                    long mean = sum / count;
                                    int pos = y * stride + x * 3;
                                    byte pixelValue = (byte)((ptr[pos] + ptr[pos + 1] + ptr[pos + 2]) / 3);

                                    byte threshold = (byte)(mean * (1.0 - k));
                                    byte newValue = (byte)(pixelValue < threshold ? 0 : 255);

                                    ptr[pos] = ptr[pos + 1] = ptr[pos + 2] = newValue;
                                }
                            }
                        }
                    }
                    lock1.Save();
                }
                logger?.LogDebug("Adaptive binarization completed successfully");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error in adaptive binarization, continuing without");
            }
        }

        private static void ApplyGaussianBlur(byte* pixels, int width, int height, int stride, double sigma)
        {
            int kernelSize = (int)(sigma * 6) | 1; // Always odd number
            double[] kernel = GenerateGaussianKernel(kernelSize, sigma);

            byte[] tempBuffer = new byte[width * height * 3];

            // Horizontal pass
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double sum = 0;
                        double weightSum = 0;

                        for (int k = -kernelSize / 2; k <= kernelSize / 2; k++)
                        {
                            int px = x + k;
                            if (px >= 0 && px < width)
                            {
                                int offset = y * stride + px * 3 + c;
                                double weight = kernel[k + kernelSize / 2];
                                sum += pixels[offset] * weight;
                                weightSum += weight;
                            }
                        }

                        tempBuffer[y * width * 3 + x * 3 + c] = (byte)(sum / weightSum);
                    }
                }
            }

            // Vertical pass
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double sum = 0;
                        double weightSum = 0;

                        for (int k = -kernelSize / 2; k <= kernelSize / 2; k++)
                        {
                            int py = y + k;
                            if (py >= 0 && py < height)
                            {
                                int offset = py * width * 3 + x * 3 + c;
                                double weight = kernel[k + kernelSize / 2];
                                sum += tempBuffer[offset] * weight;
                                weightSum += weight;
                            }
                        }

                        pixels[y * stride + x * 3 + c] = (byte)(sum / weightSum);
                    }
                }
            }
        }

        private static void ApplyLocalContrastEnhancement(byte* pixels, int width, int height, int stride)
        {
            const int windowSize = 7;
            const double enhancementFactor = 1.5;

            byte[] tempBuffer = new byte[width * height * 3];
            Marshal.Copy(new IntPtr(pixels), tempBuffer, 0, tempBuffer.Length);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double localMean = 0;
                        int count = 0;

                        // Calculate local statistics
                        for (int wy = -windowSize / 2; wy <= windowSize / 2; wy++)
                        {
                            for (int wx = -windowSize / 2; wx <= windowSize / 2; wx++)
                            {
                                int px = x + wx;
                                int py = y + wy;

                                if (px >= 0 && px < width && py >= 0 && py < height)
                                {
                                    int offset = py * stride + px * 3 + c;
                                    localMean += pixels[offset];
                                    count++;
                                }
                            }
                        }

                        localMean /= count;

                        // Enhance contrast based on local statistics
                        int currentOffset = y * stride + x * 3 + c;
                        double pixelValue = pixels[currentOffset];
                        double enhanced = localMean + (pixelValue - localMean) * enhancementFactor;

                        pixels[currentOffset] = (byte)Math.Max(0, Math.Min(255, enhanced));
                    }
                }
            }
        }

        private static void PreserveGrayValues(Bitmap image, ILogger? logger)
        {
            try
            {
                using (var lock1 = new BitmapLock(image))
                {
                    unsafe
                    {
                        fixed (byte* ptr = lock1.Pixels)
                        {
                            for (int i = 0; i < lock1.Pixels.Length; i += 3)
                            {
                                byte value = (byte)((ptr[i] + ptr[i + 1] + ptr[i + 2]) / 3);
                                if (value > 50 && value < 200) // Preserve mid-tones
                                {
                                    ptr[i] = ptr[i + 1] = ptr[i + 2] = value;
                                }
                            }
                        }
                    }
                    lock1.Save();
                }
                logger?.LogDebug("Gray value preservation completed");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error in gray value preservation, continuing without");
            }
        }

        private static double[] GenerateGaussianKernel(int size, double sigma)
        {
            double[] kernel = new double[size];
            double sum = 0;
            int radius = size / 2;

            for (int i = -radius; i <= radius; i++)
            {
                double value = Math.Exp(-(i * i) / (2 * sigma * sigma));
                kernel[i + radius] = value;
                sum += value;
            }

            // Normalize
            for (int i = 0; i < size; i++)
            {
                kernel[i] /= sum;
            }

            return kernel;
        }

        /// <summary>
        /// Get processing settings based on the processing pass type
        /// </summary>
        public static ImageProcessingSettings GetSettingsForPass(NewwaysAdmin.SharedModels.BankSlips.ProcessingPass pass)
        {
            return pass switch
            {
                NewwaysAdmin.SharedModels.BankSlips.ProcessingPass.Default => ImageProcessingSettings.Default,
                NewwaysAdmin.SharedModels.BankSlips.ProcessingPass.Fallback => ImageProcessingSettings.Fallback,
                NewwaysAdmin.SharedModels.BankSlips.ProcessingPass.Tablet => ImageProcessingSettings.Tablet,
                _ => ImageProcessingSettings.Default
            };
        }

        /// <summary>
        /// Clean up temporary processed files including PNG files
        /// </summary>
        public static void CleanupTempFiles(string directory, ILogger? logger = null)
        {
            try
            {
                if (!Directory.Exists(directory)) return;

                var tempFiles = Directory.GetFiles(directory, "*.*")
                    .Where(f => Path.GetFileName(f).StartsWith("temp_processed_") ||
                               Path.GetFileName(f).StartsWith("enhanced_processed_"))
                    .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                               f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                        logger?.LogDebug("Deleted temp file: {FileName}", Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug("Failed to delete temp file {FileName}: {Error}",
                            Path.GetFileName(file), ex.Message);
                    }
                }

                if (tempFiles.Any())
                {
                    logger?.LogInformation("Cleaned up {Count} temporary processed files", tempFiles.Count);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error during temp file cleanup");
            }
        }
    }
}