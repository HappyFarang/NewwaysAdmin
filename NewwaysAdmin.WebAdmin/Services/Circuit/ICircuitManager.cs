// ICircuitManager.cs
public interface ICircuitManager
{
    string? GetCurrentCircuitId();
    void SetCircuitId(string circuitId);
    void ClearCircuitId(string circuitId);
    bool IsCircuitActive(string circuitId);

    // ADD THESE:
    void MarkAsAuthenticated();
    bool IsAuthenticated();
}