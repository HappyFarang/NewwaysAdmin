// File: NewwaysAdmin.WorkerAttendance.UI/Windows/OTConfirmationWindow.xaml.cs
// Purpose: Code-behind for OT confirmation dialog

using System.Windows;

namespace NewwaysAdmin.WorkerAttendance.UI.Windows
{
    public partial class OTConfirmationWindow : Window
    {
        public bool IsConfirmed { get; private set; } = false;

        public OTConfirmationWindow(string workerName)
        {
            InitializeComponent();
            WorkerNameText.Text = workerName;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}