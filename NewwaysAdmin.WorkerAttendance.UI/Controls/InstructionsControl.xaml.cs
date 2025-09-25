// File: NewwaysAdmin.WorkerAttendance.UI/Controls/InstructionsControl.xaml.cs
// Purpose: Clean dynamic instructions component - simplified version

using System.Windows;
using System.Windows.Controls;
using NewwaysAdmin.WorkerAttendance.UI;  // ADD this using for VideoFeedService

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class InstructionsControl : UserControl
    {

        // Events for training workflow
        public event Action<int>? CaptureRequested;
        public event Action? TrainingCompleted;
        public event Action? TrainingCancelled;

        // NEW: Reference to video service for worker recognition
        private VideoFeedService? _videoService;

        public InstructionsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize with video service to listen for worker recognition
        /// </summary>
        public void Initialize(VideoFeedService videoService)
        {
            _videoService = videoService;

            // Subscribe to worker recognition event
            if (_videoService != null)
            {
                _videoService.WorkerRecognized += OnWorkerRecognized;
            }
        }

        /// <summary>
        /// Handle when worker is recognized - show confirmation panel
        /// </summary>
        private void OnWorkerRecognized(string workerName)
        {
            Dispatcher.Invoke(() =>
            {
                // Show the worker confirmation panel
                WorkerConfirmation.SetWorkerInfo(workerName);
                WorkerConfirmation.Visibility = Visibility.Visible;

                // Hide normal instructions temporarily
                NormalInstructions.Visibility = Visibility.Collapsed;
            });
        }

        /// <summary>
        /// Hide worker confirmation panel (called when user confirms or cancels)
        /// </summary>
        public void HideWorkerConfirmation()
        {
            WorkerConfirmation.Visibility = Visibility.Collapsed;
            WorkerConfirmation.Clear();

            // Show normal instructions again
            if (TrainingInstructions.Visibility != Visibility.Visible)
            {
                NormalInstructions.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Clean up event subscriptions
        /// </summary>
        public void Cleanup()
        {
            if (_videoService != null)
            {
                _videoService.WorkerRecognized -= OnWorkerRecognized;
                _videoService = null;
            }
        }

        /// <summary>
        /// Switch to normal attendance scanning mode
        /// </summary>
        public void ShowNormalMode()
        {
            InstructionsHeader.Text = "Instructions";
            NormalInstructions.Visibility = Visibility.Visible;
            TrainingInstructions.Visibility = Visibility.Collapsed;

            // Hide confirmation panel when switching modes
            HideWorkerConfirmation();
        }

        /// <summary>
        /// Switch to worker training mode (registration form)
        /// </summary>
        public void ShowTrainingMode()
        {
            InstructionsHeader.Text = "Worker Registration";
            NormalInstructions.Visibility = Visibility.Collapsed;
            TrainingInstructions.Visibility = Visibility.Visible;

            // Show basic instructions, hide face training
            BasicTrainingInstructions.Visibility = Visibility.Visible;
            FaceTrainingInstructionsPanel.Visibility = Visibility.Collapsed;

            CurrentTrainingInstruction.Text = "Enter worker information and start face training";

            // Hide confirmation panel when switching modes
            HideWorkerConfirmation();
        }

        /// <summary>
        /// Start face training workflow (show visual instructions)
        /// </summary>
        public void StartFaceTraining()
        {
            if (FaceTrainingInstructions == null)
            {
                CurrentTrainingInstruction.Text = "ERROR: FaceTrainingInstructions is null!";
                return;
            }

            InstructionsHeader.Text = "Face Training in Progress";

            // Hide basic instructions, show face training
            BasicTrainingInstructions.Visibility = Visibility.Collapsed;
            FaceTrainingInstructionsPanel.Visibility = Visibility.Visible;

            // CRITICAL: Reset the FaceTrainingInstructions control visibility 
            // (in case it was hidden after previous training completion)
            FaceTrainingInstructions.Visibility = Visibility.Visible;

            // Reset and wire up the face training component
            if (FaceTrainingInstructions != null)
            {
                FaceTrainingInstructions.ResetToStep1();

                // CRITICAL: Wire up the events
                FaceTrainingInstructions.CaptureRequested += (step) => {
                    CaptureRequested?.Invoke(step);
                };
                FaceTrainingInstructions.TrainingCompleted += () => {
                    TrainingCompleted?.Invoke();
                };
                FaceTrainingInstructions.TrainingCancelled += () => {
                    TrainingCancelled?.Invoke();
                };
            }

            CurrentTrainingInstruction.Text = "Follow the visual steps below";
        }

        /// <summary>
        /// Update the current training instruction text
        /// </summary>
        public void UpdateTrainingInstruction(string instruction)
        {
            CurrentTrainingInstruction.Text = instruction;
        }

        /// <summary>
        /// Update the training step description (legacy method for compatibility)
        /// </summary>
        public void UpdateTrainingStep(string step)
        {
            CurrentTrainingInstruction.Text = step;
        }

        #region Face Training Component Event Handlers

        private void OnCaptureRequested(int stepNumber)
        {
            // Forward the capture request to parent window
            CaptureRequested?.Invoke(stepNumber);
        }

        private void OnTrainingCompleted()
        {
            // Forward training completion to parent window
            TrainingCompleted?.Invoke();
        }

        private void OnTrainingCancelled()
        {
            // Forward training cancellation to parent window
            TrainingCancelled?.Invoke();
        }

        #endregion

        #region Public Methods for Face Training Integration

        /// <summary>
        /// Notify that a training step was captured successfully
        /// </summary>
        public void OnStepCaptured(int stepNumber, bool success)
        {
            FaceTrainingInstructions?.OnStepCaptured(stepNumber, success);
        }

        /// <summary>
        /// Update training status message
        /// </summary>
        public void UpdateTrainingStatus(string status)
        {
            CurrentTrainingInstruction.Text = status;
        }

        #endregion
    }
}