// File: NewwaysAdmin.WorkerAttendance.UI/PasswordWindow.xaml.cs
// Purpose: Simple password validation for admin access

using System.Windows;

namespace NewwaysAdmin.WorkerAttendance.UI
{
    public partial class PasswordWindow : Window
    {
        private const string ADMIN_PASSWORD = "AirAdmin";

        public bool IsAuthenticated { get; private set; } = false;

        public PasswordWindow()
        {
            InitializeComponent();
            PasswordInput.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            ValidatePassword();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsAuthenticated = false;
            DialogResult = false;
            Close();
        }

        private void ValidatePassword()
        {
            string enteredPassword = PasswordInput.Password;

            if (enteredPassword == ADMIN_PASSWORD)
            {
                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                IsAuthenticated = false;
                ErrorMessage.Text = "Incorrect password. Try again.";
                PasswordInput.Password = "";
                PasswordInput.Focus();
            }
        }
    }
}