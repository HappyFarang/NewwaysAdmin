// File: NewwaysAdmin.WorkerAttendance.UI/Controls/WorkerRegistrationControl.xaml.cs
// Purpose: Worker registration component logic - clean and focused

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
        private int _savedWorkerId = 0; // Track the ID of the automatically saved worker

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

            // Subscribe to training completion - this is our direct connection!
            if (_faceTrainingWorkflowService != null)
            {
                _faceTrainingWorkflowService.TrainingCompleted += OnWorkflowTrainingCompleted;
            }

            ResetForm();
        }

        private void OnWorkflowTrainingCompleted()
        {
            // Direct connection from workflow service to registration control
            // This bypasses all the MainWindow complexity!

            // Use Dispatcher to ensure UI updates happen on UI thread
            Dispatcher.Invoke(() => {
                OnFaceTrainingCompleted(true);
            });
        }

        public void ResetForm()
        {
            WorkerNameInput.Text = "";
            _faceDataCaptured = false;
            _isTrainingInProgress = false;
            _savedWorkerId = 0;
            ValidationMessage.Text = "";
            TrainingStatus.Text = "";
            TrainingInstructions.Text = "Click 'Start Face Training' to begin";
            ScanFaceButton.Content = "Start Face Training";
            ScanFaceButton.Background = System.Windows.Media.Brushes.LightBlue;

            // Reset buttons to initial state
            SaveButton.Content = "Save Worker";
            SaveButton.Background = System.Windows.Media.Brushes.LightGreen;
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
                TrainingInstructions.Text = "Face training completed successfully!";
                TrainingStatus.Text = "Worker has been saved automatically";

                // Change Save button to OK since worker is already saved
                SaveButton.Content = "OK";
                SaveButton.Background = System.Windows.Media.Brushes.LightBlue;

                // Get the saved worker ID for potential deletion
                GetSavedWorkerId();
            }
            else
            {
                ScanFaceButton.Content = "Training Failed - Retry";
                ScanFaceButton.Background = System.Windows.Media.Brushes.LightCoral;
                TrainingInstructions.Text = "Face training failed. Click to try again.";
                TrainingStatus.Text = "Training unsuccessful";
            }
        }

        private async void GetSavedWorkerId()
        {
            try
            {
                if (_storageService == null) return;

                // Get the most recently saved worker (highest ID)
                var existingWorkers = await _storageService.GetAllWorkersAsync();
                if (existingWorkers.Count > 0)
                {
                    _savedWorkerId = existingWorkers.Max(w => w.Id);
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error getting saved worker ID: {ex.Message}");
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_faceDataCaptured)
            {
                ValidationMessage.Text = "Please complete face training first";
                return;
            }

            try
            {
                // If training is complete, this is now an "OK" button
                // Worker is already saved, so just clean up and exit
                StatusChanged?.Invoke("Worker registration completed successfully!");
                Cleanup();
                WorkerSaved?.Invoke();
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = $"Error completing registration: {ex.Message}";
                StatusChanged?.Invoke($"Error: {ex.Message}");
            }
        }

        private async void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // If a worker was automatically saved, delete it
                if (_faceDataCaptured && _savedWorkerId > 0 && _storageService != null)
                {
                    StatusChanged?.Invoke("Cancelling registration and deleting saved worker...");

                    // Delete the automatically saved worker file
                    await DeleteSavedWorker(_savedWorkerId);

                    StatusChanged?.Invoke("Registration cancelled - saved worker deleted");
                }
                else
                {
                    StatusChanged?.Invoke("Registration cancelled");
                }

                Cleanup();
                RegistrationCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = $"Error cancelling registration: {ex.Message}";
                StatusChanged?.Invoke($"Cancel error: {ex.Message}");
            }
        }

        private async Task DeleteSavedWorker(int workerId)
        {
            try
            {
                if (_storageService == null) return;

                // Get the worker to confirm it exists and get the name for logging
                var worker = await _storageService.GetWorkerByIdAsync(workerId);
                if (worker != null)
                {
                    // Delete using the clean delete method
                    await _storageService.DeleteWorkerAsync(workerId);
                    StatusChanged?.Invoke($"Deleted worker '{worker.Name}' (ID: {workerId})");
                }
                else
                {
                    StatusChanged?.Invoke($"Worker with ID {workerId} not found for deletion");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Error deleting worker: {ex.Message}");
                throw; // Re-throw so caller can handle
            }
        }

        public void Cleanup()
        {
            // Unsubscribe from events to prevent memory leaks
            if (_faceTrainingWorkflowService != null)
            {
                _faceTrainingWorkflowService.TrainingCompleted -= OnWorkflowTrainingCompleted;
            }
        }

        public string GetWorkerName()
        {
            return WorkerNameInput.Text?.Trim() ?? string.Empty;
        }
    }
}