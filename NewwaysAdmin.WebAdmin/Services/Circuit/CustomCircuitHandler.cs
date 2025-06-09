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
        try
        {
            _logger.LogInformation("Circuit opened: {CircuitId}", circuit.Id);
            _circuitManager.SetCircuitId(circuit.Id);
            return base.OnCircuitOpenedAsync(circuit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnCircuitOpenedAsync for circuit {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }
    }

    public override Task OnCircuitClosedAsync(Microsoft.AspNetCore.Components.Server.Circuits.Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Circuit closed: {CircuitId}", circuit.Id);
            // UPDATED: Now using ClearCircuitId instead of just logging
            _circuitManager.ClearCircuitId(circuit.Id);
            return base.OnCircuitClosedAsync(circuit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnCircuitClosedAsync for circuit {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }
    }

    // OPTIONAL: Add these additional overrides for better connection handling
    public override Task OnConnectionDownAsync(Microsoft.AspNetCore.Components.Server.Circuits.Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Connection down for circuit: {CircuitId}", circuit.Id);
            return base.OnConnectionDownAsync(circuit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnectionDownAsync for circuit {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }
    }

    public override Task OnConnectionUpAsync(Microsoft.AspNetCore.Components.Server.Circuits.Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Connection restored for circuit: {CircuitId}", circuit.Id);
            return base.OnConnectionUpAsync(circuit, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnConnectionUpAsync for circuit {CircuitId}", circuit.Id);
            return Task.CompletedTask;
        }
    }
}