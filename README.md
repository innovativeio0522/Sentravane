# Sentravane Fleet Platform

Enterprise-grade driverless Windows endpoint security and ransomware containment platform built with .NET 8 LTS and C# 12.

---

## Overview

Sentravane provides proactive ransomware tripwires, high-speed process suspension, and dynamic network isolation for enterprise Windows endpoints without requiring custom third-party kernel drivers.

### Key Capabilities
- **Canary Decoy Tripwires**: Zero-byte and formatted decoy files placed in active user profiles (`Documents`, `Desktop`) discovered dynamically in Session 0 via the registry (`ProfileList`).
- **Real-Time ETW Kernel Tracing**: Subscribes to Microsoft Windows Kernel File I/O events (`DiskFileIO`, `FileIOInit`, `Process`) via `Microsoft.Diagnostics.Tracing.TraceEvent`.
- **Atomic Process Suspension**: Immediate process tree halting using low-level NT APIs (`NtSuspendProcess`, `CreateToolhelp32Snapshot`).
- **Dynamic WFP Network Isolation**: Ephemeral outbound socket quarantine via Windows Filtering Platform (`fwpuclnt.dll` with `FWPM_SESSION_FLAG_DYNAMIC`), ensuring fail-open safety across reboots.
- **Forensic Telemetry Capture**: Collects offending process metadata, SHA256 binary signatures, and active TCP socket connections via IP Helper APIs (`iphlpapi.dll`).
- **Encrypted Offline Store**: Local DPAPI-encrypted SQLite queue (`Microsoft.Data.Sqlite`) for offline events with automatic drainage upon reconnect.
- **Central Fleet Console & Hub**: ASP.NET Core SignalR WebSocket hub and Web API with EF Core SQLite and multi-tenant endpoints.

---

## Repository Structure

```text
Sentravane/
├── docs/
│   ├── designs/            # Architectural specifications, sequence diagrams, and sitemap
│   └── reviews/            # Design review reports, audit findings, and triage logs
├── src/
│   ├── Sentravane.Shared/         # Domain models, IPC contracts, and SignalR interfaces
│   ├── Sentravane.Server.Web/     # Fleet Management Web API + SignalR Fleet Hub
│   └── Sentravane.Sensor.Service/ # Windows Background Service (Session 0 NT AUTHORITY\SYSTEM)
├── tests/
│   └── Sentravane.Tests/          # xUnit unit and integration test suite
└── Sentravane.slnx                # Solution manifest
```

---

## Prerequisites

- **OS**: Windows 10 / 11 / Windows Server 2019+
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Privileges**: Administrator privileges required for Kernel ETW tracing and WFP quarantine features.

---

## Getting Started

### 1. Build the Solution
```powershell
dotnet build Sentravane.slnx
```

### 2. Run the Test Suite
```powershell
dotnet test Sentravane.slnx
```

### 3. Start the Central Fleet Server
```powershell
cd src/Sentravane.Server.Web
dotnet run
```
The Fleet Hub and Web API will listen on `http://localhost:5000` (or `https://localhost:5001`).

### 4. Run or Install the Windows Sensor
To run the sensor directly in development mode (with console logging):
```powershell
cd src/Sentravane.Sensor.Service
dotnet run
```

To register as a Windows Service under the Service Control Manager:
```powershell
sc.exe create SentravaneSensor binPath= "F:\path\to\Sentravane.Sensor.Service.exe" start= auto
sc.exe start SentravaneSensor
```

---

## License
Proprietary / Enterprise. All rights reserved.
