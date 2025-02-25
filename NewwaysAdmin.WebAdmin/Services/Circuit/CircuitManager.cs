// CircuitManager.cs
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace NewwaysAdmin.WebAdmin.Services.Circuit;

public class CircuitManager : ICircuitManager
{
    private string? _currentCircuitId;
    private readonly ILogger<CircuitManager> _logger;

    public CircuitManager(ILogger<CircuitManager> logger)
    {
        _logger = logger;
    }

    public string? GetCurrentCircuitId() => _currentCircuitId;

    public void SetCircuitId(string circuitId)
    {
        _currentCircuitId = circuitId;
        _logger.LogInformation("Circuit ID set: {CircuitId}", circuitId);
    }
}