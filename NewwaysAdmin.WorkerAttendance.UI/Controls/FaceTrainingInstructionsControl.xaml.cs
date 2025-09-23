// File: NewwaysAdmin.WorkerAttendance.UI/Controls/FaceTrainingInstructionsControl.xaml.cs
// Purpose: Standalone visual face training instructions component logic

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class FaceTrainingInstructionsControl : UserControl
    {
        private int _currentStep = 1;
        private const int _totalSteps = 4;

        // Events for parent window communication
        public event Action<int>? CaptureRequested;
        public event Action? TrainingCompleted;
        public event Action? TrainingCancelled;

        public FaceTrainingInstructionsControl()
        {
            InitializeComponent();
            ResetToStep1();
        }

        /// <summary>
        /// Reset training to step 1
        /// </summary>
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

        /// <summary>
        /// Mark current step as captured and advance
        /// </summary>
        public void OnStepCaptured(bool success)
        {
            if (!success)
            {
                CurrentInstruction.Text = "Capture failed. Please try again.";
                return;
            }

            // Mark current step as complete
            switch (_currentStep)
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
                    ShowCompletion();
                    return;
            }

            // Advance to next step
            _currentStep++;

            if (_currentStep <= _totalSteps)
            {
                ActivateCurrentStep();
                UpdateProgress();
            }
        }

        /// <summary>
        /// Show error message
        /// </summary>
        public void ShowError(string message)
        {
            CurrentInstruction.Text = $"Error: {message}";
        }

        /// <summary>
        /// Update status message
        /// </summary>
        public void UpdateStatus(string message)
        {
            CurrentInstruction.Text = message;
        }

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
            ProgressText.Text = $"Step {_currentStep} of {_totalSteps}";
        }

        private void ShowCompletion()
        {
            CompletionBorder.Visibility = Visibility.Visible;
            ProgressText.Text = "Complete!";
            CurrentInstruction.Text = "All face angles captured successfully!";
            TrainingCompleted?.Invoke();
        }

        #endregion

        #region Button Click Handlers

        private void CaptureStep1_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing straight pose...";
            CaptureRequested?.Invoke(1);
        }

        private void CaptureStep2_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing left pose...";
            CaptureRequested?.Invoke(2);
        }

        private void CaptureStep3_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing right pose...";
            CaptureRequested?.Invoke(3);
        }

        private void CaptureStep4_Click(object sender, RoutedEventArgs e)
        {
            CurrentInstruction.Text = "Capturing upward pose...";
            CaptureRequested?.Invoke(4);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Ask for confirmation before cancelling
            var result = MessageBox.Show(
                "Are you sure you want to cancel face training?\nAll progress will be lost.",
                "Cancel Training",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                CurrentInstruction.Text = "Training cancelled";
                TrainingCancelled?.Invoke();
            }
        }

        #endregion
    }
}