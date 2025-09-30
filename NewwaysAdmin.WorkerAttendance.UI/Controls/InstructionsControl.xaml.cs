// File: NewwaysAdmin.WorkerAttendance.UI/Controls/InstructionsControl.xaml.cs
// Purpose: Main instructions display component
// CRITICAL FIX: Removed ALL event handler wiring - FaceTrainingInstructions manages itself

using System.Windows;
using System.Windows.Controls;
using NewwaysAdmin.WorkerAttendance.UI;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class InstructionsControl : UserControl
    {
        private VideoFeedService? _videoService;

        // NO EVENTS EXPOSED - FaceTrainingInstructions handles everything internally

        public InstructionsControl()
        {
            InitializeComponent();
        }

        public void Initialize(VideoFeedService videoService)
        {
            _videoService = videoService;

            if (_videoService != null)
            {
                _videoService.WorkerRecognized += OnWorkerRecognized;
            }
        }

        private void OnWorkerRecognized(string workerName)
        {
            Dispatcher.Invoke(() =>
            {
                WorkerConfirmation.SetWorkerInfo(workerName);
                WorkerConfirmation.Visibility = Visibility.Visible;
                NormalInstructions.Visibility = Visibility.Collapsed;
            });
        }

        public void ShowWorkerConfirmation(string workerName)
        {
            WorkerConfirmation.Visibility = Visibility.Visible;
            WorkerConfirmation.SetWorkerInfo(workerName);
            NormalInstructions.Visibility = Visibility.Collapsed;
        }

        public void HideWorkerConfirmation()
        {
            WorkerConfirmation.Visibility = Visibility.Collapsed;
            WorkerConfirmation.Clear();

            if (TrainingInstructions.Visibility != Visibility.Visible)
            {
                NormalInstructions.Visibility = Visibility.Visible;
            }
        }

        public void Cleanup()
        {
            if (_videoService != null)
            {
                _videoService.WorkerRecognized -= OnWorkerRecognized;
                _videoService = null;
            }

            // CRITICAL: Cleanup face training control
            FaceTrainingInstructions?.Cleanup();
        }

        public void ShowNormalMode()
        {
            InstructionsHeader.Text = "Instructions";
            NormalInstructions.Visibility = Visibility.Visible;
            TrainingInstructions.Visibility = Visibility.Collapsed;
            HideWorkerConfirmation();
        }

        public void ShowTrainingMode()
        {
            InstructionsHeader.Text = "Worker Registration";
            NormalInstructions.Visibility = Visibility.Collapsed;
            TrainingInstructions.Visibility = Visibility.Visible;
            BasicTrainingInstructions.Visibility = Visibility.Visible;
            FaceTrainingInstructionsPanel.Visibility = Visibility.Collapsed;
            CurrentTrainingInstruction.Text = "Enter worker information and start face training";
            HideWorkerConfirmation();
        }

        /// <summary>
        /// CRITICAL FIX: Just show the control - NO EVENT HANDLERS!
        /// FaceTrainingInstructions subscribes to workflow events internally via StartTrainingForWorker()
        /// </summary>
        public void StartFaceTraining()
        {
            if (FaceTrainingInstructions == null)
            {
                CurrentTrainingInstruction.Text = "ERROR: FaceTrainingInstructions is null!";
                return;
            }

            InstructionsHeader.Text = "Face Training in Progress";
            BasicTrainingInstructions.Visibility = Visibility.Collapsed;
            FaceTrainingInstructionsPanel.Visibility = Visibility.Visible;
            FaceTrainingInstructions.Visibility = Visibility.Visible;

            // CRITICAL: Just reset the visual state
            // DON'T wire up any event handlers here!
            // The FaceTrainingInstructions control will subscribe to workflow when StartTrainingForWorker() is called
            FaceTrainingInstructions.ResetToStep1();

            CurrentTrainingInstruction.Text = "Follow the visual steps below";
        }

        public void UpdateTrainingInstruction(string instruction)
        {
            CurrentTrainingInstruction.Text = instruction;
        }

        public void UpdateTrainingStep(string step)
        {
            CurrentTrainingInstruction.Text = step;
        }

        /// <summary>
        /// NO-OP: FaceTrainingInstructions gets step updates directly from workflow
        /// </summary>
        public void OnStepCaptured(int stepNumber, bool success)
        {
            // FaceTrainingInstructions subscribes to workflow.StepCompleted directly
            // This method kept for backwards compatibility but does nothing
        }

        public void UpdateTrainingStatus(string status)
        {
            CurrentTrainingInstruction.Text = status;
        }
    }
}