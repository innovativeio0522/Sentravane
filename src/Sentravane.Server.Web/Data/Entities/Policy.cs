using System.ComponentModel.DataAnnotations;

namespace Sentravane.Server.Web.Data.Entities;

public class Policy
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string TenantId { get; set; } = string.Empty;

    public bool UsbEnforcementEnabled { get; set; } = true;

    public string UsbWhitelistJson { get; set; } = "[]";

    public int OverrideTimeoutSeconds { get; set; } = 20;

    public string CanaryPathsJson { get; set; } = "[\"Documents\\\\!00_sentravane_canary.docx\",\"Desktop\\\\!00_sentravane_canary.xlsx\"]";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

