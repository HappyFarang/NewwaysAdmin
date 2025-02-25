// CustomCircuitHandler.cs
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace NewwaysAdmin.WebAdmin.Services.Circuit;

public class CustomCircuitHandler : CircuitHandler
{
    private readonly ILogger<CustomCircuitHandler> _logger;
    private readonly ICircuitManager _circuitManager;

    public CustomCircuitHandler(
        ILogger<CustomCircuitHandler> logger,
        ICircuitManager circuitManager)
    {
        _logger = logger;
        _circuitManager = circuitManager;
    }

    public override Task OnCircuitOpenedAsync(Microsoft.AspNetCore.Components.Server.Circuits.Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit opened: {CircuitId}", circuit.Id);
        _circuitManager.SetCircuitId(circuit.Id);
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(Microsoft.AspNetCore.Components.Server.Circuits.Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit closed: {CircuitId}", circuit.Id);
        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }
}