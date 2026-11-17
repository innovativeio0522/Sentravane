using System.ComponentModel.DataAnnotations;

namespace Sentravane.Server.Web.Data.Entities;

public class Policy
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string TenantId { get; set; } = string.Empty;

    public bool CanaryTripwiresEnabled { get; set; } = true;

    public bool WfpQuarantineEnabled { get; set; } = true;

    public int RapidBurstThresholdCount { get; set; } = 40;

    public int RapidBurstWindowSeconds { get; set; } = 5;

    public bool CaptureMinidumpOnIncident { get; set; } = true;

    public bool UsbEnforcementEnabled { get; set; } = false;

    public string UsbWhitelistJson { get; set; } = "[]";

    public int OverrideTimeoutSeconds { get; set; } = 20;

    public string CanaryPathsJson { get; set; } = "[\"Documents\\\\!00_sentravane_canary.docx\",\"Desktop\\\\!00_sentravane_canary.xlsx\"]";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
