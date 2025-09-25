// File: NewwaysAdmin.WorkerAttendance.UI/Controls/WorkerConfirmationControl.xaml.cs
// Purpose: Code-behind for worker confirmation display component

using System.Windows.Controls;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class WorkerConfirmationControl : UserControl
    {
        public WorkerConfirmationControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Set the worker information to display
        /// </summary>
        public void SetWorkerInfo(string workerName)
        {
            WorkerNameText.Text = workerName;
        }

        /// <summary>
        /// Clear the displayed information (for cleanup)
        /// </summary>
        public void Clear()
        {
            WorkerNameText.Text = "[ชื่อพนักงาน]";
        }
    }
}