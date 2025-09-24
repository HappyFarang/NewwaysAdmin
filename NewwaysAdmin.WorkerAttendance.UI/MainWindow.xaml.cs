// File: NewwaysAdmin.WorkerAttendance.UI/MainWindow.xaml.cs
// Purpose: Main window with clean component-based architecture

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NewwaysAdmin.WorkerAttendance.Services;
using NewwaysAdmin.WorkerAttendance.Models;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;
using System.Diagnostics;

namespace NewwaysAdmin.WorkerAttendance.UI
{
    public partial class MainWindow : Window
    {
        private ArduinoService _arduinoService;
        private VideoFeedService _videoService;
        private ApplicationState _currentState = ApplicationState.Ready;
        private List<DetectedFace> _detectedFaces = new();

        // Storage system fields
        private ILogger<MainWindow> _logger;
        private EnhancedStorageFactory _storageFactory;
        private WorkerStorageService _workerStorageService;
        private readonly ILoggerFactory _loggerFactory; 
        private FaceTrainingService _faceTrainingService;
        private FaceTrainingWorkflowService _faceTrainingWorkflowService;



        // UI State
        private bool _isInTrainingMode = false;
        private int _currentTrainingStep = 1;

        public MainWindow()
        {
            InitializeComponent();

            // Set up logging factory (existing code)
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            });
            _logger = _loggerFactory.CreateLogger<MainWindow>();

