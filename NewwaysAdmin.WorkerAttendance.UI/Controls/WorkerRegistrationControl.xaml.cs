// File: NewwaysAdmin.WorkerAttendance.UI/Controls/WorkerRegistrationControl.xaml.cs
// Purpose: Worker registration component logic
// FIXED: Proper event cleanup between training sessions

using System.Windows;
using System.Windows.Controls;
using NewwaysAdmin.WorkerAttendance.Services;
using NewwaysAdmin.WorkerAttendance.Models;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class WorkerRegistrationControl : UserControl
    {
        private WorkerStorageService? _storageService;
        private FaceTrainingWorkflowService? _faceTrainingWorkflowService;
        private bool _faceDataCaptured = false;
        private bool _isTrainingInProgress = false;

        // Events to communicate with parent window
        public event Action<string>? StatusChanged;
        public event Action? WorkerSaved;
        public event Action? RegistrationCancelled;
        public event Action? FaceTrainingRequested;

        public WorkerRegistrationControl()
        {
            InitializeComponent();
        }

        public void Initialize(WorkerStorageService storageService, FaceTrainingWorkflowService workflowService)
        {
            _storageService = storageService;
            _faceTrainingWorkflowService = workflowService;

            // CRITICAL FIX: Unsubscribe first to prevent accumulation
            // This is called every time we switch to training mode
            Cleanup(); // Use existing Cleanup method

            // Subscribe to workflow events
            if (_faceTrainingWorkflowService != null)
            {
                _faceTrainingWorkflowService.AllStepsCompleted += OnAllStepsCompleted;
                _faceTrainingWorkflowService.WorkerSaved += OnWorkerSaved;
            }

            ResetForm();
        }

        /// <summary>
        /// Cleanup: Unsubscribe from workflow events
        /// </summary>
        public void Cleanup()
        {
            if (_faceTrainingWorkflowService != null)
            {
                _faceTrainingWorkflowService.AllStepsCompleted -= OnAllStepsCompleted;
                _faceTrainingWorkflowService.WorkerSaved -= OnWorkerSaved;
            }
        }

        /// <summary>
        /// Called when all 4 steps captured, but NOT yet saved
        /// </summary>
        private void OnAllStepsCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                _isTrainingInProgress = false;
                _faceDataCaptured = true;

                ScanFaceButton.Content = "Face Training Complete ✓";
                ScanFaceButton.Background = System.Windows.Media.Brushes.LightGreen;
                TrainingInstructions.Text = "All poses captured! Click SAVE to store worker.";
                TrainingStatus.Text = "Ready to save - or click CANCEL to discard";

                SaveButton.IsEnabled = true;
                ValidationMessage.Text = "";
            });
        }

        /// <summary>
        /// Called after worker actually saved to storage
        /// </summary>
        private void OnWorkerSaved()
        {
            Dispatcher.Invoke(() =>
            {
                StatusChanged?.Invoke("Worker saved successfully!");
                // DON'T cleanup here - we want to train more workers!
                // Cleanup will happen in Initialize() before next worker
                WorkerSaved?.Invoke();
            });
        }

        public void ResetForm()
        {
            WorkerNameInput.Text = "";
            _faceDataCaptured = false;
            _isTrainingInProgress = false;
            ValidationMessage.Text = "";
            TrainingStatus.Text = "";
            TrainingInstructions.Text = "Click 'Start Face Training' to begin";
            ScanFaceButton.Content = "Start Face Training";
            ScanFaceButton.Background = System.Windows.Media.Brushes.LightBlue;
            ScanFaceButton.IsEnabled = true;

            SaveButton.Content = "Save Worker";
            SaveButton.Background = System.Windows.Media.Brushes.LightGreen;
            SaveButton.IsEnabled = false;
        }

        private void ScanFaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTrainingInProgress && !_faceDataCaptured)
            {
                StartFaceTraining();
            }
        }

        private void StartFaceTraining()
        {
            if (string.IsNullOrWhiteSpace(WorkerNameInput.Text))
            {
                ValidationMessage.Text = "Please enter worker name first";
                return;
            }

            _isTrainingInProgress = true;
            ScanFaceButton.Content = "Training in Progress...";
            ScanFaceButton.Background = System.Windows.Media.Brushes.Orange;
            ScanFaceButton.IsEnabled = false;
            TrainingInstructions.Text = "Look directly at the camera";
            TrainingStatus.Text = "Starting face training...";
            ValidationMessage.Text = "";

            FaceTrainingRequested?.Invoke();
        }

        public void UpdateTrainingInstructions(string instruction)
        {
            TrainingInstructions.Text = instruction;
        }

        public void UpdateTrainingStatus(string status)
        {
            TrainingStatus.Text = status;
        }

        public void OnFaceTrainingCompleted(bool success)
        {
            _isTrainingInProgress = false;
            _faceDataCaptured = success;

            if (success)
            {
                ScanFaceButton.Content = "Face Training Complete ✓";
                ScanFaceButton.Background = System.Windows.Media.Brushes.LightGreen;
                TrainingInstructions.Text = "All poses captured! Click SAVE to store worker.";
                TrainingStatus.Text = "Ready to save";
                SaveButton.IsEnabled = true;
            }
            else
            {
                ScanFaceButton.Content = "Training Failed";
                ScanFaceButton.Background = System.Windows.Media.Brushes.Red;
                TrainingInstructions.Text = "Training failed. Please try again.";
                TrainingStatus.Text = "Error during training";
                ScanFaceButton.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_faceDataCaptured)
            {
                ValidationMessage.Text = "Please complete face training first";
                return;
            }

            if (string.IsNullOrWhiteSpace(WorkerNameInput.Text))
            {
                ValidationMessage.Text = "Please enter worker name";
                return;
            }

            SaveButton.IsEnabled = false;
            SaveButton.Content = "Saving...";
            ValidationMessage.Text = "";

            try
            {
                if (_faceTrainingWorkflowService != null)
                {
                    bool success = await _faceTrainingWorkflowService.SaveWorkerAsync();

                    if (success)
                    {
                        SaveButton.Content = "Saved ✓";
                        SaveButton.Background = System.Windows.Media.Brushes.Green;
                        TrainingStatus.Text = "Worker saved successfully!";

                        await Task.Delay(1000);

                        ResetForm();
                        WorkerSaved?.Invoke();
                    }
                    else
                    {
                        ValidationMessage.Text = "Failed to save worker";
                        SaveButton.IsEnabled = true;
                        SaveButton.Content = "Save Worker";
                    }
                }
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = $"Error: {ex.Message}";
                SaveButton.IsEnabled = true;
                SaveButton.Content = "Save Worker";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
            RegistrationCancelled?.Invoke();
        }

        public string GetWorkerName()
        {
            return WorkerNameInput.Text?.Trim() ?? string.Empty;
        }
    }
}