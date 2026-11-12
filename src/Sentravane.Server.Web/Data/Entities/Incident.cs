using System.ComponentModel.DataAnnotations;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Data.Entities;

public class Incident
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string EndpointId { get; set; } = string.Empty;

    public Endpoint? Endpoint { get; set; }

    public IncidentReason Reason { get; set; } = IncidentReason.CanaryModification;

    public string? CanaryPath { get; set; }

    public int OffendingPid { get; set; }

    public string? ProcessName { get; set; }

    public string? CommandLine { get; set; }

    public string? ForensicSnapshotJson { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsResolved { get; set; } = false;
}

