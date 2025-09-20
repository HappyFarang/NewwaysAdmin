// File: NewwaysAdmin.WorkerAttendance.Services/PythonFaceService.cs
// Purpose: Executes Python face detection script with visual preview

using System.Diagnostics;
using Newtonsoft.Json;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.Services
{
    public class PythonFaceService
    {
        private string _pythonScriptPath;

        public event Action<string>? StatusChanged;

        public PythonFaceService(string pythonScriptPath)
        {
            _pythonScriptPath = pythonScriptPath;
        }

        public async Task<FaceDetectionResult> DetectFacesAsync()
        {
            try
            {
                StatusChanged?.Invoke("Starting face detection with visual preview...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{_pythonScriptPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);

                if (process == null)
                {
                    var errorResult = new FaceDetectionResult
                    {
                        Status = "error",
                        Message = "Failed to start Python process"
                    };
                    StatusChanged?.Invoke(errorResult.Message);
                    return errorResult;
                }

                StatusChanged?.Invoke("Scanning for faces with visual markers...");

                // Wait for the process to complete (with timeout)
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                bool finished = process.WaitForExit(15000); // 15 second timeout for visual detection

                if (!finished)
                {
                    process.Kill();
                    var timeoutResult = new FaceDetectionResult
                    {
                        Status = "timeout",
                        Message = "Face detection timed out"
                    };
                    StatusChanged?.Invoke(timeoutResult.Message);
                    return timeoutResult;
                }

                string output = await outputTask;
                string error = await errorTask;

                if (process.ExitCode != 0)
                {
                    var errorResult = new FaceDetectionResult
                    {
                        Status = "error",
                        Message = $"Python error: {error}"
                    };
                    StatusChanged?.Invoke(errorResult.Message);
                    return errorResult;
                }

                if (string.IsNullOrEmpty(output))
                {
                    var noOutputResult = new FaceDetectionResult
                    {
                        Status = "error",
                        Message = "No output from Python script"
                    };
                    StatusChanged?.Invoke(noOutputResult.Message);
                    return noOutputResult;
                }

                var result = JsonConvert.DeserializeObject<FaceDetectionResult>(output);
                var finalResult = result ?? new FaceDetectionResult
                {
                    Status = "error",
                    Message = "Invalid JSON response from Python"
                };

                StatusChanged?.Invoke($"Face detection completed: {finalResult.Status}");
                return finalResult;
            }
            catch (Exception ex)
            {
                var exceptionResult = new FaceDetectionResult
                {
                    Status = "error",
                    Message = $"Exception: {ex.Message}"
                };
                StatusChanged?.Invoke(exceptionResult.Message);
                return exceptionResult;
            }
        }

        public void StopDetection()
        {
            StatusChanged?.Invoke("Face detection stopped");
        }
    }
}