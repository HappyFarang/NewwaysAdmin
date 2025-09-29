// File: NewwaysAdmin.WorkerAttendance.Services/FaceTrainingWorkflowService.cs
// Purpose: Manages the 4-pose face training workflow and encoding collection
// FIXED: No auto-save, proper reset, cancel works correctly

using Microsoft.Extensions.Logging;
using NewwaysAdmin.WorkerAttendance.Models;

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
        public event Action? AllStepsCompleted;               // CHANGED: renamed from TrainingCompleted
        public event Action? WorkerSaved;                     // NEW: fired after actual save
        public event Action<string>? ErrorOccurred;

        public bool IsTrainingActive { get; private set; }
        public bool AreAllStepsComplete { get; private set; }  // NEW: track if all 4 steps done
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
            // Write to Debug Output window
            System.Diagnostics.Debug.WriteLine("╔═══════════════════════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine("║ START TRAINING WORKFLOW");
            System.Diagnostics.Debug.WriteLine($"║ BEFORE RESET: _currentStep={_currentStep}, _capturedEncodings.Count={_capturedEncodings.Count}, IsTrainingActive={IsTrainingActive}");

            // FORCE complete reset
            IsTrainingActive = false;
            _currentStep = 1;
            _capturedEncodings.Clear();
            _workerName = null;
            AreAllStepsComplete = false;

            System.Diagnostics.Debug.WriteLine($"║ AFTER RESET: _currentStep={_currentStep}, _capturedEncodings.Count={_capturedEncodings.Count}, IsTrainingActive={IsTrainingActive}");

            // Now set new values
            _workerName = workerName;
            IsTrainingActive = true;

            System.Diagnostics.Debug.WriteLine($"║ NEW WORKER: {workerName}");
            System.Diagnostics.Debug.WriteLine($"║ FINAL STATE: _currentStep={_currentStep}, _capturedEncodings.Count={_capturedEncodings.Count}, IsTrainingActive={IsTrainingActive}");
            System.Diagnostics.Debug.WriteLine("╚═══════════════════════════════════════════════════════════");

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
            System.Diagnostics.Debug.WriteLine("┌───────────────────────────────────────────────────────────");
            System.Diagnostics.Debug.WriteLine("│ ENCODING RECEIVED EVENT");
            System.Diagnostics.Debug.WriteLine($"│ IsTrainingActive: {IsTrainingActive}");
            System.Diagnostics.Debug.WriteLine($"│ _currentStep: {_currentStep}");
            System.Diagnostics.Debug.WriteLine($"│ _capturedEncodings.Count BEFORE: {_capturedEncodings.Count}");

            if (!IsTrainingActive)
            {
                System.Diagnostics.Debug.WriteLine("│ ❌ REJECTED - Training not active!");
                System.Diagnostics.Debug.WriteLine("└───────────────────────────────────────────────────────────");
                return;
            }

            // Store the encoding
            _capturedEncodings.Add(encoding);

            System.Diagnostics.Debug.WriteLine($"│ _capturedEncodings.Count AFTER: {_capturedEncodings.Count}");
            System.Diagnostics.Debug.WriteLine($"│ Encoding size: {encoding.Length} bytes");

            // Notify UI of successful capture
            System.Diagnostics.Debug.WriteLine($"│ ✓ Invoking StepCompleted for step {_currentStep}");
            StepCompleted?.Invoke(_currentStep, true);
            StatusChanged?.Invoke($"Step {_currentStep} captured successfully!");

            // Advance to next step or mark as complete
            if (_currentStep < _totalSteps)
            {
                int oldStep = _currentStep;
                _currentStep++;
                System.Diagnostics.Debug.WriteLine($"│ ➜ ADVANCED from step {oldStep} to step {_currentStep}");
                StatusChanged?.Invoke($"Ready for step {_currentStep} of {_totalSteps}");
            }
            else
            {
                // All steps completed
                AreAllStepsComplete = true;
                System.Diagnostics.Debug.WriteLine("│ ★ ALL STEPS COMPLETE");
                System.Diagnostics.Debug.WriteLine($"│ Total encodings collected: {_capturedEncodings.Count}");
                System.Diagnostics.Debug.WriteLine($"│ Worker: {_workerName}");
                StatusChanged?.Invoke($"All {_totalSteps} poses captured! Ready to save.");
                AllStepsCompleted?.Invoke();
            }

            System.Diagnostics.Debug.WriteLine("└───────────────────────────────────────────────────────────");
        }

        private void OnTrainingServiceError(string error)
        {
            if (!IsTrainingActive) return;

            _logger.LogError("Face training service error during step {Step}: {Error}", _currentStep, error);
            StepCompleted?.Invoke(_currentStep, false);
            ErrorOccurred?.Invoke($"Step {_currentStep} failed: {error}");
        }

        /// <summary>
        /// NEW: Explicitly save the worker - called by UI when user clicks Save/OK
        /// </summary>
        public async Task<bool> SaveWorkerAsync()
        {
            if (!AreAllStepsComplete)
            {
                _logger.LogWarning("Cannot save - not all steps completed");
                ErrorOccurred?.Invoke("Cannot save - training not complete");
                return false;
            }

            try
            {
                if (string.IsNullOrEmpty(_workerName))
                {
                    ErrorOccurred?.Invoke("Worker name is missing");
                    return false;
                }

                _logger.LogInformation("Saving worker {WorkerName} with {Count} encodings",
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

                // Reset state for next worker
                ResetTrainingState();

                // Notify that worker was saved
                WorkerSaved?.Invoke();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving worker {WorkerName}", _workerName);
                ErrorOccurred?.Invoke($"Failed to save worker: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// IMPROVED: Cancel training and clear all data without saving
        /// </summary>
        public void CancelTraining()
        {
            if (!IsTrainingActive && !AreAllStepsComplete)
            {
                _logger.LogInformation("Nothing to cancel - training not active");
                return;
            }

            _logger.LogInformation("Training cancelled for worker {WorkerName} (had {Count} encodings)",
                _workerName ?? "Unknown", _capturedEncodings.Count);

            StatusChanged?.Invoke("Training cancelled - no data saved");

            // Reset everything without saving
            ResetTrainingState();
        }

        /// <summary>
        /// IMPROVED: Clean reset for next worker
        /// </summary>
        private void ResetTrainingState()
        {
            _logger.LogInformation("Resetting training workflow state");

            IsTrainingActive = false;
            AreAllStepsComplete = false;
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