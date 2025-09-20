// File: NewwaysAdmin.WorkerAttendance.UI/Controls/InstructionsControl.xaml.cs
// Purpose: Dynamic instructions component that adapts to application mode

using System.Windows;
using System.Windows.Controls;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class InstructionsControl : UserControl
    {
        public InstructionsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Switch to normal attendance scanning mode
        /// </summary>
        public void ShowNormalMode()
        {
            InstructionsHeader.Text = "Instructions";
            NormalInstructions.Visibility = Visibility.Visible;
            TrainingInstructions.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Switch to worker training mode
        /// </summary>
        public void ShowTrainingMode()
        {
            InstructionsHeader.Text = "Worker Training";
            NormalInstructions.Visibility = Visibility.Collapsed;
            TrainingInstructions.Visibility = Visibility.Visible;
            CurrentTrainingInstruction.Text = "";
        }

        /// <summary>
        /// Update the current training instruction (e.g., "Look left", "Look right")
        /// </summary>
        public void UpdateTrainingInstruction(string instruction)
        {
            CurrentTrainingInstruction.Text = instruction;
        }

        /// <summary>
        /// Update the training step description
        /// </summary>
        public void UpdateTrainingStep(string step)
        {
            TrainingStepText.Text = step;
        }
    }
}