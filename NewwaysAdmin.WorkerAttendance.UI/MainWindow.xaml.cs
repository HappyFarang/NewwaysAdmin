// File: NewwaysAdmin.WorkerAttendance.UI/MainWindow.xaml.cs
// Purpose: Main window code-behind for handling Arduino events and UI updates

using System.Windows;
using NewwaysAdmin.WorkerAttendance.Arduino;

namespace NewwaysAdmin.WorkerAttendance.UI
{
    public partial class MainWindow : Window
    {
        private ArduinoService _arduinoService;

        public MainWindow()
        {
            InitializeComponent();
            
            _arduinoService = new ArduinoService();
            _arduinoService.ButtonPressed += OnButtonPressed;
            _arduinoService.StatusChanged += OnStatusChanged;
            
            // Try to connect to Arduino
            _arduinoService.TryConnect();
        }

        private void OnButtonPressed(string buttonType)
        {
            // This runs on background thread, so we need to invoke on UI thread
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Button pressed: {buttonType}";
                
                if (buttonType == "SIGN_IN")
                {
                    StatusText.Text = "Starting face scan...";
                    // TODO: Start Python face detection
                }
                else if (buttonType == "CONFIRM")
                {
                    StatusText.Text = "Confirmed!";
                    // TODO: Process confirmation
                }
            });
        }

        private void OnStatusChanged(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _arduinoService.Disconnect();
            base.OnClosed(e);
        }
    }
}