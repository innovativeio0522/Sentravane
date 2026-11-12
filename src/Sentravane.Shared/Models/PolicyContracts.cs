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
    public bool UsbEnforcementEnabled { get; init; } = true;
    public List<UsbWhitelistEntry> UsbWhitelist { get; init; } = [];
    public int OverrideTimeoutSeconds { get; init; } = 20;
    public List<string> CanaryRelativePaths { get; init; } =
    [
        "Documents\\!00_sentravane_canary.docx",
        "Desktop\\!00_sentravane_canary.xlsx",
        "AppData\\!00_sentravane_canary.pdf"
    ];
}

