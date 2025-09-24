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
        private bool _isAuthenticated = false; // Track if user is already authenticated

        public WorkerManagementControl()
        {
            InitializeComponent();
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            // Check password before expanding
            if (!_isAuthenticated)
            {
                var passwordWindow = new PasswordWindow();
                passwordWindow.Owner = Window.GetWindow(this);

                bool? result = passwordWindow.ShowDialog();

                if (result == true && passwordWindow.IsAuthenticated)
                {
                    _isAuthenticated = true;
                    ExpandManagement();
                }
                else
                {
                    MessageBox.Show("Access denied", "Authentication Failed");
                    return;
                }
            }
            else
            {
                // Already authenticated - just expand
                ExpandManagement();
            }
        }

        private void DoneButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseManagement();
            // Reset authentication when closing - user will need to re-authenticate next time
            _isAuthenticated = false;
        }

        private void TrainWorkerButton_Click(object sender, RoutedEventArgs e)
        {
            // No password check needed here - already authenticated at expand
            TrainWorkerRequested?.Invoke();
        }

        private void ManageWorkersButton_Click(object sender, RoutedEventArgs e)
        {
            // No password check needed here - already authenticated at expand
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
            // Reset authentication when forced to collapse
            _isAuthenticated = false;
        }

        /// <summary>
        /// Check if currently expanded
        /// </summary>
        public bool IsExpanded => _isExpanded;
    }
}