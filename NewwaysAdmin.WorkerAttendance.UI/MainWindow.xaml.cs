// File: NewwaysAdmin.WorkerAttendance.UI/MainWindow.xaml.cs
// Purpose: Main window with unified video and detection workflow

using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NewwaysAdmin.WorkerAttendance.Services;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.UI
{
    public partial class MainWindow : Window
    {
        private ArduinoService _arduinoService;
        private VideoFeedService _videoService;
        private ApplicationState _currentState = ApplicationState.Ready;
        private List<DetectedFace> _detectedFaces = new();

        public MainWindow()
        {
            InitializeComponent();

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

        // Helper methods stay the same...
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

        protected override void OnClosed(EventArgs e)
        {
            _videoService.StopVideoFeed();
            _arduinoService.Disconnect();
            base.OnClosed(e);
        }
    }
}