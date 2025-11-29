// File: Mobile/NewwaysAdmin.Mobile/Services/Connectivity/ConnectionState.cs
using Microsoft.Extensions.Logging;

namespace NewwaysAdmin.Mobile.Services.Connectivity
{
    /// <summary>
    /// Simple observable connection state - any component can check or subscribe to changes
    /// </summary>
    public class ConnectionState
    {
        private readonly ILogger<ConnectionState> _logger;
        private bool _isOnline = false;
        private DateTime? _lastChecked = null;
        private DateTime? _lastOnline = null;

        public ConnectionState(ILogger<ConnectionState> logger)
        {
            _logger = logger;
        }

        // ===== STATE =====

        public bool IsOnline
        {
            get => _isOnline;
            private set
            {
                if (_isOnline != value)
                {
                    _isOnline = value;
                    _logger.LogInformation("Connection state changed: {State}", value ? "ONLINE" : "OFFLINE");

                    if (value)
                        _lastOnline = DateTime.UtcNow;

                    // Fire event
                    OnConnectionChanged?.Invoke(this, value);
                }
            }
        }

        public DateTime? LastChecked => _lastChecked;
        public DateTime? LastOnline => _lastOnline;

        // ===== EVENT =====

        public event EventHandler<bool>? OnConnectionChanged;

        // ===== METHODS =====

        public void SetOnline()
        {
            _lastChecked = DateTime.UtcNow;
            IsOnline = true;
        }

        public void SetOffline()
        {
            _lastChecked = DateTime.UtcNow;
            IsOnline = false;
        }

        public void SetState(bool isOnline)
        {
            _lastChecked = DateTime.UtcNow;
            IsOnline = isOnline;
        }
    }
}