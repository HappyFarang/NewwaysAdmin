// CircuitManager.cs
using NewwaysAdmin.WebAdmin.Services.Circuit;

public class CircuitManager : ICircuitManager
{
    private string? _currentCircuitId;
    private bool _isAuthenticated = false; // ADD THIS
    private readonly ILogger<CircuitManager> _logger;
    private readonly object _lock = new object();

    public CircuitManager(ILogger<CircuitManager> logger)
    {
        _logger = logger;
    }

    public string? GetCurrentCircuitId()
    {
        lock (_lock)
        {
            return _currentCircuitId;
        }
    }

    public void SetCircuitId(string circuitId)
    {
        lock (_lock)
        {
            _currentCircuitId = circuitId;
            _logger.LogInformation("Circuit ID set: {CircuitId}", circuitId);
        }
    }

    public void ClearCircuitId(string circuitId)
    {
        lock (_lock)
        {
            if (_currentCircuitId == circuitId)
            {
                _currentCircuitId = null;
                _isAuthenticated = false; // Clear auth flag too
                _logger.LogInformation("Circuit ID cleared: {CircuitId}", circuitId);
            }
        }
    }

    public bool IsCircuitActive(string circuitId)
    {
        lock (_lock)
        {
            return _currentCircuitId == circuitId;
        }
    }

    // ADD THESE TWO METHODS:
    public void MarkAsAuthenticated()
    {
        lock (_lock)
        {
            _isAuthenticated = true;
            _logger.LogInformation("Circuit marked as authenticated: {CircuitId}", _currentCircuitId);
        }
    }

    public bool IsAuthenticated()
    {
        lock (_lock)
        {
            return _isAuthenticated;
        }
    }
}