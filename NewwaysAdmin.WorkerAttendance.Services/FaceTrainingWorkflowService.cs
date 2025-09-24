// File: NewwaysAdmin.WorkerAttendance.Services/FaceTrainingWorkflowService.cs
// Purpose: Manages the 4-pose face training workflow and encoding collection

using Microsoft.Extensions.Logging;
using NewwaysAdmin.WorkerAttendance.Models;
using System.Windows;
using System.Threading.Tasks;

namespace NewwaysAdmin.WorkerAttendance.Services
{
    public class FaceTrainingWorkflowService
    {
        private readonly FaceTrainingService _faceTrainingService;
        private readonly WorkerStorageService _storageService;
        private readonly ILogger<FaceTrainingWorkflowService> _logger;

        // Training state
        private readonly List<byte[]> _capturedEncodings = new();
        private int _currentStep = 1;
        private const int _totalSteps = 4;
        private string? _workerName;

        // Events for UI communication
        public event Action<int, bool>? StepCompleted;        // stepNumber, success
        public event Action<string>? StatusChanged;
        public event Action? TrainingCompleted;
        public event Action<string>? ErrorOccurred;

        public bool IsTrainingActive { get; private set; }
        public int CurrentStep => _currentStep;
        public int TotalSteps => _totalSteps;

        public FaceTrainingWorkflowService(
            FaceTrainingService faceTrainingService,
            WorkerStorageService storageService,
            ILogger<FaceTrainingWorkflowService> logger)
        {
            _faceTrainingService = faceTrainingService;
            _storageService = storageService;
            _logger = logger;

            // Wire up to face training service events
            _faceTrainingService.FaceEncodingReceived += OnFaceEncodingReceived;
            _faceTrainingService.ErrorOccurred += OnTrainingServiceError;
        }

        public void StartTrainingWorkflow(string workerName)
        {
            if (IsTrainingActive)
            {
                _logger.LogWarning("Training workflow already active");
                return;
            }

            _workerName = workerName;
            _currentStep = 1;
            _capturedEncodings.Clear();
            IsTrainingActive = true;

            _logger.LogInformation("Started face training workflow for worker: {WorkerName}", workerName);
            StatusChanged?.Invoke($"Starting face training for {workerName} - Step 1 of {_totalSteps}");
        }

        public async Task ProcessCaptureRequestAsync(int requestedStep)
        {
            if (!IsTrainingActive)
            {
                _logger.LogWarning("Capture requested but training workflow not active");
                ErrorOccurred?.Invoke("Training workflow not active");
                return;
            }

            if (requestedStep != _currentStep)
            {
                _logger.LogWarning("Capture requested for step {Requested} but current step is {Current}",
                    requestedStep, _currentStep);
                ErrorOccurred?.Invoke($"Expected step {_currentStep}, got {requestedStep}");
                return;
            }

            _logger.LogInformation("Processing capture request for step {Step}", _currentStep);
            StatusChanged?.Invoke($"Capturing pose {_currentStep} of {_totalSteps}...");

            // Delegate to face training service
            await _faceTrainingService.RequestFaceCaptureAsync();
        }

        private void OnFaceEncodingReceived(byte[] encoding)
        {
            if (!IsTrainingActive) return;

            _logger.LogInformation("Received face encoding for step {Step}: {Size} bytes",
                _currentStep, encoding.Length);

            // Store the encoding
            _capturedEncodings.Add(encoding);

            // Notify UI of successful capture
            StepCompleted?.Invoke(_currentStep, true);
            StatusChanged?.Invoke($"Step {_currentStep} captured successfully!");

            // Advance to next step or complete training (MainWindow will control step progression)
            if (_currentStep < _totalSteps)
            {
                _currentStep++;
                StatusChanged?.Invoke($"Ready for step {_currentStep} of {_totalSteps}");
            }
            else
            {
                // All steps completed - save worker
                _ = Task.Run(CompleteTrainingAsync);
            }
        }

        private void OnTrainingServiceError(string error)
        {
            if (!IsTrainingActive) return;

            _logger.LogError("Face training service error during step {Step}: {Error}", _currentStep, error);
            StepCompleted?.Invoke(_currentStep, false);
            ErrorOccurred?.Invoke($"Step {_currentStep} failed: {error}");
        }

        private async Task CompleteTrainingAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_workerName))
                {
                    ErrorOccurred?.Invoke("Worker name is missing");
                    return;
                }

                _logger.LogInformation("Completing training for {WorkerName} with {Count} encodings",
                    _workerName, _capturedEncodings.Count);

                StatusChanged?.Invoke("Saving worker data...");

                // Generate next worker ID
                var existingWorkers = await _storageService.GetAllWorkersAsync();
                int nextId = existingWorkers.Count > 0 ? existingWorkers.Max(w => w.Id) + 1 : 1;

                // Create worker with face encodings
                var worker = new Worker
                {
                    Id = nextId,
                    Name = _workerName,
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    FaceEncodings = new List<byte[]>(_capturedEncodings)
                };

                // Save to storage
                await _storageService.SaveWorkerAsync(worker);

                _logger.LogInformation("Worker {WorkerName} saved successfully with ID {Id}",
                    _workerName, nextId);

                StatusChanged?.Invoke($"Worker '{_workerName}' saved successfully!");

                // Reset state
                ResetTrainingState();

                // Notify completion
                TrainingCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing training for worker {WorkerName}", _workerName);
                ErrorOccurred?.Invoke($"Failed to save worker: {ex.Message}");
            }
        }

        public void CancelTraining()
        {
            if (!IsTrainingActive) return;

            _logger.LogInformation("Training workflow cancelled for worker {WorkerName}", _workerName);
            StatusChanged?.Invoke("Training cancelled");

            ResetTrainingState();
        }

        private void ResetTrainingState()
        {
            IsTrainingActive = false;
            _currentStep = 1;
            _capturedEncodings.Clear();
            _workerName = null;
        }

        public (int current, int total, List<bool> completed) GetProgress()
        {
            var completed = new List<bool>();
            for (int i = 1; i <= _totalSteps; i++)
            {
                completed.Add(i < _currentStep || (i == _currentStep && _capturedEncodings.Count >= i));
            }

            return (_currentStep, _totalSteps, completed);
        }
    }
}