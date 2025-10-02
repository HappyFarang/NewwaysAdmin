// NewwaysAdmin.WebAdmin/Services/Workers/WorkerSignalService.cs
// Purpose: Signal the desktop attendance app about remote sign-outs

using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.WebAdmin.Services.Workers
{
    public class WorkerSignalService
    {
        private readonly ILogger<WorkerSignalService> _logger;
        private readonly string _signalPath;

        public WorkerSignalService(ILogger<WorkerSignalService> logger, IConfiguration configuration)
        {
            _logger = logger;

            // Use the same base path as your shared data
            // Adjust this path to match your actual shared folder location
            var basePath = configuration["IOManager:BasePath"] ?? @"\\YOURSERVER\SharedData";
            _signalPath = Path.Combine(basePath, "Workers", ".signals");

            // Ensure signal directory exists
            Directory.CreateDirectory(_signalPath);
        }

        /// <summary>
        /// Send a signal to the desktop app that a worker was signed out remotely
        /// </summary>
        public async Task SignalWorkerSignOutAsync(int workerId, string workerName)
        {
            try
            {
                var signalFileName = $"signout_{workerId}_{DateTime.Now:yyyyMMddHHmmss}.signal";
                var signalFile = Path.Combine(_signalPath, signalFileName);

                var signalData = new
                {
                    Type = "WORKER_SIGNOUT",
                    WorkerId = workerId,
                    WorkerName = workerName,
                    Timestamp = DateTime.Now,
                    SignaledBy = "WebAdmin"
                };

                await File.WriteAllTextAsync(signalFile, System.Text.Json.JsonSerializer.Serialize(signalData));

                _logger.LogInformation("📡 Signal sent: Worker {WorkerId} signed out remotely", workerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send worker sign-out signal");
                // Don't throw - signal is nice-to-have, not critical
            }
        }

        /// <summary>
        /// Clean up old signal files (called periodically)
        /// </summary>
        public void CleanupOldSignals()
        {
            try
            {
                var files = Directory.GetFiles(_signalPath, "*.signal");
                var cutoff = DateTime.Now.AddHours(-1); // Keep signals for 1 hour

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoff)
                    {
                        File.Delete(file);
                        _logger.LogDebug("Cleaned up old signal file: {FileName}", fileInfo.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up signal files");
            }
        }
    }
}