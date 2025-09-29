// File: NewwaysAdmin.WorkerAttendance.UI/Controls/WorkerRegistrationControl.xaml.cs
// Purpose: Worker registration component logic - clean and focused
// FIXED: Manual save only, proper cancel, can train multiple workers

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

            // Subscribe to workflow events
            if (_faceTrainingWorkflowService != null)
            {
                // CHANGED: Subscribe to AllStepsCompleted instead of TrainingCompleted
                _faceTrainingWorkflowService.AllStepsCompleted += OnAllStepsCompleted;
                _faceTrainingWorkflowService.WorkerSaved += OnWorkerSaved;
            }

            ResetForm();
        }

        /// <summary>
        /// CHANGED: Called when all 4 steps captured, but NOT yet saved
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

                // Save button remains "Save Worker" - user must click it
                SaveButton.IsEnabled = true;
                ValidationMessage.Text = "";
            });
        }

        /// <summary>
        /// NEW: Called after worker actually saved to storage
        /// </summary>
        private void OnWorkerSaved()
        {
            Dispatcher.Invoke(() =>
            {
                StatusChanged?.Invoke("Worker saved successfully!");
                Cleanup();
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

            // Reset buttons to initial state
            SaveButton.Content = "Save Worker";
            SaveButton.Background = System.Windows.Media.Brushes.LightGreen;
            SaveButton.IsEnabled = false;  // Disabled until training complete
        }

        private void ScanFaceButton_Click(object sender, RoutedEventArgs e)
        {
            // Only allow starting training if not in progress and no data captured yet
            if (!_isTrainingInProgress && !_faceDataCaptured)
            {
                StartFaceTraining();
            }
            // If training complete or in progress, button does nothing
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
            ScanFaceButton.IsEnabled = false;  // Disable during training
            TrainingInstructions.Text = "Look directly at the camera";
            TrainingStatus.Text = "Starting face training...";
            ValidationMessage.Text = "";

            // Notify parent that face training is requested
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
                TrainingStatus.Text = "Ready to save - or click CANCEL to discard";
                SaveButton.IsEnabled = true;
            }
            else
            {
                ScanFaceButton.Content = "Training Failed - Retry";
                ScanFaceButton.Background = System.Windows.Media.Brushes.LightCoral;
                ScanFaceButton.IsEnabled = true;  // Allow retry
                TrainingInstructions.Text = "Face training failed. Click to try again.";
                TrainingStatus.Text = "Training unsuccessful";
            }
        }

        /// <summary>
        /// CHANGED: Now explicitly saves the worker (doesn't auto-save)
        /// </summary>
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_faceDataCaptured)
            {
                ValidationMessage.Text = "Please complete face training first";
                return;
            }

            try
            {
                StatusChanged?.Invoke("Saving worker...");
                SaveButton.IsEnabled = false;  // Prevent double-click

                // CHANGED: Explicitly call SaveWorkerAsync
                if (_faceTrainingWorkflowService != null)
                {
                    bool success = await _faceTrainingWorkflowService.SaveWorkerAsync();

                    if (!success)
                    {
                        ValidationMessage.Text = "Failed to save worker";
                        SaveButton.IsEnabled = true;  // Re-enable on failure
                    }
                    // Success is handled by OnWorkerSaved event
                }
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = $"Error saving worker: {ex.Message}";
                StatusChanged?.Invoke($"Error: {ex.Message}");
                SaveButton.IsEnabled = true;  // Re-enable on error
            }
        }

        /// <summary>
        /// FIXED: Cancel now works correctly - discards data without saving
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusChanged?.Invoke("Registration cancelled");

                // CHANGED: Call CancelTraining to discard data
                if (_faceTrainingWorkflowService != null)
                {
                    _faceTrainingWorkflowService.CancelTraining();
                }

                Cleanup();
                RegistrationCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = $"Error cancelling: {ex.Message}";
                StatusChanged?.Invoke($"Cancel error: {ex.Message}");
            }
        }

        public void Cleanup()
        {
            // Unsubscribe from events to prevent memory leaks
            if (_faceTrainingWorkflowService != null)
            {
                _faceTrainingWorkflowService.AllStepsCompleted -= OnAllStepsCompleted;
                _faceTrainingWorkflowService.WorkerSaved -= OnWorkerSaved;
            }
        }

        public string GetWorkerName()
        {
            return WorkerNameInput.Text?.Trim() ?? string.Empty;
        }
    }
}