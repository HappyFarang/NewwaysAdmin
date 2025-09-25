// File: NewwaysAdmin.WorkerAttendance.UI/VideoFeedService.cs
// Purpose: Manages unified video feed with detection commands

using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.UI
{
    public class VideoFeedService
    {
        private Process? _videoProcess;
        private bool _isRunning = false;
        private string _pythonScript;

        public event Action<BitmapImage>? FrameReceived;
        public event Action<string>? StatusChanged;
        public event Action<FaceDetectionResult>? DetectionComplete;

        public event Action<string, double, string>? SignInRecognition; // worker_name, confidence, worker_id
        public event Action<string>? SignInUnknown;
        public event Action<string, double, string>? SignInConfirmed; // worker_name, confidence, worker_id


        public VideoFeedService(string pythonScript)
        {
            _pythonScript = pythonScript;
        }

        public async Task<bool> StartVideoFeedAsync()
        {
            try
            {
                if (_isRunning)
                    return true;

                StatusChanged?.Invoke("Starting unified video service...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{_pythonScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _videoProcess = Process.Start(startInfo);

                if (_videoProcess == null)
                {
                    StatusChanged?.Invoke("Failed to start video service");
                    return false;
                }

                _isRunning = true;
                _ = Task.Run(ReadMessagesFromPython);

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error starting video: {ex.Message}");
                return false;
            }
        }

        public async Task StartDetectionAsync()
        {
            try
            {
                StatusChanged?.Invoke("Sending start_detection command via file...");

                string commandFile = Path.Combine(Path.GetTempPath(), "face_detection_command.txt");
                await File.WriteAllTextAsync(commandFile, "start_detection");

                StatusChanged?.Invoke("Command file created successfully");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error sending command: {ex.Message}");
            }
        }

        public async Task StopDetectionAsync()
        {
            try
            {
                string commandFile = Path.Combine(Path.GetTempPath(), "face_detection_command.txt");
                await File.WriteAllTextAsync(commandFile, "stop_detection");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error sending stop command: {ex.Message}");
            }
        }

        private async Task ReadMessagesFromPython()
        {
            if (_videoProcess?.StandardOutput == null) return;

            try
            {
                while (!_videoProcess.HasExited && _isRunning)
                {
                    var line = await _videoProcess.StandardOutput.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        var message = JsonConvert.DeserializeObject<PythonMessage>(line);
                        if (message == null) continue;

                        switch (message.Type)
                        {
                            case "frame":
                                if (!string.IsNullOrEmpty(message.Data))
                                {
                                    var bitmap = Base64ToBitmapImage(message.Data);
                                    if (bitmap != null)
                                    {
                                        FrameReceived?.Invoke(bitmap);
                                    }
                                }
                                break;

                            case "status":
                                StatusChanged?.Invoke(message.Message ?? "Unknown status");
                                break;

                            case "detection_complete":
                                var result = new FaceDetectionResult
                                {
                                    Status = message.Status ?? "unknown",
                                    Message = message.Message ?? "",
                                    Faces = message.Faces?.Select(f => new DetectedFace
                                    {
                                        Id = f.Id ?? "",
                                        Confidence = f.Confidence,
                                        Position = f.Position != null ? new FacePosition
                                        {
                                            X = f.Position.X,
                                            Y = f.Position.Y,
                                            Width = f.Position.Width,
                                            Height = f.Position.Height
                                        } : null
                                    }).ToList()
                                };
                                DetectionComplete?.Invoke(result);
                                break;

                            case "error":
                                StatusChanged?.Invoke($"Python error: {message.Message}");
                                break;
                            case "signin_recognition":
                                if (message.Worker_Name != null && message.Worker_Id != null)
                                {
                                    SignInRecognition?.Invoke(
                                        message.Worker_Name,
                                        message.Confidence,
                                        message.Worker_Id
                                    );
                                }
                                break;

                            case "signin_unknown":
                                SignInUnknown?.Invoke(message.Message ?? "Unknown person detected");
                                break;

                            case "signin_confirmed":
                                if (message.Worker_Name != null && message.Worker_Id != null)
                                {
                                    SignInConfirmed?.Invoke(
                                        message.Worker_Name,
                                        message.Confidence,
                                        message.Worker_Id
                                    );
                                }
                                break;
                        }
                    }
                    catch (JsonException)
                    {
                        StatusChanged?.Invoke(line);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error reading messages: {ex.Message}");
            }
        }

        private BitmapImage? Base64ToBitmapImage(string base64String)
        {
            try
            {
                byte[] imageBytes = Convert.FromBase64String(base64String);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(imageBytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error converting frame: {ex.Message}");
                return null;
            }
        }

        public void StopVideoFeed()
        {
            try
            {
                _isRunning = false;

                if (_videoProcess?.StandardInput != null)
                {
                    var command = new { command = "stop" };
                    _videoProcess.StandardInput.WriteLine(JsonConvert.SerializeObject(command));
                    _videoProcess.StandardInput.Flush();
                }

                if (_videoProcess?.HasExited == false)
                {
                    _videoProcess.Kill();
                }

                _videoProcess?.Dispose();
                _videoProcess = null;

                StatusChanged?.Invoke("Video service stopped");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error stopping video: {ex.Message}");
            }
        }
        public async Task ConfirmSignInAsync()
        {
            try
            {
                StatusChanged?.Invoke("Sending confirmation command...");

                string commandFile = Path.Combine(Path.GetTempPath(), "face_detection_command.txt");
                await File.WriteAllTextAsync(commandFile, "confirm_signin");

                StatusChanged?.Invoke("Confirmation command sent");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error sending confirmation: {ex.Message}");
            }
        }
    }

    // Updated message classes for unified communication
    public class PythonMessage
    {
        // Keep existing properties:
        public string? Type { get; set; }
        public string? Data { get; set; }
        public string? Message { get; set; }
        public string? Status { get; set; }
        public double Timestamp { get; set; }
        public List<PythonFace>? Faces { get; set; }

        // ADD these new properties:
        public string? Worker_Name { get; set; }
        public string? Worker_Id { get; set; }
        public double Confidence { get; set; }
    }

    public class PythonFace
    {
        public string? Id { get; set; }
        public double Confidence { get; set; }
        public PythonPosition? Position { get; set; }
    }

    public class PythonPosition
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}