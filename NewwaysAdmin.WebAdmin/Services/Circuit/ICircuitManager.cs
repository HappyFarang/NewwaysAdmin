// ICircuitManager.cs
namespace NewwaysAdmin.WebAdmin.Services.Circuit;

public interface ICircuitManager
{
    string? GetCurrentCircuitId();
    void SetCircuitId(string circuitId);
    void ClearCircuitId(string circuitId);
    bool IsCircuitActive(string circuitId);
}

// CircuitManager.cs
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace NewwaysAdmin.WebAdmin.Services.Circuit;

public class CircuitManager : ICircuitManager
{
    private string? _currentCircuitId;
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
}