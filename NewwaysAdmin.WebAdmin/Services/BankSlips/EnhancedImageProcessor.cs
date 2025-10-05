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

        // ✅ NEW: Colored background removal settings
        public bool RemoveColoredBackground { get; set; } = false;
        public int ColoredBgBlackPoint { get; set; } = 127;  // X value for black (0-255)
        public int ColoredBgWhitePoint { get; set; } = 150;  // X value for white (0-255)

        public static ImageProcessingSettings Default => new()
        {
            GaussianSigma = 0.5,
            BinarizationWindow = 15,
            BinarizationK = 0.2,
            PreserveGrays = true,
            BorderSize = 20,
            RemoveColoredBackground = false
        };

        public static ImageProcessingSettings Fallback => new()
        {
            GaussianSigma = 0.8,
            BinarizationWindow = 30,
            BinarizationK = 0.15,
            PreserveGrays = false,
            BorderSize = 30,
            RemoveColoredBackground = false
        };

        public static ImageProcessingSettings Tablet => new()
        {
            GaussianSigma = 0.7,
            BinarizationWindow = 20,
            BinarizationK = 0.3,
            PreserveGrays = false,
            BorderSize = 30,
            RemoveColoredBackground = false
        };

        // ✅ NEW: Preset for colored backgrounds (like K+ app)
        public static ImageProcessingSettings ColoredBackground => new()
        {
            GaussianSigma = 0.5,
            BinarizationWindow = 15,
            BinarizationK = 0.2,
            PreserveGrays = true,
            BorderSize = 20,
            RemoveColoredBackground = true,
            ColoredBgBlackPoint = 127,
            ColoredBgWhitePoint = 150
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
                logger?.LogDebug("Processing image {InputPath} with settings: Sigma={Sigma}, Window={Window}, K={K}, RemoveColoredBg={RemoveBg}",
                    Path.GetFileName(inputPath), settings.GaussianSigma, settings.BinarizationWindow, settings.BinarizationK, settings.RemoveColoredBackground);

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

                    // ✅ NEW: Apply colored background removal FIRST if enabled
                    if (settings.RemoveColoredBackground)
                    {
                        RemoveColoredBackground(result, settings, logger);
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

        // ✅ NEW: Remove colored background (mimics GIMP: Desaturate + Curve adjustment)
        private static void RemoveColoredBackground(Bitmap image, ImageProcessingSettings settings, ILogger? logger)
        {
            try
            {
                using (var lock1 = new BitmapLock(image))
                {
                    unsafe
                    {
                        fixed (byte* ptr = lock1.Pixels)
                        {
                            // Step 1: Desaturate (convert to grayscale)
                            for (int i = 0; i < lock1.Pixels.Length; i += 3)
                            {
                                // Convert RGB to grayscale using standard luminosity method
                                byte gray = (byte)(0.299 * ptr[i + 2] + 0.587 * ptr[i + 1] + 0.114 * ptr[i]);
                                ptr[i] = ptr[i + 1] = ptr[i + 2] = gray;
                            }

                            // Step 2: Apply aggressive curve adjustment
                            // Black point: X=127 → Y=0
                            // White point: X=150 → Y=255
                            // Everything below 127 becomes pure black
                            // Everything above 150 becomes pure white
                            // Values between are stretched linearly

                            int blackPoint = settings.ColoredBgBlackPoint;
                            int whitePoint = settings.ColoredBgWhitePoint;
                            double range = whitePoint - blackPoint;

                            for (int i = 0; i < lock1.Pixels.Length; i += 3)
                            {
                                int value = ptr[i]; // Already grayscale, so R=G=B

                                byte newValue;
                                if (value <= blackPoint)
                                {
                                    newValue = 0; // Pure black
                                }
                                else if (value >= whitePoint)
                                {
                                    newValue = 255; // Pure white
                                }
                                else
                                {
                                    // Linear interpolation between black and white points
                                    double normalized = (value - blackPoint) / range;
                                    newValue = (byte)(normalized * 255);
                                }

                                ptr[i] = ptr[i + 1] = ptr[i + 2] = newValue;
                            }
                        }
                    }
                    lock1.Save();
                }
                logger?.LogDebug("Colored background removal completed (BlackPoint={BlackPoint}, WhitePoint={WhitePoint})",
                    settings.ColoredBgBlackPoint, settings.ColoredBgWhitePoint);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error in colored background removal, continuing without");
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
                            int windowSize = settings.BinarizationWindow;
                            double k = settings.BinarizationK;

                            for (int y = 0; y < lock1.Height; y++)
                            {
                                for (int x = 0; x < lock1.Width; x++)
                                {
                                    double localMean = 0;
                                    double localStdDev = 0;
                                    int count = 0;

                                    for (int wy = -windowSize / 2; wy <= windowSize / 2; wy++)
                                    {
                                        for (int wx = -windowSize / 2; wx <= windowSize / 2; wx++)
                                        {
                                            int px = x + wx;
                                            int py = y + wy;

                                            if (px >= 0 && px < lock1.Width && py >= 0 && py < lock1.Height)
                                            {
                                                int offset = py * lock1.Stride + px * 3;
                                                double gray = (ptr[offset] + ptr[offset + 1] + ptr[offset + 2]) / 3.0;
                                                localMean += gray;
                                                count++;
                                            }
                                        }
                                    }

                                    localMean /= count;

                                    for (int wy = -windowSize / 2; wy <= windowSize / 2; wy++)
                                    {
                                        for (int wx = -windowSize / 2; wx <= windowSize / 2; wx++)
                                        {
                                            int px = x + wx;
                                            int py = y + wy;

                                            if (px >= 0 && px < lock1.Width && py >= 0 && py < lock1.Height)
                                            {
                                                int offset = py * lock1.Stride + px * 3;
                                                double gray = (ptr[offset] + ptr[offset + 1] + ptr[offset + 2]) / 3.0;
                                                localStdDev += (gray - localMean) * (gray - localMean);
                                            }
                                        }
                                    }

                                    localStdDev = Math.Sqrt(localStdDev / count);

                                    int currentOffset = y * lock1.Stride + x * 3;
                                    double pixelValue = (ptr[currentOffset] + ptr[currentOffset + 1] + ptr[currentOffset + 2]) / 3.0;

                                    double threshold = localMean * (1 + k * ((localStdDev / 128.0) - 1));

                                    byte binaryValue = pixelValue > threshold ? (byte)255 : (byte)0;
                                    ptr[currentOffset] = ptr[currentOffset + 1] = ptr[currentOffset + 2] = binaryValue;
                                }
                            }
                        }
                    }
                    lock1.Save();
                }
                logger?.LogDebug("Adaptive binarization completed");
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error in adaptive binarization, continuing without");
            }
        }

        private static unsafe void ApplyGaussianBlur(byte* pixels, int width, int height, int stride, double sigma)
        {
            int kernelSize = (int)(sigma * 3) * 2 + 1;
            double[] kernel = GenerateGaussianKernel(kernelSize, sigma);
            byte[] temp = new byte[height * stride];

            // Horizontal pass
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double sum = 0;
                        for (int k = 0; k < kernelSize; k++)
                        {
                            int px = x + k - kernelSize / 2;
                            if (px >= 0 && px < width)
                            {
                                int offset = y * stride + px * 3 + c;
                                sum += pixels[offset] * kernel[k];
                            }
                        }
                        temp[y * stride + x * 3 + c] = (byte)Math.Max(0, Math.Min(255, sum));
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
                        for (int k = 0; k < kernelSize; k++)
                        {
                            int py = y + k - kernelSize / 2;
                            if (py >= 0 && py < height)
                            {
                                int offset = py * stride + x * 3 + c;
                                sum += temp[offset] * kernel[k];
                            }
                        }
                        pixels[y * stride + x * 3 + c] = (byte)Math.Max(0, Math.Min(255, sum));
                    }
                }
            }
        }

        private static unsafe void ApplyLocalContrastEnhancement(byte* pixels, int width, int height, int stride)
        {
            int windowSize = 5;
            double enhancementFactor = 1.3;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double localMean = 0;
                        int count = 0;

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

            for (int i = 0; i < size; i++)
            {
                kernel[i] /= sum;
            }

            return kernel;
        }

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

        public static void CleanupTempFiles(string directory, ILogger? logger = null)
        {
            try
            {
                if (!Directory.Exists(directory)) return;

                var tempFiles = Directory.GetFiles(directory, "*.png")
                    .Concat(Directory.GetFiles(directory, "processed_*.*"));

                foreach (var file in tempFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }

                logger?.LogDebug("Cleaned up temporary processing files in {Directory}", directory);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error cleaning up temp files");
            }
        }
    }
}