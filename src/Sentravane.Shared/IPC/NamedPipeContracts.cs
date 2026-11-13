using Sentravane.Shared.Models;

namespace Sentravane.Shared.IPC;

public enum IpcCommand
{
    GetStatus = 1,
    ListCanaries = 2,
    QuarantineEndpoint = 3,
    UnquarantineEndpoint = 4,
    TriggerDiagnostics = 5
}

public record IpcRequest
{
    public IpcCommand Command { get; init; }
    public string? Arguments { get; init; }
}

public record IpcResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? PayloadJson { get; init; }
}

public record IPCMessage
{
    public required string Type { get; init; }
    public string? PayloadJson { get; init; }
}

public record OverrideRequest
{
    public required string IncidentId { get; init; }
    public required string ProcessName { get; init; }
    public int Pid { get; init; }
    public int TimeoutSeconds { get; init; } = 20;
    public string? Reason { get; init; }
}

public record OverrideDecision
{
    public required string IncidentId { get; init; }
    public bool UserOverridden { get; init; }
    public string? UserComments { get; init; }
    public DateTime DecisionTimeUtc { get; init; } = DateTime.UtcNow;
}
