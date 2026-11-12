namespace Sentravane.Shared.Models;

public enum IncidentReason
{
    CanaryModification = 1,
    RapidEncryptionAnomaly = 2,
    UnauthorizedUsb = 3,
    SuspiciousProcessCreation = 4,
    ManualQuarantine = 5
}

public enum ContainmentStatus
{
    Normal = 0,
    Suspended = 1,
    Quarantined = 2,
    Remediated = 3
}

public enum EndpointStatus
{
    Online = 1,
    Offline = 2,
    Quarantined = 3,
    Decommissioned = 4
}

public enum UsbPolicyAction
{
    Allow = 1,
    BlockAndEject = 2,
    ReadOnly = 3
}