            // Python and Arduino setup (existing code)
            string pythonBasePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "NewwaysAdmin.WorkerAttendance.Python"
            );

            string unifiedScript = Path.Combine(pythonBasePath, "unified_video_detection.py");
            string faceTrainingScript = Path.Combine(pythonBasePath, "face_training_capture.py");

            _arduinoService = new ArduinoService();
            _videoService = new VideoFeedService(unifiedScript);

            var trainingLogger = _loggerFactory.CreateLogger<FaceTrainingService>();
            _faceTrainingService = new FaceTrainingService(faceTrainingScript, trainingLogger);
                        
            // Wire up existing events (keep existing code)
            _arduinoService.ButtonPressed += OnButtonPressed;
            _arduinoService.StatusChanged += OnArduinoStatusChanged;
            _videoService.FrameReceived += OnFrameReceived;
            _videoService.StatusChanged += OnVideoServiceStatusChanged;
            _videoService.DetectionComplete += OnDetectionComplete;

            _faceTrainingService.StatusChanged += OnFaceTrainingStatusChanged;
            _faceTrainingService.ErrorOccurred += OnFaceTrainingError;
            

            Instructions.CaptureRequested += OnCaptureRequested;

            // Wire up events with Dispatcher for UI updates
            _faceTrainingService.StatusChanged += (msg) =>
                Dispatcher.Invoke(() => UpdateStatus($"[TRAINING] {msg}"));
            _faceTrainingService.ErrorOccurred += (msg) =>
                Dispatcher.Invoke(() => UpdateStatus($"[TRAINING ERROR] {msg}"));
            _faceTrainingService.FaceEncodingReceived += OnFaceEncodingReceived;
            _faceTrainingService.FrameReceived += OnTrainingFrameReceived;


            // Start services
            _arduinoService.TryConnect();
            _ = StartVideoFeedAsync();
            //_ = TestFaceTrainingAtStartup();



            // Initialize storage after UI is loaded
            this.Loaded += MainWindow_Loaded;
        }

        private async Task TestFaceTrainingAtStartup()
        {
            try
            {
                UpdateStatus("Testing face training at startup...");

                // Start face training instead of normal video
                bool success = await _faceTrainingService.StartTrainingSessionAsync();

                if (success)
                {
                    UpdateStatus("Face training started successfully at startup!");
                }
                else
                {
                    UpdateStatus("Face training FAILED at startup");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Face training startup exception: {ex.Message}");
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize storage system
                var storageLogger = _loggerFactory.CreateLogger<WorkerStorageService>();
                _storageFactory = new EnhancedStorageFactory(_logger);
                WorkerAttendanceStorageConfiguration.ConfigureStorageFolders(_storageFactory, _logger);
                _workerStorageService = new WorkerStorageService(_storageFactory, storageLogger);

                // Create workflow service with storage ready
                var workflowLogger = _loggerFactory.CreateLogger<FaceTrainingWorkflowService>();
                _faceTrainingWorkflowService = new FaceTrainingWorkflowService(
                    _faceTrainingService,
                    _workerStorageService,
                    workflowLogger);

                // Initialize the training control - direct access since it's named in XAML
                Instructions.FaceTrainingInstructions?.Initialize(_faceTrainingWorkflowService);

                // Initialize registration component with storage service
                WorkerRegistration.Initialize(_workerStorageService);

                // Test storage system
                await TestStorageSystemAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storage initialization failed");
                UpdateStatus($"Storage system error: {ex.Message}");
            }
        }

        #region Component Event Handlers

        // Worker Management Component Events
        private void OnTrainWorkerRequested()
        {
            var passwordWindow = new PasswordWindow();
            passwordWindow.Owner = this;

            bool? result = passwordWindow.ShowDialog();

            if (result == true && passwordWindow.IsAuthenticated)
            {
                // Just switch to registration form - NO face training yet
                SwitchToTrainingMode();
            }
            else
            {
                MessageBox.Show("Access denied", "Authentication Failed");
            }
        }

        private void OnManageWorkersRequested()
        {
            var passwordWindow = new PasswordWindow();
            passwordWindow.Owner = this;

            bool? result = passwordWindow.ShowDialog();

            if (result == true && passwordWindow.IsAuthenticated)
            {
                // Password correct - proceed to worker management
                MessageBox.Show("Password correct! Worker management will be implemented here", "Access Granted");
            }
            else
            {
                // Password wrong or cancelled
                MessageBox.Show("Access denied", "Authentication Failed");
            }
        }

        // Worker Registration Component Events
        private void OnRegistrationStatusChanged(string status)
        {
            UpdateStatus(status);
        }

        private void OnWorkerSaved()
        {
            SwitchToNormalMode();
        }

        private void OnRegistrationCancelled()
        {
            SwitchToNormalMode();
        }

        private async void OnFaceTrainingRequested()
        {
            UpdateStatus("Starting face training session...");

            // Get worker name from registration control
            string workerName = WorkerRegistration.GetWorkerName(); // You might need to add this method

            if (string.IsNullOrEmpty(workerName))
            {
                UpdateStatus("ERROR: Worker name is required");
                return;
            }

            // Stop the normal video feed
            _videoService.StopVideoFeed();
            UpdateStatus("Normal video feed stopped");

            // Wait for cleanup
            await Task.Delay(500);

            // Kill any remaining processes if needed
            var pythonProcesses = Process.GetProcessesByName("python");
            var aliveProcesses = pythonProcesses.Where(p => !p.HasExited).ToList();

            if (aliveProcesses.Any())
            {
                UpdateStatus($"Terminating {aliveProcesses.Count} Python processes...");
                foreach (var proc in aliveProcesses)
                {
                    try
                    {
                        proc.Kill();
                        await proc.WaitForExitAsync();
                    }
                    catch { /* ignore */ }
                }
            }

            await Task.Delay(300);

            // Start face training process
            bool success = await _faceTrainingService.StartTrainingSessionAsync();

            if (success)
            {
                // Switch to visual instructions AND start the workflow
                Instructions.StartFaceTraining();

                // Start the workflow with the worker name
                Instructions.FaceTrainingInstructions?.StartTrainingForWorker(workerName);

                UpdateStatus($"Face training ready for {workerName} - follow the visual instructions");
            }
            else
            {
                UpdateStatus("FAILED to start Python training process");
            }
        }


        private async void OnCaptureRequested(int stepNumber)
        {
            // Debugging message (UI-safe since triggered from UI)
            MessageBox.Show($"MainWindow received capture request for step {stepNumber}!");

            // Ensure UI updates are on the UI thread
            Dispatcher.Invoke(() =>
            {
                UpdateStatus($"Capturing face step {stepNumber}...");
                Instructions.UpdateTrainingStatus("Hold still - capturing...");
            });

            // Send capture command (non-UI, safe on any thread)
            await _faceTrainingService.RequestFaceCaptureAsync();
        }
        #endregion

        #region UI Mode Management

        private void SwitchToTrainingMode()
        {
            _isInTrainingMode = true;

            // Update UI components for training mode
            Instructions.ShowTrainingMode();
            Instructions.UpdateTrainingStep("Enter worker name and click 'Start Face Training'");

            WorkerManagement.Visibility = Visibility.Collapsed;
            WorkerRegistration.Visibility = Visibility.Visible;
            WorkerRegistration.ResetForm();

            UpdateStatus("Training mode activated - register new worker");
        }

        private async void SwitchToNormalMode()
        {
            _isInTrainingMode = false;

            // Unwire events
            Instructions.CaptureRequested -= OnCaptureRequested;
            Instructions.TrainingCompleted -= OnTrainingCompleted;
            Instructions.TrainingCancelled -= OnTrainingCancelled;

            UpdateStatus("Switching back to normal mode...");

            // Stop face training process properly
            try
            {
                await _faceTrainingService.StopTrainingSessionAsync();
                UpdateStatus("Face training stop command sent");

                // Give time for Python to cleanup properly (camera.release(), etc.)
                await Task.Delay(1500);

                // Only kill processes if they haven't exited cleanly
                var pythonProcesses = Process.GetProcessesByName("python");
                var aliveProcesses = pythonProcesses.Where(p => !p.HasExited).ToList();

                if (aliveProcesses.Any())
                {
                    UpdateStatus($"Found {aliveProcesses.Count} Python processes still running - terminating...");

                    foreach (var proc in aliveProcesses)
                    {
                        try
                        {
                            proc.Kill();
                            await proc.WaitForExitAsync();
                            UpdateStatus($"Terminated process {proc.Id}");
                        }
                        catch (Exception ex)
                        {
                            UpdateStatus($"Could not kill process {proc.Id}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    UpdateStatus("All Python processes exited cleanly");
                }

                await Task.Delay(300); // Brief delay for resource cleanup

                // Restart normal video feed
                UpdateStatus("Restarting normal video feed...");
                await _videoService.StartVideoFeedAsync();

            }
            catch (Exception ex)
            {
                UpdateStatus($"Error during mode switch: {ex.Message}");
            }

            // Update UI
            Instructions.ShowNormalMode();
            WorkerRegistration.Visibility = Visibility.Collapsed;
            WorkerManagement.Visibility = Visibility.Visible;
            UpdateStatus("Ready for attendance scanning...");
        }

        #endregion

        #region Existing Video/Arduino Logic (Unchanged)

        private async Task StartVideoFeedAsync()
        {
            await _videoService.StartVideoFeedAsync();
        }

        private void OnFrameReceived(BitmapImage frame)
        {
            Dispatcher.Invoke(() =>
            {
                VideoFeed.Source = frame;
            });
        }

        private async void OnButtonPressed(string buttonType)
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                if (buttonType == "SIGN_IN" && _currentState == ApplicationState.Ready)
                {
                    await StartFaceDetectionAsync();
                }
                else if (buttonType == "CONFIRM" && _currentState == ApplicationState.WaitingForConfirmation)
                {
                    await ProcessConfirmationAsync();
                }
                else
                {
                    UpdateStatus($"Invalid action. Current state: {_currentState}");
                }
            });
        }

        private async Task StartFaceDetectionAsync()
        {
            try
            {
                _currentState = ApplicationState.Scanning;
                UpdateState("Scanning...");
                UpdateStatus("Starting face detection... Look at the camera");

                await _videoService.StartDetectionAsync();
            }
            catch (Exception ex)
            {
                _currentState = ApplicationState.Ready;
                UpdateState("Ready");
                UpdateStatus($"Error starting detection: {ex.Message}");
            }
        }

        private void OnDetectionComplete(FaceDetectionResult result)
        {
            Dispatcher.Invoke(() =>
            {
                if (result.Status == "success" && result.Faces?.Count > 0)
                {
                    _detectedFaces = result.Faces;
                    _currentState = ApplicationState.WaitingForConfirmation;
                    UpdateState("Face Detected!");
                    UpdateStatus($"Face detected! Press CONFIRM to check in as: {result.Faces[0].Id}");
                }
                else
                {
                    _currentState = ApplicationState.Ready;
                    UpdateState("Ready");
                    UpdateStatus("Face detection failed. Try again.");
                }
            });
        }

        private async Task ProcessConfirmationAsync()
        {
            try
            {
                _currentState = ApplicationState.Processing;
                UpdateState("Processing...");

                var primaryFace = _detectedFaces[0];
                UpdateStatus($"Confirmed! Checking in worker: {primaryFace.Id}");

                await _videoService.StopDetectionAsync();

                // TODO: Save attendance record here
                await Task.Delay(2000);

                _currentState = ApplicationState.Ready;
                _detectedFaces.Clear();
                UpdateState("Ready");
                UpdateStatus("Ready for next scan...");
            }
            catch (Exception ex)
            {
                _currentState = ApplicationState.Ready;
                UpdateState("Ready");
                UpdateStatus($"Error processing confirmation: {ex.Message}");
            }
        }

        private async Task TestStorageSystemAsync()
        {
            try
            {
                var workers = await _workerStorageService.GetAllWorkersAsync();
                _logger.LogInformation("Found {Count} workers in database", workers.Count);
                UpdateStatus($"Storage system ready - {workers.Count} workers loaded");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storage system test failed");
                UpdateStatus($"Storage system error: {ex.Message}");
            }
        }

        private void OnArduinoStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (_currentState == ApplicationState.Ready)
                {
                    UpdateStatus(status);
                }
            });
        }

        private void OnVideoServiceStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                if (_currentState == ApplicationState.Ready || _currentState == ApplicationState.Scanning)
                {
                    UpdateStatus(status);
                }
            });
        }

        private void UpdateStatus(string message)
        {
            // Status now shown via StateDisplay instead of removed StatusText
            // Could also update StateDisplay here if needed
        }

        private void UpdateState(string state)
        {
            StateDisplay.Text = state;
            StateDisplay.Foreground = _currentState switch
            {
                ApplicationState.Ready => System.Windows.Media.Brushes.Green,
                ApplicationState.Scanning => System.Windows.Media.Brushes.Orange,
                ApplicationState.WaitingForConfirmation => System.Windows.Media.Brushes.Blue,
                ApplicationState.Processing => System.Windows.Media.Brushes.Purple,
                _ => System.Windows.Media.Brushes.Black
            };
        }

        #endregion
        private void OnFaceTrainingStatusChanged(string status)
        {
            Dispatcher.Invoke(() => UpdateStatus($"Training: {status}"));
        }

        private void OnFaceTrainingError(string error)
        {
            Dispatcher.Invoke(() => UpdateStatus($"Training Error: {error}"));
        }

        private void OnFaceEncodingReceived(byte[] encoding)
        {
            Dispatcher.Invoke(() =>
            {
                // Notify UI component that step was captured successfully
                Instructions.OnStepCaptured(_currentTrainingStep, true);

                // Move to next step
                _currentTrainingStep++;

                // Optional: Update status to reflect progress
                UpdateStatus($"Captured face encoding for step {_currentTrainingStep - 1}");
            });

            // Store the encoding for later use (non-UI, safe outside Dispatcher)
            // TODO: Add to worker's face encodings list
        }

        private void OnTrainingFrameReceived(byte[] frameData)
        {
            try
            {
                var bitmap = BytesToBitmapImage(frameData);
                Dispatcher.Invoke(() =>
                {
                    VideoFeed.Source = bitmap; // Show training video feed
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error displaying training frame");
            }
        }

        private BitmapImage BytesToBitmapImage(byte[] imageData)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(imageData);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        protected override void OnClosed(EventArgs e)
        {
            _videoService.StopVideoFeed();
            _arduinoService.Disconnect();
            _loggerFactory?.Dispose();
            base.OnClosed(e);
        }        

        private void OnTrainingCompleted()
        {
            // TODO: Save worker and return to normal mode
            SwitchToNormalMode();
            UpdateStatus("Face training completed!");
        }

        private void OnTrainingCancelled()
        {
            // Handle training cancellation
            SwitchToNormalMode();
            UpdateStatus("Face training cancelled");
        }

        private async void TestPythonProcess()
        {
            try
            {
                UpdateStatus("Testing basic Python process...");

                // Test 1: Try basic python version check
                var testInfo = new ProcessStartInfo
                {
                    FileName = @"C:\Python312\python.exe",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                UpdateStatus("Attempting python --version...");
                var process = Process.Start(testInfo);

                if (process != null)
                {
                    await process.WaitForExitAsync();
                    var output = await process.StandardOutput.ReadToEndAsync();
                    UpdateStatus($"Python version test SUCCESS: {output.Trim()}");
                }
                else
                {
                    UpdateStatus("Python version test FAILED - process is null");
                    return;
                }

                // Test 2: Try running our script with just import test
                var scriptPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..",
                    "NewwaysAdmin.WorkerAttendance.Python",
                    "face_training_capture.py"
                );

                UpdateStatus($"Script path: {scriptPath}");
                UpdateStatus($"Script exists: {File.Exists(scriptPath)}");

                var scriptInfo = new ProcessStartInfo
                {
                    FileName = @"C:\Python312\python.exe",
                    Arguments = $"-c \"print('Python can execute')\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                UpdateStatus("Testing basic Python execution...");
                var scriptProcess = Process.Start(scriptInfo);

                if (scriptProcess != null)
                {
                    await scriptProcess.WaitForExitAsync();
                    var stdout = await scriptProcess.StandardOutput.ReadToEndAsync();
                    var stderr = await scriptProcess.StandardError.ReadToEndAsync();

                    UpdateStatus($"Basic Python test - Exit code: {scriptProcess.ExitCode}");
                    UpdateStatus($"Stdout: {stdout.Trim()}");
                    if (!string.IsNullOrEmpty(stderr))
                        UpdateStatus($"Stderr: {stderr.Trim()}");
                }

            }
            catch (Exception ex)
            {
                UpdateStatus($"Python test EXCEPTION: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}