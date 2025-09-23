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



        // UI State
        private bool _isInTrainingMode = false;
        private int _currentTrainingStep = 1;

        public MainWindow()
        {
            InitializeComponent();

            // Set up logging factory
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole().SetMinimumLevel(LogLevel.Information);
            });
            _logger = _loggerFactory.CreateLogger<MainWindow>();

            // Python and Arduino setup
            string pythonBasePath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..",
                "NewwaysAdmin.WorkerAttendance.Python"
            );

            string unifiedScript = Path.Combine(pythonBasePath, "unified_video_detection.py");

            _arduinoService = new ArduinoService();
            _videoService = new VideoFeedService(unifiedScript);

            string faceTrainingScript = Path.Combine(pythonBasePath, "face_training_capture.py");
            var trainingLogger = _loggerFactory.CreateLogger<FaceTrainingService>();
            _faceTrainingService = new FaceTrainingService(faceTrainingScript, trainingLogger);


            // Wire up events
            _arduinoService.ButtonPressed += OnButtonPressed;
            _arduinoService.StatusChanged += OnArduinoStatusChanged;
            _videoService.FrameReceived += OnFrameReceived;
            _videoService.StatusChanged += OnVideoServiceStatusChanged;
            _videoService.DetectionComplete += OnDetectionComplete;

            // Wire up face training events
            _faceTrainingService.StatusChanged += OnFaceTrainingStatusChanged;
            _faceTrainingService.ErrorOccurred += OnFaceTrainingError;
            _faceTrainingService.FaceEncodingReceived += OnFaceEncodingReceived;
            _faceTrainingService.FrameReceived += OnTrainingFrameReceived;

            // Start services
            _arduinoService.TryConnect();
            _ = StartVideoFeedAsync();

            // Initialize storage after UI is loaded
            this.Loaded += MainWindow_Loaded;
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
            // Make sure this calls the right method
            Instructions.StartFaceTraining();

            UpdateStatus("Testing Python directly...");

            try
            {
                // Test Python version
                var pythonTest = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(pythonTest);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    UpdateStatus($"Python test: {output.Trim()} {error}");
                }
                else
                {
                    UpdateStatus("Python process failed to start");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Python error: {ex.Message}");
            }

            // Check script file
            try
            {
                string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "..", "..", "..", "..",
                    "NewwaysAdmin.WorkerAttendance.Python",
                    "face_training_capture.py");

                bool exists = File.Exists(scriptPath);
                UpdateStatus($"Script exists: {exists} at {scriptPath}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Path error: {ex.Message}");
            }
        }

        private async void OnCaptureRequested(int stepNumber)
        {
            UpdateStatus($"Capturing face step {stepNumber}...");
            Instructions.UpdateTrainingStatus("Hold still - capturing...");

            // Tell Python to capture current frame
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

        private void SwitchToNormalMode()
        {
            _isInTrainingMode = false;

            // Unwire events
            Instructions.CaptureRequested -= OnCaptureRequested;
            Instructions.TrainingCompleted -= OnTrainingCompleted;
            Instructions.TrainingCancelled -= OnTrainingCancelled;

            // Rest of your existing code...
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
            UpdateStatus($"Training: {status}");
        }

        private void OnFaceTrainingError(string error)
        {
            UpdateStatus($"Training Error: {error}");
        }

        private void OnFaceEncodingReceived(byte[] encoding)
        {
            // Notify UI component that step was captured successfully
            Instructions.OnStepCaptured(_currentTrainingStep, true);

            // Move to next step
            _currentTrainingStep++;

            // Store the encoding for later use
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
    }
}