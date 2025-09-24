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

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",  // Changed back to "python"
                    Arguments = $"\"{_pythonScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _logger.LogInformation("Starting face training session with script: {Script}", _pythonScript);
                _logger.LogInformation("Using Python executable: {PythonPath}", startInfo.FileName);
                _logger.LogInformation("Full command: {Command} {Args}", startInfo.FileName, startInfo.Arguments);

                _trainingProcess = Process.Start(startInfo);

                if (_trainingProcess == null)
                {
                    _logger.LogError("Failed to start training process");
                    ErrorOccurred?.Invoke("Failed to start training process");
                    return false;
                }

                _isRunning = true;  // Set AFTER successful process start

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
                {
                    _logger.LogInformation("Training session not running - nothing to stop");
                    return;
                }

                _logger.LogInformation("Stopping training session");
                StatusChanged?.Invoke("Stopping training session...");

                // Check if process exists before trying to access it
                if (_trainingProcess != null)
                {
                    if (_trainingProcess.StandardInput != null && !_trainingProcess.HasExited)
                    {
                        try
                        {
                            // Send STOP command to Python
                            await _trainingProcess.StandardInput.WriteLineAsync("STOP");
                            await _trainingProcess.StandardInput.FlushAsync();
                            _logger.LogInformation("STOP command sent to Python");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to send STOP command to Python process");
                        }
                    }

                    // Wait a moment for graceful shutdown
                    try
                    {
                        if (!_trainingProcess.HasExited)
                        {
                            bool exited = _trainingProcess.WaitForExit(2000); // 2 second timeout
                            if (!exited)
                            {
                                _logger.LogWarning("Training process did not exit gracefully, will be terminated");
                                _trainingProcess.Kill();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during process shutdown");
                    }

                    _trainingProcess.Dispose();
                    _trainingProcess = null;
                }
                else
                {
                    _logger.LogWarning("Training process was already null when trying to stop");
                }

                _isRunning = false;
                _logger.LogInformation("Training session stopped");
                StatusChanged?.Invoke("Training session stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping training session");
                _isRunning = false; // Force reset the flag
                ErrorOccurred?.Invoke($"Error stopping training: {ex.Message}");
            }
        }

        /// <summary>
        /// Read messages from Python training script
        /// </summary>
        // Replace the ReadTrainingMessages method in FaceTrainingService.cs:

        private async Task ReadTrainingMessages()
        {
            if (_trainingProcess?.StandardOutput == null) return;

            try
            {
                _logger.LogInformation("Starting to read Python JSON messages...");

                // Store a local reference to avoid null reference issues
                var process = _trainingProcess;

                while (process != null && !process.HasExited && _isRunning)
                {
                    // Double-check process is still valid
                    if (process.StandardOutput == null)
                        break;

                    var line = await process.StandardOutput.ReadLineAsync();
                    _logger.LogInformation("Raw Python output: '{Line}'", line ?? "NULL");

                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        // Parse JSON message (same as VideoFeedService)
                        var message = JsonConvert.DeserializeObject<PythonMessage>(line);
                        if (message == null) continue;

                        _logger.LogInformation("Parsed message type: {Type}", message.Type);

                        switch (message.Type)
                        {
                            case "frame":
                                if (!string.IsNullOrEmpty(message.Data))
                                {
                                    byte[] frameBytes = Convert.FromBase64String(message.Data);
                                    FrameReceived?.Invoke(frameBytes);
                                }
                                break;

                            case "status":
                                _logger.LogInformation("Python status: {Status}", message.Message);
                                StatusChanged?.Invoke(message.Message ?? "Unknown status");
                                break;

                            case "error":
                                _logger.LogWarning("Python error: {Error}", message.Message);
                                ErrorOccurred?.Invoke(message.Message ?? "Unknown error");
                                break;

                            case "encoding":
                                if (!string.IsNullOrEmpty(message.Data))
                                {
                                    byte[] encodingBytes = Convert.FromBase64String(message.Data);
                                    _logger.LogInformation("Received face encoding: {Size} bytes", encodingBytes.Length);
                                    FaceEncodingReceived?.Invoke(encodingBytes);
                                    StatusChanged?.Invoke("Face encoding captured successfully!");
                                }
                                break;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("Failed to parse JSON message: {Line}, Error: {Error}", line, ex.Message);
                        // Treat non-JSON lines as status messages
                        StatusChanged?.Invoke(line);
                    }

                    // Re-check if process is still the same instance (in case it was replaced)
                    if (_trainingProcess != process)
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading training messages");
                ErrorOccurred?.Invoke($"Error reading messages: {ex.Message}");
            }
        }

        // Also add the PythonMessage class if it's not already in this file:
        public class PythonMessage
        {
            public string? Type { get; set; }
            public string? Data { get; set; }
            public string? Message { get; set; }
            public double Timestamp { get; set; }
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