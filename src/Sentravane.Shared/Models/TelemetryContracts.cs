namespace Sentravane.Shared.Models;

public record HeartbeatPayload
{
    public required string EndpointId { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public double CpuPercent { get; init; }
    public long WorkingSetBytes { get; init; }
    public ContainmentStatus ContainmentStatus { get; init; } = ContainmentStatus.Normal;
    public bool CanaryTripwiresIntact { get; init; } = true;
    public int ActiveCanariesMonitored { get; init; }
}

public record ProcessEntry
{
    public int Pid { get; init; }
    public string? ProcessName { get; init; }
    public string? ImagePath { get; init; }
    public string? CommandLine { get; init; }
    public string? UserSid { get; init; }
    public int? ParentPid { get; init; }
    public string? ParentName { get; init; }
    public string? ParentCommandLine { get; init; }
}

public record SocketEntry
{
    public required string LocalIp { get; init; }
    public int LocalPort { get; init; }
    public required string RemoteIp { get; init; }
    public int RemotePort { get; init; }
    public string State { get; init; } = "ESTABLISHED";
}

public record ForensicSnapshotPayload
{
    public required string EndpointId { get; init; }
    public string IncidentId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public IncidentReason TriggerReason { get; init; }
    public string? CanaryFilePath { get; init; }
    public required ProcessEntry OffendingProcess { get; init; }
    public List<SocketEntry> ActiveNetworkSockets { get; init; } = [];
    public List<string> RecentModifiedFiles { get; init; } = [];
    public List<string> ContainmentActionsTaken { get; init; } = [];
}

