// File: NewwaysAdmin.WorkerAttendance.UI/MainWindow.xaml.cs
// Purpose: Main window with unified video and detection workflow

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NewwaysAdmin.WorkerAttendance.Services;
using NewwaysAdmin.WorkerAttendance.Models;
using Microsoft.Extensions.Logging;
using NewwaysAdmin.Shared.IO.Structure;


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

            // Wire up events
            _arduinoService.ButtonPressed += OnButtonPressed;
            _arduinoService.StatusChanged += OnArduinoStatusChanged;
            _videoService.FrameReceived += OnFrameReceived;
            _videoService.StatusChanged += OnVideoServiceStatusChanged;
            _videoService.DetectionComplete += OnDetectionComplete;

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

                // Test storage system
                await TestStorageSystemAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Storage initialization failed");
                UpdateStatus($"Storage system error: {ex.Message}");
            }
        }

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

        private void UpdateStatus(string message)
        {
            StatusText.Text = message;
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
        private void TrainWorkerButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Worker training will be implemented here", "Coming Soon");
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



        private void ManageWorkersButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Worker management will be implemented here", "Coming Soon");
        }
        protected override void OnClosed(EventArgs e)
        {
            _videoService.StopVideoFeed();
            _arduinoService.Disconnect();
            _loggerFactory?.Dispose();
            base.OnClosed(e);
        }
    }
}