namespace Sentravane.Shared.Models;

public record UsbWhitelistEntry
{
    public required string SerialNumber { get; init; }
    public string? VendorId { get; init; }
    public string? ProductId { get; init; }
    public string? Description { get; init; }
}

public record EndpointPolicy
{
    public bool CanaryTripwiresEnabled { get; init; } = true;
    public bool WfpQuarantineEnabled { get; init; } = true;
    public int RapidBurstThresholdCount { get; init; } = 40;
    public int RapidBurstWindowSeconds { get; init; } = 5;
    public bool CaptureMinidumpOnIncident { get; init; } = true;
    public bool UsbEnforcementEnabled { get; init; } = false;
    public List<UsbWhitelistEntry> UsbWhitelist { get; init; } = [];
    public int OverrideTimeoutSeconds { get; init; } = 20;
    public List<string> CanaryRelativePaths { get; init; } =
    [
        "Documents\\!00_sentravane_canary.docx",
        "Desktop\\!00_sentravane_canary.xlsx",
        "AppData\\!00_sentravane_canary.pdf"
    ];
}
