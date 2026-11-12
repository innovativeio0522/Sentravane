using System.ComponentModel.DataAnnotations;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Data.Entities;

public class Endpoint
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string TenantId { get; set; } = string.Empty;

    public Tenant? Tenant { get; set; }

    [Required]
    public string HardwareUuid { get; set; } = string.Empty;

    public string MacAddress { get; set; } = string.Empty;

    public string Hostname { get; set; } = string.Empty;

    public string OsVersion { get; set; } = string.Empty;

    public string SensorVersion { get; set; } = "1.0.0";

    public string AgentSecretHash { get; set; } = string.Empty;

    public EndpointStatus Status { get; set; } = EndpointStatus.Online;

    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;

    public string? CurrentConnectionId { get; set; }
}

