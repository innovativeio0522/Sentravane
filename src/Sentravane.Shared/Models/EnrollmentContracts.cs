namespace Sentravane.Shared.Models;

public record EnrollmentRequest
{
    public required string EnrollmentToken { get; init; }
    public required string HardwareUuid { get; init; }
    public required string MacAddress { get; init; }
    public required string Hostname { get; init; }
    public required string OsVersion { get; init; }
    public string SensorVersion { get; init; } = "1.0.0";
}

public record EnrollmentResponse
{
    public required string EndpointId { get; init; }
    public required string TenantId { get; init; }
    public required string AgentSecret { get; init; }
    public required string WebSocketUrl { get; init; }
    public int HeartbeatIntervalSeconds { get; init; } = 30;
    public EndpointPolicy? Policies { get; init; }
}

public record SensorVersionManifest
{
    public string LatestVersion { get; init; } = "1.0.0";
    public string DownloadUrl { get; init; } = string.Empty;
    public string Sha256Checksum { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public bool IsMandatory { get; init; }
}

