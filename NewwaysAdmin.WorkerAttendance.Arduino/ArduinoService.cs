// File: NewwaysAdmin.WorkerAttendance.Arduino/ArduinoService.cs
// Purpose: Handles USB serial communication with Arduino for button input detection

using System.IO.Ports;

namespace NewwaysAdmin.WorkerAttendance.Arduino
{
    public class ArduinoService
    {
        private SerialPort? _serialPort;
        private bool _isConnected = false;

        public event Action<string>? ButtonPressed;
        public event Action<string>? StatusChanged;

        public bool TryConnect(string portName = "COM3")
        {
            try
            {
                _serialPort = new SerialPort(portName, 9600);
                _serialPort.DataReceived += OnDataReceived;
                _serialPort.Open();
                _isConnected = true;

                StatusChanged?.Invoke($"Connected to Arduino on {portName}");
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Failed to connect: {ex.Message}");
                return false;
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort?.IsOpen == true)
            {
                string data = _serialPort.ReadLine().Trim();
                ButtonPressed?.Invoke(data);
            }
        }

        public void Disconnect()
        {
            if (_serialPort?.IsOpen == true)
            {
                _serialPort.Close();
            }
            _isConnected = false;
            StatusChanged?.Invoke("Arduino disconnected");
        }
    }
}