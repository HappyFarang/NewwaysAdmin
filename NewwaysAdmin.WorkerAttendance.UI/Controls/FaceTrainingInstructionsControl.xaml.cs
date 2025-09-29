// File: NewwaysAdmin.WorkerAttendance.UI/Controls/FaceTrainingInstructionsControl.xaml.cs
// Purpose: Standalone visual face training instructions component logic
// FIXED: Updated event names to match new FaceTrainingWorkflowService

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

            // CRITICAL FIX: Force complete reset - important for second+ worker
            // The control might be hidden with old completion state still visible
            ForceCompleteReset();

            // Re-subscribe to events in case we unsubscribed after previous training
            SubscribeToWorkflowEvents();

            // Make sure the control is visible
            Visibility = Visibility.Visible;

            // Start the workflow
            _workflowService.StartTrainingWorkflow(workerName);

            CurrentInstruction.Text = $"Training {workerName} - Position face straight and click CAPTURE";
        }

        /// <summary>
        /// NEW: Force a complete reset of the control - clears ALL state including completion
        /// </summary>
        public void ForceCompleteReset()
        {
            _currentStep = 1;

            // Reset all step visuals
            ResetStep(Step1Border, CaptureStep1Button, Step1Status);
            ResetStep(Step2Border, CaptureStep2Button, Step2Status);
            ResetStep(Step3Border, CaptureStep3Button, Step3Status);
            ResetStep(Step4Border, CaptureStep4Button, Step4Status);

            // Activate step 1
            ActivateStep(Step1Border, CaptureStep1Button);

            // CRITICAL: Hide completion border (might be visible from previous training)
            CompletionBorder.Visibility = Visibility.Collapsed;

            // Reset progress display
            UpdateProgress();
            CurrentInstruction.Text = "Position face in camera and click CAPTURE";
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
            _workflowService.AllStepsCompleted += OnWorkflowAllStepsCompleted;  // CHANGED: was TrainingCompleted
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
            _workflowService.AllStepsCompleted -= OnWorkflowAllStepsCompleted;  // CHANGED: was TrainingCompleted
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

            // CRITICAL FIX: Always hide completion border when resetting
            CompletionBorder.Visibility = Visibility.Collapsed;

            // Update UI
            UpdateProgress();
            CurrentInstruction.Text = "Position face in camera and click CAPTURE";
        }

        public void OnStepCaptured(int stepNumber, bool success)
        {
            Dispatcher.Invoke(() =>
            {
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
            // This is the key event that drives step progression!
            OnStepCaptured(stepNumber, success);
        }

        /// <summary>
        /// CHANGED: Renamed from OnWorkflowTrainingCompleted
        /// Called when all 4 steps are captured (but not yet saved)
        /// </summary>
        private void OnWorkflowAllStepsCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = "All face angles captured! Click SAVE to store worker.";

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
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(1);
            }
        }

        private async void CaptureStep2_Click(object sender, RoutedEventArgs e)
        {
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(2);
            }
        }

        private async void CaptureStep3_Click(object sender, RoutedEventArgs e)
        {
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(3);
            }
        }

        private async void CaptureStep4_Click(object sender, RoutedEventArgs e)
        {
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(4);
            }
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

        private void MarkStepComplete(int stepNumber)
        {
            switch (stepNumber)
            {
                case 1:
                    Step1Border.Background = Brushes.LightGreen;
                    Step1Status.Text = "✓";
                    CaptureStep1Button.IsEnabled = false;
                    break;
                case 2:
                    Step2Border.Background = Brushes.LightGreen;
                    Step2Status.Text = "✓";
                    CaptureStep2Button.IsEnabled = false;
                    break;
                case 3:
                    Step3Border.Background = Brushes.LightGreen;
                    Step3Status.Text = "✓";
                    CaptureStep3Button.IsEnabled = false;
                    break;
                case 4:
                    Step4Border.Background = Brushes.LightGreen;
                    Step4Status.Text = "✓";
                    CaptureStep4Button.IsEnabled = false;
                    break;
            }
        }

        private void ActivateNextStep(int stepNumber)
        {
            switch (stepNumber)
            {
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

        private string GetStepInstruction(int step)
        {
            return step switch
            {
                1 => "Position face straight and click CAPTURE",
                2 => "Turn face LEFT and click CAPTURE",
                3 => "Turn face RIGHT and click CAPTURE",
                4 => "Look UPWARD and click CAPTURE",
                _ => "Unknown step"
            };
        }

        private void UpdateProgress()
        {
            ProgressText.Text = $"Step {_currentStep} of {_totalSteps}";
        }

        #endregion
    }
}