// File: NewwaysAdmin.WorkerAttendance.UI/Controls/WorkerManagementControl.xaml.cs
// Purpose: Collapsible worker management with expand/collapse functionality

using System.Windows;
using System.Windows.Controls;

namespace NewwaysAdmin.WorkerAttendance.UI.Controls
{
    public partial class WorkerManagementControl : UserControl
    {
        // Events to communicate with parent window
        public event Action? TrainWorkerRequested;
        public event Action? ManageWorkersRequested;

        private bool _isExpanded = false;

        public WorkerManagementControl()
        {
            InitializeComponent();
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            ExpandManagement();
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseManagement();
        }

        private void TrainWorkerButton_Click(object sender, RoutedEventArgs e)
        {
            TrainWorkerRequested?.Invoke();
        }

        private void ManageWorkersButton_Click(object sender, RoutedEventArgs e)
        {
            ManageWorkersRequested?.Invoke();
        }

        private void ExpandManagement()
        {
            _isExpanded = true;
            ExpandButton.Visibility = Visibility.Collapsed;
            ManagementOptions.Visibility = Visibility.Visible;
        }

        private void CollapseManagement()
        {
            _isExpanded = false;
            ManagementOptions.Visibility = Visibility.Collapsed;
            ExpandButton.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Force collapse (useful when switching to training mode)
        /// </summary>
        public void ForceCollapse()
        {
            CollapseManagement();
        }

        /// <summary>
        /// Check if currently expanded
        /// </summary>
        public bool IsExpanded => _isExpanded;
    }
}