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

            // Wire up workflow events (remove StepCompleted to avoid double calls)
            _workflowService.StatusChanged += OnWorkflowStatusChanged;
            _workflowService.TrainingCompleted += OnWorkflowTrainingCompleted;
            _workflowService.ErrorOccurred += OnWorkflowError;
        }

        public void StartTrainingForWorker(string workerName)
        {
            if (_workflowService == null)
            {
                CurrentInstruction.Text = "ERROR: Workflow service not initialized";
                return;
            }

            // Start the workflow
            _workflowService.StartTrainingWorkflow(workerName);

            // Reset UI to step 1
            ResetToStep1();
            CurrentInstruction.Text = $"Training {workerName} - Position face straight and click CAPTURE";
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

        public void OnStepCaptured(int stepNumber, bool success) // Use stepNumber parameter
        {
            Dispatcher.Invoke(() =>
            {
                if (!success)
                {
                    CurrentInstruction.Text = $"Capture failed for step {stepNumber}. Please try again.";
                    return;
                }

                // Only process if step matches _currentStep to avoid double updates
                if (stepNumber == _currentStep)
                {
                    // Mark specified step as complete
                    switch (stepNumber)
                    {
                        case 1:
                            CompleteStep(Step1Border, Step1Status);
                            break;
                        case 2:
                            CompleteStep(Step2Border, Step2Status);
                            break;
                        case 3:
                            CompleteStep(Step3Border, Step3Status);
                            break;
                        case 4:
                            CompleteStep(Step4Border, Step4Status);
                            CompleteTraining();
                            return;
                    }

                    // Advance to next step
                    _currentStep = stepNumber + 1;

                    if (_currentStep <= _totalSteps)
                    {
                        ActivateCurrentStep();
                        UpdateProgress();
                    }
                }
            });
        }

        public void ShowError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = $"Error: {message}";
            });
        }

        public void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = message;
            });
        }

        #region Workflow Event Handlers

        private void OnWorkflowStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = $"Workflow: {status}";
            });
        }

        private void OnWorkflowTrainingCompleted()
        {
            Dispatcher.Invoke(() =>
            {
                CompleteTraining();
                TrainingCompleted?.Invoke();
            });
        }

        private void OnWorkflowError(string error)
        {
            Dispatcher.Invoke(() =>
            {
                CurrentInstruction.Text = $"Workflow Error: {error}";
            });
        }

        #endregion

        #region Private Methods

        private void ResetStep(Border border, Button button, TextBlock status)
        {
            border.Background = new SolidColorBrush(Colors.LightGray);
            border.BorderBrush = new SolidColorBrush(Colors.Gray);
            border.BorderThickness = new Thickness(1);
            button.Background = new SolidColorBrush(Colors.LightGray);
            button.Foreground = new SolidColorBrush(Colors.Black);
            button.IsEnabled = false;
            status.Text = "";
        }

        private void ActivateStep(Border border, Button button)
        {
            border.Background = new SolidColorBrush(Colors.LightBlue);
            border.BorderBrush = new SolidColorBrush(Colors.Blue);
            border.BorderThickness = new Thickness(2);
            button.Background = new SolidColorBrush(Colors.Orange);
            button.Foreground = new SolidColorBrush(Colors.White);
            button.IsEnabled = true;
        }

        private void CompleteStep(Border border, TextBlock status)
        {
            border.Background = new SolidColorBrush(Colors.LightGreen);
            border.BorderBrush = new SolidColorBrush(Colors.Green);
            border.BorderThickness = new Thickness(2);
            status.Text = "✓ Done";
        }

        private void ActivateCurrentStep()
        {
            switch (_currentStep)
            {
                case 2:
                    ActivateStep(Step2Border, CaptureStep2Button);
                    CurrentInstruction.Text = "Turn head to the left and click CAPTURE";
                    break;
                case 3:
                    ActivateStep(Step3Border, CaptureStep3Button);
                    CurrentInstruction.Text = "Turn head to the right and click CAPTURE";
                    break;
                case 4:
                    ActivateStep(Step4Border, CaptureStep4Button);
                    CurrentInstruction.Text = "Look slightly upward and click CAPTURE";
                    break;
            }
        }

        private void UpdateProgress()
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = $"Step {_currentStep} of {_totalSteps}";
            });
        }

        private void CompleteTraining()
        {
            Dispatcher.Invoke(() =>
            {
                CompletionBorder.Visibility = Visibility.Visible;
                ProgressText.Text = "Complete!";
                CurrentInstruction.Text = "All face angles captured successfully!";
                // Unsubscribe from workflow events to prevent further updates
                if (_workflowService != null)
                {
                    _workflowService.StatusChanged -= OnWorkflowStatusChanged;
                    _workflowService.TrainingCompleted -= OnWorkflowTrainingCompleted;
                    _workflowService.ErrorOccurred -= OnWorkflowError;
                }
                Visibility = Visibility.Collapsed; // Hide the entire control
                TrainingCompleted?.Invoke();
            });
        }

        #endregion

        #region Button Click Handlers

        private async void CaptureStep1_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing straight pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(1);
            }
            else
            {
                CaptureRequested?.Invoke(1);
            }
        }

        private async void CaptureStep2_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing left pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(2);
            }
            else
            {
                CaptureRequested?.Invoke(2);
            }
        }

        private async void CaptureStep3_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing right pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(3);
            }
            else
            {
                CaptureRequested?.Invoke(3);
            }
        }

        private async void CaptureStep4_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing upward pose...";
            if (_workflowService != null)
            {
                await _workflowService.ProcessCaptureRequestAsync(4);
                CompleteTraining();
            }
            else
            {
                CaptureRequested?.Invoke(4);
                CompleteTraining();
            }
        }

        #endregion
    }
}