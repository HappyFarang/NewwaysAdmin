// ICircuitManager.cs
namespace NewwaysAdmin.WebAdmin.Services.Circuit;

public interface ICircuitManager
{
    string? GetCurrentCircuitId();
    void SetCircuitId(string circuitId);
}