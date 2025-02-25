using System.IO;

namespace NewwaysAdmin.OrderProcessor
{
    public class PdfFileWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly PdfProcessor _processor;
        private readonly OrderProcessorLogger _logger;
        private readonly SemaphoreSlim _processingLock = new(1, 1);
        private bool _disposedValue;

        public PdfFileWatcher(string watchFolder, PdfProcessor processor, OrderProcessorLogger logger)
        {
            ArgumentException.ThrowIfNullOrEmpty(watchFolder);

            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Ensure watch directory exists
            if (!Directory.Exists(watchFolder))
            {
                Directory.CreateDirectory(watchFolder);
            }

            _watcher = new FileSystemWatcher(watchFolder)
            {
                Filter = "*.pdf",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
            };
        }

        public void Start()
        {
            _logger.Log("PDF File Watcher starting...");

            _watcher.Created += async (sender, e) => await OnNewPdfAsync(e);
            _watcher.EnableRaisingEvents = true;

            _logger.Log($"Watching for PDFs in: {_watcher.Path}");
        }

        public void Stop()
        {
            _logger.Log("PDF File Watcher stopping...");
            _watcher.EnableRaisingEvents = false;
        }

        private async Task OnNewPdfAsync(FileSystemEventArgs e)
        {
            try
            {
                await _processingLock.WaitAsync();

                _logger.Log($"New PDF detected: {Path.GetFileName(e.FullPath)}");

                // Wait for file to be ready
                if (await WaitForFileAccessAsync(e.FullPath))
                {
                    await _processor.ProcessPdfAsync(e.FullPath);
                }
                else
                {
                    _logger.Log($"Timeout waiting for file access: {Path.GetFileName(e.FullPath)}");
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Error processing new PDF: {ex.Message}");
            }
            finally
            {
                _processingLock.Release();
            }
        }

        private async Task<bool> WaitForFileAccessAsync(string filePath, int maxAttempts = 10, int delayMs = 500)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }
                catch (IOException)
                {
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Error checking file access: {ex.Message}");
                    return false;
                }
            }
            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _watcher.Dispose();
                    _processingLock.Dispose();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}