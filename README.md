# Sentravane Fleet Platform

Driverless Windows endpoint security platform for enterprise fleets.

## Architecture
- `docs/designs/`: Architectural specs, design diagrams, and sitemap.
- `docs/reviews/`: Goldfish review reports, audit logs, and remediation plans.
- `src/`: Core implementation:
  - `Sentravane.Shared/`: Domain models and IPC protocols.
  - `Sentravane.Sensor.Service/`: Windows Background Service (canaries, ETW, WFP).
  - `Sentravane.Server.Web/`: SignalR fleet management hub and REST API.
- `tests/`: xUnit / integration test suites.

## Getting Started
Build the solution using Visual Studio 2022 or via CLI:
```powershell
dotnet build Sentravane.sln
```
