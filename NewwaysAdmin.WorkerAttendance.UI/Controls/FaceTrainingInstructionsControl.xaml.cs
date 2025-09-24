// File: NewwaysAdmin.WorkerAttendance.UI/Controls/FaceTrainingInstructionsControl.xaml.cs
// Purpose: Standalone visual face training instructions component logic

using NewwaysAdmin.WorkerAttendance.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class FaceTrainingInstructionsControl : UserControl
    {
        private int _currentStep = 1;
        private const int _totalSteps = 4;

        // Add workflow service reference
        private FaceTrainingWorkflowService? _workflowService;

        // Events for parent window communication
        public event Action<int>? CaptureRequested;
        public event Action? TrainingCompleted;
        public event Action? TrainingCancelled;

        public FaceTrainingInstructionsControl()
        {
            InitializeComponent();
            ResetToStep1();
        }

        public void Initialize(FaceTrainingWorkflowService workflowService)
        {
            _workflowService = workflowService;
            // Don't subscribe here - let SubscribeToWorkflowEvents handle it
        }

        public void StartTrainingForWorker(string workerName)
        {
            if (_workflowService == null)
            {
                CurrentInstruction.Text = "ERROR: Workflow service not initialized";
                return;
            }

            // CRITICAL: Re-subscribe to events in case we unsubscribed after previous training
            SubscribeToWorkflowEvents();

            // Make sure the control is visible (in case it was hidden)
            Visibility = Visibility.Visible;

            // IMPORTANT: Reset UI to ensure clean state
            ResetToStep1();

            // Start the workflow - should be clean thanks to Cleanup() in WorkerRegistrationControl
            _workflowService.StartTrainingWorkflow(workerName);

            CurrentInstruction.Text = $"Training {workerName} - Position face straight and click CAPTURE";
        }

        /// <summary>
        /// Subscribe to workflow events - can be called multiple times safely
        /// </summary>
        private void SubscribeToWorkflowEvents()
        {
            if (_workflowService == null) return;

            // Unsubscribe first to avoid double subscription
            UnsubscribeFromWorkflowEvents();

            // Now subscribe to all relevant events
            _workflowService.StatusChanged += OnWorkflowStatusChanged;
            _workflowService.TrainingCompleted += OnWorkflowTrainingCompleted;
            _workflowService.ErrorOccurred += OnWorkflowError;

            // CRITICAL: Subscribe to StepCompleted to handle individual step progression
            _workflowService.StepCompleted += OnWorkflowStepCompleted;
        }

        /// <summary>
        /// Unsubscribe from workflow events - can be called multiple times safely
        /// </summary>
        private void UnsubscribeFromWorkflowEvents()
        {
            if (_workflowService == null) return;

            _workflowService.StatusChanged -= OnWorkflowStatusChanged;
            _workflowService.TrainingCompleted -= OnWorkflowTrainingCompleted;
            _workflowService.ErrorOccurred -= OnWorkflowError;
            _workflowService.StepCompleted -= OnWorkflowStepCompleted;
        }

        public void ResetToStep1()
        {
            _currentStep = 1;

            // Reset all steps
            ResetStep(Step1Border, CaptureStep1Button, Step1Status);
            ResetStep(Step2Border, CaptureStep2Button, Step2Status);
            ResetStep(Step3Border, CaptureStep3Button, Step3Status);
            ResetStep(Step4Border, CaptureStep4Button, Step4Status);

            // Activate step 1
            ActivateStep(Step1Border, CaptureStep1Button);

            // Hide completion
            CompletionBorder.Visibility = Visibility.Collapsed;

            // Update UI
            UpdateProgress();
            CurrentInstruction.Text = "Position face in camera and click CAPTURE";
        }

        public void OnStepCaptured(int stepNumber, bool success)
        {
            Dispatcher.Invoke(() =>
            {
                // DEBUG: Let's see the call stack and what's calling this
                var stackTrace = new System.Diagnostics.StackTrace(true);
                var caller = stackTrace.GetFrame(1)?.GetMethod()?.Name ?? "Unknown";

                CurrentInstruction.Text = $"DEBUG: OnStepCaptured called with step {stepNumber} from {caller}";

                // Give user time to read the debug message
                System.Threading.Thread.Sleep(1000);

                if (!success)
                {
                    CurrentInstruction.Text = $"Capture failed for step {stepNumber}. Please try again.";
                    return;
                }

                // Mark the completed step as done
                MarkStepComplete(stepNumber);

                // Sync our internal step counter with the workflow service
                _currentStep = stepNumber;

                if (stepNumber < _totalSteps)
                {
                    // Calculate the NEXT step to activate
                    int nextStep = stepNumber + 1;
                    _currentStep = nextStep; // Update internal counter to next step

                    // Activate the next step in the UI
                    ActivateNextStep(nextStep);
                    CurrentInstruction.Text = GetStepInstruction(nextStep);
                }
                else
                {
                    // All steps completed
                    _currentStep = _totalSteps;
                    CompleteTraining();
                }

                UpdateProgress();
            });
        }

        private void CompleteTraining()
        {
            // Show completion UI
            Step4Border.Background = Brushes.LightGreen;
            Step4Status.Text = "✓";
            CompletionBorder.Visibility = Visibility.Visible;
            CurrentInstruction.Text = "All face angles captured successfully!";
        }

        #region Workflow Event Handlers

        private void OnWorkflowStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = status;
            });
        }

        private void OnWorkflowError(string error)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = $"Error: {error}";
            });
        }

        private void OnWorkflowStepCompleted(int stepNumber, bool success)
        {
            Dispatcher.Invoke(() => {
                CurrentInstruction.Text = $"DEBUG: Workflow service says step {stepNumber} completed";
            });

            // Wait a moment so user can see the message
            System.Threading.Tasks.Task.Delay(1000).Wait();

            // This is the key event that drives step progression!
            OnStepCaptured(stepNumber, success);
        }

        private void OnWorkflowTrainingCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = "All face angles captured successfully!";

                // Unsubscribe from workflow events to prevent further updates
                UnsubscribeFromWorkflowEvents();

                // Hide the entire control
                Visibility = Visibility.Collapsed;

                // Notify parent that training is complete
                TrainingCompleted?.Invoke();
            });
        }

        #endregion

        #region Button Click Handlers

        private async void CaptureStep1_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "DEBUG: Step 1 button clicked - calling workflow service";
            if (_workflowService != null)
            {
                CurrentInstruction.Text = "DEBUG: About to call ProcessCaptureRequestAsync(1)";
                await _workflowService.ProcessCaptureRequestAsync(1);
                CurrentInstruction.Text = "DEBUG: ProcessCaptureRequestAsync(1) returned";
            }
            else
            {
                CurrentInstruction.Text = "DEBUG: ERROR - workflow service is null!";
            }
        }

        private async void CaptureStep2_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing left pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(2);
            }
            // REMOVED: CaptureRequested?.Invoke(2) - we only use workflow service now
        }

        private async void CaptureStep3_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing right pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(3);
            }
            // REMOVED: CaptureRequested?.Invoke(3) - we only use workflow service now
        }

        private async void CaptureStep4_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing upward pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(4);
            }
            // REMOVED: CaptureRequested?.Invoke(4) - we only use workflow service now
        }

        #endregion

        #region Helper Methods

        private void ResetStep(Border border, Button button, TextBlock status)
        {
            border.Background = Brushes.LightGray;
            border.BorderBrush = Brushes.Gray;
            border.BorderThickness = new Thickness(1);
            button.IsEnabled = false;
            button.Background = Brushes.LightGray;
            status.Text = "";
        }

        private void ActivateStep(Border border, Button button)
        {
            border.Background = Brushes.LightBlue;
            border.BorderBrush = Brushes.Blue;
            border.BorderThickness = new Thickness(2);
            button.IsEnabled = true;
            button.Background = Brushes.LightBlue;
        }

        private void ActivateNextStep(int stepNumber)
        {
            // First, deactivate all steps to ensure clean state
            DeactivateAllSteps();

            // Then activate only the requested step
            switch (stepNumber)
            {
                case 1:
                    ActivateStep(Step1Border, CaptureStep1Button);
                    break;
                case 2:
                    ActivateStep(Step2Border, CaptureStep2Button);
                    break;
                case 3:
                    ActivateStep(Step3Border, CaptureStep3Button);
                    break;
                case 4:
                    ActivateStep(Step4Border, CaptureStep4Button);
                    break;
            }
        }

        private void DeactivateAllSteps()
        {
            // Deactivate all steps (but don't reset them - keep completed status)
            DeactivateStep(Step1Border, CaptureStep1Button);
            DeactivateStep(Step2Border, CaptureStep2Button);
            DeactivateStep(Step3Border, CaptureStep3Button);
            DeactivateStep(Step4Border, CaptureStep4Button);
        }

        private void DeactivateStep(Border border, Button button)
        {
            // Only deactivate if it's not already marked as complete (green)
            if (border.Background != Brushes.LightGreen)
            {
                border.Background = Brushes.LightGray;
                border.BorderBrush = Brushes.Gray;
                border.BorderThickness = new Thickness(1);
            }
            button.IsEnabled = false;
            if (button.Background != Brushes.LightGreen) // Don't change completed buttons
            {
                button.Background = Brushes.LightGray;
            }
        }

        private void MarkStepComplete(int stepNumber)
        {
            switch (stepNumber)
            {
                case 1:
                    Step1Border.Background = Brushes.LightGreen;
                    Step1Status.Text = "✓";
                    break;
                case 2:
                    Step2Border.Background = Brushes.LightGreen;
                    Step2Status.Text = "✓";
                    break;
                case 3:
                    Step3Border.Background = Brushes.LightGreen;
                    Step3Status.Text = "✓";
                    break;
                case 4:
                    Step4Border.Background = Brushes.LightGreen;
                    Step4Status.Text = "✓";
                    break;
                default:
                    // Ignore invalid step numbers (ghost process protection)
                    return;
            }
        }

        private void UpdateProgress()
        {
            ProgressText.Text = $"Step {_currentStep} of {_totalSteps}";
        }

        private string GetStepInstruction(int step)
        {
            return step switch
            {
                1 => "Position face straight and click CAPTURE",
                2 => "Turn head left and click CAPTURE",
                3 => "Turn head right and click CAPTURE",
                4 => "Tilt head up slightly and click CAPTURE",
                _ => "Follow the steps above"
            };
        }

        #endregion
    }
}