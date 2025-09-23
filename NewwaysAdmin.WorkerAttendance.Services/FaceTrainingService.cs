// File: NewwaysAdmin.WorkerAttendance.Services/FaceTrainingService.cs
// Purpose: Dedicated service for face training workflow using face_training_capture.py

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace NewwaysAdmin.WorkerAttendance.Services
{
    public class FaceTrainingService
    {
        private Process? _trainingProcess;
        private bool _isRunning = false;
        private readonly string _pythonScript;
        private readonly ILogger<FaceTrainingService> _logger;

        // Events for UI communication
        public event Action<byte[]>? FrameReceived;
        public event Action<string>? StatusChanged;
        public event Action<string>? ErrorOccurred;
        public event Action<byte[]>? FaceEncodingReceived;

        public bool IsTrainingActive => _isRunning;

        public FaceTrainingService(string pythonScriptPath, ILogger<FaceTrainingService> logger)
        {
            _pythonScript = pythonScriptPath;
            _logger = logger;
        }

        /// <summary>
        /// Start the face training session
        /// </summary>
        public async Task<bool> StartTrainingSessionAsync()
        {
            try
            {
                if (_isRunning)
                {
                    _logger.LogInformation("Training session already active");
                    StatusChanged?.Invoke("Training session already active");
                    return true;
                }

                _logger.LogInformation("Starting face training session with script: {Script}", _pythonScript);
                StatusChanged?.Invoke("Starting face training session...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{_pythonScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _trainingProcess = Process.Start(startInfo);

                if (_trainingProcess == null)
                {
                    _logger.LogError("Failed to start training process");
                    ErrorOccurred?.Invoke("Failed to start training process");
                    return false;
                }

                _isRunning = true;

                // Start reading messages from Python in background
                _ = Task.Run(ReadTrainingMessages);

                _logger.LogInformation("Training session started successfully");
                StatusChanged?.Invoke("Training session started successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting training session");
                ErrorOccurred?.Invoke($"Error starting training: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Capture face encoding at current moment
        /// </summary>
        public async Task RequestFaceCaptureAsync()
        {
            try
            {
                if (!_isRunning || _trainingProcess?.StandardInput == null)
                {
                    _logger.LogWarning("Training session not active when capture requested");
                    ErrorOccurred?.Invoke("Training session not active");
                    return;
                }

                _logger.LogInformation("Requesting face capture");
                StatusChanged?.Invoke("Requesting face capture...");

                // Send CAPTURE command to Python
                await _trainingProcess.StandardInput.WriteLineAsync("CAPTURE");
                await _trainingProcess.StandardInput.FlushAsync();

                _logger.LogInformation("Face capture command sent to Python");
                StatusChanged?.Invoke("Face capture command sent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending capture command");
                ErrorOccurred?.Invoke($"Error sending capture command: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the training session
        /// </summary>
        public async Task StopTrainingSessionAsync()
        {
            try
            {
                if (!_isRunning)
                    return;

                _logger.LogInformation("Stopping training session");
                StatusChanged?.Invoke("Stopping training session...");

                if (_trainingProcess?.StandardInput != null)
                {
                    // Send STOP command to Python
                    await _trainingProcess.StandardInput.WriteLineAsync("STOP");
                    await _trainingProcess.StandardInput.FlushAsync();
                }

                // Give process time to cleanup gracefully
                await Task.Delay(1000);

                if (_trainingProcess?.HasExited == false)
                {
                    _trainingProcess.Kill();
                }

                _trainingProcess?.Dispose();
                _trainingProcess = null;
                _isRunning = false;

                _logger.LogInformation("Training session stopped successfully");
                StatusChanged?.Invoke("Training session stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping training session");
                ErrorOccurred?.Invoke($"Error stopping training: {ex.Message}");
            }
        }

        /// <summary>
        /// Read messages from Python training script
        /// </summary>
        private async Task ReadTrainingMessages()
        {
            if (_trainingProcess?.StandardOutput == null) return;

            try
            {
                _logger.LogInformation("Started reading training messages from Python");

                while (!_trainingProcess.HasExited && _isRunning)
                {
                    var line = await _trainingProcess.StandardOutput.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;

                    // Parse the message format from face_training_capture.py
                    ProcessTrainingMessage(line);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading training messages");
                ErrorOccurred?.Invoke($"Error reading training messages: {ex.Message}");
            }
        }

        /// <summary>
        /// Process messages from Python script using the defined protocol
        /// </summary>
        private void ProcessTrainingMessage(string message)
        {
            try
            {
                // face_training_capture.py uses format: "TYPE:content"
                var parts = message.Split(':', 2);
                if (parts.Length != 2)
                {
                    _logger.LogDebug("Received malformed message: {Message}", message);
                    return;
                }

                string messageType = parts[0];
                string content = parts[1];

                _logger.LogDebug("Processing training message: {Type} - {Content}", messageType, content.Substring(0, Math.Min(50, content.Length)));

                switch (messageType)
                {
                    case "STATUS":
                        _logger.LogInformation("Python status: {Status}", content);
                        StatusChanged?.Invoke(content);
                        break;

                    case "ERROR":
                        _logger.LogWarning("Python error: {Error}", content);
                        ErrorOccurred?.Invoke(content);
                        break;

                    case "FRAME":
                        // Convert base64 frame to bytes for UI
                        try
                        {
                            byte[] frameBytes = Convert.FromBase64String(content);
                            FrameReceived?.Invoke(frameBytes);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing frame data");
                            ErrorOccurred?.Invoke($"Error processing video frame: {ex.Message}");
                        }
                        break;

                    case "ENCODING":
                        // Convert base64 encoding to bytes for storage
                        try
                        {
                            byte[] encodingBytes = Convert.FromBase64String(content);
                            _logger.LogInformation("Received face encoding: {Size} bytes", encodingBytes.Length);
                            FaceEncodingReceived?.Invoke(encodingBytes);
                            StatusChanged?.Invoke("Face encoding captured successfully!");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing face encoding");
                            ErrorOccurred?.Invoke($"Error processing face encoding: {ex.Message}");
                        }
                        break;

                    default:
                        _logger.LogDebug("Unknown message type: {Type}", messageType);
                        StatusChanged?.Invoke($"Unknown message: {message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing training message: {Message}", message);
                ErrorOccurred?.Invoke($"Error processing message: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            if (_isRunning)
            {
                _logger.LogInformation("Disposing FaceTrainingService");
                _ = StopTrainingSessionAsync();
            }
        }
    }
}