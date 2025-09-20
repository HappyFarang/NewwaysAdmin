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

        public void Initialize(WorkerStorageService storageService)
        {
            _storageService = storageService;
            ResetForm();
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
        }

        private void ScanFaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isTrainingInProgress)
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
                TrainingStatus.Text = "Ready to save worker";
            }
            else
            {
                ScanFaceButton.Content = "Training Failed - Retry";
                ScanFaceButton.Background = System.Windows.Media.Brushes.LightCoral;
                TrainingInstructions.Text = "Face training failed. Click to try again.";
                TrainingStatus.Text = "Training unsuccessful";
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm())
                return;

            try
            {
                await SaveWorker();
            }
            catch (Exception ex)
            {
                ValidationMessage.Text = $"Error saving worker: {ex.Message}";
                StatusChanged?.Invoke($"Save error: {ex.Message}");
            }
        }

        private bool ValidateForm()
        {
            ValidationMessage.Text = "";

            if (string.IsNullOrWhiteSpace(WorkerNameInput.Text))
            {
                ValidationMessage.Text = "Worker name is required";
                return false;
            }

            if (!_faceDataCaptured)
            {
                ValidationMessage.Text = "Please complete face training first";
                return false;
            }

            return true;
        }

        private async Task SaveWorker()
        {
            if (_storageService == null)
            {
                ValidationMessage.Text = "Storage service not available";
                return;
            }

            string workerName = WorkerNameInput.Text.Trim();
            StatusChanged?.Invoke($"Saving worker '{workerName}'...");

            // Generate next worker ID
            var existingWorkers = await _storageService.GetAllWorkersAsync();
            int nextId = existingWorkers.Count > 0 ? existingWorkers.Max(w => w.Id) + 1 : 1;

            // Create new worker
            var worker = new Worker
            {
                Id = nextId,
                Name = workerName,
                IsActive = true,
                CreatedDate = DateTime.Now,
                FaceEncodings = new List<byte[]>() // TODO: Add actual face data from training
            };

            // Save using IO Manager
            await _storageService.SaveWorkerAsync(worker);

            StatusChanged?.Invoke($"Worker '{workerName}' saved successfully with ID {nextId}!");
            WorkerSaved?.Invoke();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
            StatusChanged?.Invoke("Worker registration cancelled");
            RegistrationCancelled?.Invoke();
        }
    }
}