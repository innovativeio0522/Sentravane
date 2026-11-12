using Sentravane.Shared.Models;

namespace Sentravane.Shared.Contracts;

/// <summary>
/// Methods invoked on the agent by the server over SignalR WebSockets.
/// </summary>
public interface IFleetHubClient
{
    Task IsolateEndpoint(string incidentId);
    Task LiftQuarantine();
    Task UpdatePolicy(EndpointPolicy policy);
    Task RequestForensics();
    Task AcknowledgeHeartbeat(DateTime serverTimeUtc);
}

/// <summary>
/// Methods invoked on the server by the agent over SignalR WebSockets.
/// </summary>
public interface IFleetHubServer
{
    Task SendHeartbeat(HeartbeatPayload payload);
    Task ReportIncident(ForensicSnapshotPayload snapshot);
    Task ReportUsbEvent(string serialNumber, bool isAuthorized, string actionTaken);
}

