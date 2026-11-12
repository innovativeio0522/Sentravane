# Sentravane: Driverless Enterprise Fleet Security & Containment Platform

*   **Author:** Sentravane Engineering Team
*   **Status:** Reviewed
*   **Date:** 2026-11-13

--------------------------------------------------------------------------------

## Overview

Sentravane is an enterprise endpoint security, device control, and automated ransomware containment platform built for corporate Windows fleets. It provides proactive visibility, threat containment, and hardware access enforcement across thousands of distributed workstations without relying on kernel-mode drivers. By combining user-mode event streaming (Event Tracing for Windows), deceptive canary tripwires, instant thread suspension, and programmatic network isolation via the Windows Filtering Platform (WFP), Sentravane halts ransomware and unauthorized USB data exfiltration in milliseconds while maintaining unbroken cloud command-and-control connectivity.

## The Problem

Modern enterprises face three acute endpoint security dilemmas:

1. **Ransomware Blast Radius & Lateral Spread:** Once commodity ransomware executes on an endpoint, encryption proceeds at hundreds of files per second. Traditional cloud-dependent endpoint detection and response (EDR) agents suffer from detection-to-cloud roundtrip latency: by the time an alert is processed and a remote isolation command is dispatched, sensitive documents are encrypted and the malware has spread laterally across local network shares.
2. **Kernel Driver Instability and Deployment Friction:** Conventional security platforms rely on Ring-0 kernel drivers (Minifilters, WFP kernel callouts). These introduce severe fleet-wide stability risks (Blue Screens of Death / BSODs), require lengthy Microsoft Hardware Quality Labs (WHQL) and Extended Validation (EV) signing cycles, and complicate automated CI/CD and update pipelines.
3. **Physical Perimeter & Insider Threat Blind Spots:** Unmonitored USB flash drives remain a primary vector for malware introduction and intellectual property theft. Without granular hardware control, organizations struggle to enforce device hygiene without completely blocking legitimate peripherals like keyboards and mice.

Sentravane solves these problems by providing an autonomous, driverless Windows agent that enforces instant local containment and USB device control in user space, governed by a central multi-tenant cloud management plane.

## Goals

*   **Autonomous Sub-Millisecond Containment:** Detect rapid file modifications against canary tripwires and suspend offending process trees (`NtSuspendProcess`) locally within milliseconds, without waiting for cloud round-trip authorization.
*   **Selective Network Quarantine:** Isolate compromised endpoints from all LAN and WAN traffic using programmatic user-mode Windows Filtering Platform (WFP) rules, while explicitly preserving bidirectional TLS communication with the Sentravane cloud console.
*   **Driverless Hardware Control:** Automatically detect, evaluate, and dismount/eject unauthorized USB mass storage devices via Win32 Configuration Manager APIs before file systems are mounted, based on centralized serial whitelists.
*   **Tamper Resistance without Kernel Drivers:** Prevent local administrators and malware from terminating or pausing the background agent by hardening Service Control Manager security descriptors (DACLs).
*   **Frictionless Enterprise Fleet Management:** Support silent mass deployment via standard `.msi` packages and enrollment tokens (compatible with Intune, SCCM, and GPO), reporting real-time telemetry and forensic snapshots over persistent SignalR WebSockets.
*   **Per-Seat Subscription Governance:** Enforce seat-based licensing dynamically at the web server and WebSocket connection layer, enabling seamless device decommissioning and license re-allocation.

## Non-Goals

*   **Kernel-Mode Operations:** Developing or deploying Ring-0 drivers (File System Minifilters, WFP Callout Drivers).
*   **Pre-Execution Binary Blocking:** Preventing unknown binaries from executing prior to process creation (Sentravane acts at behavioral execution and tripwire modification).
*   **Kernel Rootkit & BYOVD Defense:** Detecting or intercepting kernel-level compromises (Bring Your Own Vulnerable Driver attacks) that tamper with the Windows kernel itself.
*   **Consumer Standalone Utility:** Developing an offline, single-machine consumer antivirus tool without fleet orchestration.
*   **Non-Windows OS Support in V1:** Linux and macOS endpoint sensors are strictly excluded from initial release scope.

## Critical User Journeys (CUJs)

### CUJ 1: Silent Fleet Enrollment & Onboarding
*   **Given:** An IT administrator manages 200 corporate laptops enrolled in Microsoft Intune and has purchased a 250-seat Sentravane Business subscription.
*   **When:** The administrator generates an Enrollment Token in the Sentravane Web Console and deploys `SentravaneSensor.msi` silently with the token.
*   **Then:** The Windows Service starts automatically under `NT AUTHORITY\SYSTEM`, captures the hardware fingerprint (Motherboard UUID + MAC address), completes a TLS handshake with `POST /api/v1/fleet/enroll`, secures an issued endpoint secret in Windows DPAPI, consumes 1 license seat, and opens an authenticated SignalR WebSocket connection, displaying green ("Online") on the web console.

### CUJ 2: Zero-Day Ransomware Tripwire & Autonomous Quarantine
*   **Given:** An employee workstation is infected with zero-day ransomware delivered via a malicious email attachment.
*   **When:** The ransomware begins encrypting files and touches a canary decoy file (`!00_sentravane_canary.docx`) in the user's `Documents` folder.
*   **Then:** 
    1. The Windows Service correlates the canary write with ETW file I/O to identify the offending Process ID (PID).
    2. The service immediately calls `NtSuspendProcess`, freezing the process tree.
    3. The service activates WFP firewall rules blocking all LAN/WAN traffic except the Sentravane cloud IP.
    4. The tray companion displays a high-priority containment alert with a 20-second emergency override countdown.
    5. An encrypted forensic snapshot (process lineage, open TCP sockets, affected file list) is transmitted over the open WebSocket to the Web Console, triggering an outbound incident webhook to corporate Slack.

### CUJ 3: Unauthorized USB Ejection & Perimeter Protection
*   **Given:** A corporate laptop has USB Device Control policy enabled, whitelisting only company-issued IronKey USB drives by serial number.
*   **When:** An employee plugs an unauthorized personal USB flash drive into the machine.
*   **Then:** The Windows Service receives the PnP device arrival notification, queries the device hardware ID and serial number, detects that the serial is absent from the local cached whitelist, and invokes `CM_Request_Device_Eject` to dismount the drive before Windows Explorer can mount the volume. The tray UI notifies the user that the device was rejected by security policy.

--------------------------------------------------------------------------------

## Technical Plan (Architecture)

### 1. High-Level Architecture Topology

```
+--------------------------------------------------------------------------------------+
|                                SENTRAVANE CLOUD PLANE                                 |
|                                                                                      |
|  +-----------------------+     +------------------------+     +-------------------+  |
|  |  React Web Console    |     |  ASP.NET Core Web API  |     |   PostgreSQL DB   |  |
|  |  (Tailwind, TypeScript| <-> |  & SignalR Hub         | <-> |  (Fleet Inventory,|  |
|  |   Fleet Dashboard)    |     |  (Telemetry Ingestion) |     |   Policies, Logs) |  |
|  +-----------------------+     +-----------+------------+     +-------------------+  |
|                                            ^                            ^            |
|                                            | WSS / TLS                  |            |
|                                            v                            v            |
|                                +------------------------+     +-------------------+  |
|                                |   Redis Backplane      |     |  Outbound Webhook |  |
|                                |  (Connection Registry, | <-> |  Dispatcher       |  |
|                                |   Socket Routing)      |     |  (Slack, SIEM)    |  |
|                                +------------------------+     +-------------------+  |
+--------------------------------------------|-----------------------------------------+
                                             |  Encrypted TLS Connection
                                             v
+--------------------------------------------------------------------------------------+
|                           CORPORATE ENDPOINT (WINDOWS 10/11)                          |
|                                                                                      |
|  [SESSION 0: NT AUTHORITY\SYSTEM]                                                    |
|  +--------------------------------------------------------------------------------+  |
|  | Sentravane.Sensor.Service (.NET 8/9 Native AOT Background Engine)               |  |
|  |                                                                                |  |
|  |  +---------------------+   +---------------------+   +-----------------------+ |  |
|  |  | ETW Kernel Consumer |   | Canary Tripwire     |   | USB Device Controller | |  |
|  |  | (TraceEventSession) |   | Engine              |   | (cfgmgr32 / SetupAPI) | |  |
|  |  +----------+----------+   +----------+----------+   +-----------+-----------+ |  |
|  |             |                         |                          |             |  |
|  |             +------------+------------+                          |             |  |
|  |                          v                                       v             |  |
|  |              +------------------------+              +-----------------------+ |  |
|  |              | Correlation Engine     |              | Win32 Ejection Engine | |  |
|  |              | (PID Attribution)      |              | (CM_Request_Eject)    | |  |
|  |              +-----------+------------+              +-----------------------+ |  |
|  |                          |                                                     |  |
|  |             +------------+------------+                                        |  |
|  |             v                         v                                        |  |
|  |  +---------------------+   +---------------------+   +-----------------------+ |  |
|  |  | Process Suspension  |   | WFP Quarantine      |   | Local Encrypted Store | |  |
|  |  | (ntdll!NtSuspend)   |   | (fwpuclnt.dll)      |   | (DPAPI Machine Key)   | |  |
|  |  +---------------------+   +---------------------+   +-----------------------+ |  |
|  +-----------------------------------+--------------------------------------------+  |
|                                      |                                               |
|                                      | Authenticated Duplex Named Pipe               |
|                                      | (\\.\pipe\SentravaneSensorIPC with SDDL DACL)  |
|                                      v                                               |
|  [SESSION 1+: INTERACTIVE USER SESSION]                                              |
|  +--------------------------------------------------------------------------------+  |
|  | Sentravane.Sensor.Tray (WPF/WinUI 3 Native AOT Desktop Companion)              |  |
|  |  - System Tray Status & Sensor Health Indicator                                |  |
|  |  - UIPI-Hardened Emergency Override Dialog (20s countdown)                     |  |
|  |  - Unauthorized USB Notification Toasts                                        |  |
|  +--------------------------------------------------------------------------------+  |
+--------------------------------------------------------------------------------------+
```

### 2. Component Boundaries & Responsibilities

The codebase is split into four decoupled components:

1. **`Sentravane.Sensor.Service`** (Windows Background Worker Service):
   - **Runtime**: C# .NET 8/9 compiled with `PublishAot=true` (zero JIT, self-contained single binary).
   - **Execution Context**: Windows Service running as `NT AUTHORITY\SYSTEM` in Session 0.
   - **Responsibilities**:
     - Consumes kernel events via `Microsoft.Diagnostics.Tracing.TraceEvent`.
     - Plants, verifies, and watches canary decoy files across all user profiles.
     - Intercepts USB insertion events via `RegisterDeviceNotification` and executes `CM_Request_Device_Eject`.
     - Suspends offending process trees using `ntdll!NtSuspendProcess`.
     - Enforces programmatic network isolation using user-mode Windows Filtering Platform (`fwpuclnt.dll`).
     - Maintains the outbound SignalR WebSocket link and caches offline telemetry in a local DPAPI-encrypted circular SQLite buffer.
     - Secures its own service control descriptor via `SetServiceObjectSecurity` to deny `SERVICE_STOP` to local administrators.

2. **`Sentravane.Sensor.Tray`** (Desktop Companion Application):
   - **Runtime**: C# .NET 8/9 with WPF/WinUI 3 (or Tauri Rust + WebView2).
   - **Execution Context**: Standard interactive user session (`Session 1+`), auto-started via `HKLM\Software\Microsoft\Windows\CurrentVersion\Run`.
   - **Responsibilities**:
     - Connects to the local service over `\\.\pipe\SentravaneSensorIPC`.
     - Displays system tray icon with real-time connectivity status.
     - Displays UIPI-hardened emergency countdown dialogs when rapid encryption anomalies are flagged.
     - Forwards user override decisions back across the named pipe.

3. **`Sentravane.Server.Web`** (Cloud Management Plane & SignalR Ingestion Hub):
   - **Runtime**: ASP.NET Core 8/9 Web API.
   - **Responsibilities**:
     - Fleet Enrollment endpoint (`POST /api/v1/fleet/enroll`) with tenant token verification and seat quota enforcement.
     - Real-time SignalR Hub (`/hubs/fleet`) terminating thousands of persistent TLS WebSocket connections.
     - Connection registry mapped in Redis (`ConnectionId <-> EndpointId <-> TenantId`).
     - Policy engine: compiles and pushes canary naming conventions, USB serial whitelists, and containment rules to active agents.
     - Outbound Webhook dispatcher: broadcasts security incidents to Slack, Microsoft Teams, or SIEM endpoints.

4. **`Sentravane.Client.Web`** (Administrative Fleet Console):
   - **Runtime**: React, TypeScript, Tailwind CSS.
   - **Responsibilities**:
     - Real-time inventory grid showing live agent statuses (Online, Offline, Quarantined).
     - Single-click emergency controls: Lift Quarantine, Isolate Endpoint, Trigger Full Scan.
     - Policy editor for USB hardware whitelists and canary configurations.
     - Incident timeline with interactive forensic process trees and socket connection lists.

### 3. Core Engine Mechanics & System APIs

#### A. Driverless Telemetry & Canary Tripwire Engine
*   **TraceEvent Ingestion**: Initializes a real-time `TraceEventSession` subscribing to `KernelTraceEventParser.Keywords.FileIOInit | KernelTraceEventParser.Keywords.FileIO | KernelTraceEventParser.Keywords.Process`.
*   **Buffer Tuning**: Configured with `BufferSizeMB = 64`, `FlushTimer = 50ms` to prevent event drops during heavy disk I/O bursts.
*   **Tripwire Deployment**: Automatically deploys hidden canary files upon startup into:
    - `%USERPROFILE%\Documents\!00_sentravane_canary.docx`
    - `%USERPROFILE%\Desktop\!00_sentravane_canary.xlsx`
    - `%APPDATA%\!00_sentravane_canary.pdf`
*   **Tripwire Structure**: Populated with authentic DOCX/XLSX/PDF magic numbers, headers, and pseudo-random high-entropy byte padding to ensure encryption algorithms do not skip them.
*   **PID Attribution**: When a canary file write or rename is detected via `ReadDirectoryChangesW`, the agent queries the internal ETW event ring buffer to locate the corresponding `FileIO_Write` event occurring within that millisecond, extracting the exact executing `ProcessId`.

#### B. Process Suspension Engine (`ntdll!NtSuspendProcess`)
*   Unlike `TerminateProcess` (which destroys process memory and halts immediately), Sentravane uses `NtSuspendProcess`:
    ```csharp
    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern uint NtSuspendProcess(IntPtr processHandle);
    ```
*   **Atomicity**: `NtSuspendProcess` freezes all active threads across the target process simultaneously, preventing malware from executing worker threads or spawning new child processes.
*   **Process Tree Traversal**: The engine queries `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)` to recursively discover and suspend all child and grandchild processes descended from the offending PID.

#### C. Programmatic WFP Network Quarantine (`fwpuclnt.dll`)
*   Does not rely on shell commands (`netsh advfirewall`).
*   Uses P/Invoke to interact directly with the Windows Filtering Platform user-mode API:
    - `FwpmEngineOpen0`: Opens a handle to the WFP filtering engine.
    - `FwpmSubLayerAdd0`: Creates a dedicated Sentravane high-weight sublayer (`FWPM_SUBLAYER_FLAG_PERSISTENT`).
    - `FwpmFilterAdd0`: Injects blocking filters at `FWPM_LAYER_OUTBOUND_TRANSPORT_V4` and `FWPM_LAYER_INBOUND_TRANSPORT_V4`.
*   **Exceptions Injected**:
    - **Permit Loopback**: `127.0.0.1/8` and `::1`.
    - **Permit Sentravane C2**: Dedicated outbound filter permitting traffic only to the resolving IP and port of `app.sentravane.com`.
    - **Permit DHCP/DNS**: Restricts DNS resolution strictly to configured corporate nameservers for C2 resolution.

#### D. Unauthorized USB Detection & Auto-Ejection (`cfgmgr32.dll`)
*   **Registration**: The Windows Service registers a window class with a message-only HWND to receive `WM_DEVICECHANGE` notifications (`DBT_DEVICEARRIVAL`).
*   **Device Property Extraction**: Uses SetupAPI (`SetupDiGetClassDevs`, `SetupDiEnumDeviceInfo`, `SetupDiGetDeviceRegistryProperty`) to retrieve:
    - `HardwareID` (e.g. `USB\VID_0781&PID_5583`)
    - `DeviceInstanceId` containing the unique hardware serial number.
*   **Policy Evaluation**: Checks the extracted serial against the cached in-memory whitelist.
*   **Ejection Call**: If unauthorized, calls the Configuration Manager API:
    ```csharp
    [DllImport("cfgmgr32.dll", SetLastError = true)]
    public static extern uint CM_Request_Device_Eject(
        IntPtr dnDevInst, 
        out PNP_VETO_TYPE pVetoType, 
        StringBuilder pszVetoName, 
        uint ulNameLength, 
        uint ulFlags);
    ```
    If ejection is vetoed by an active lock, invokes `CM_Disable_DevNode` to power down the hardware device node at the Windows PnP level.

#### E. Service DACL Tamper-Resistance (`advapi32.dll`)
*   To prevent local administrators or malware from stopping the service, the service installer modifies the Service Security Descriptor via `SetServiceObjectSecurity`.
*   **SDDL String Applied**:
    ```text
    D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(D;;RPWPDT;;;BA)
    ```
    - Grants full control to `NT AUTHORITY\SYSTEM` (`SY`).
    - Explicitly denies `SERVICE_STOP` (`RP`), `SERVICE_PAUSE_CONTINUE` (`WP`), and `DELETE` (`DT`) to the local `Administrators` group (`BA`).

### 4. Data Models & Interface Contracts

#### A. Fleet Enrollment Contract (`POST /api/v1/fleet/enroll`)
*   **Request Payload**:
    ```json
    {
      "enrollment_token": "sentra_enroll_8f9a2b3c4d5e",
      "hardware_uuid": "4C4C4544-0050-4810-8057-C2C04F343232",
      "mac_address": "00:1A:2B:3C:4D:5E",
      "hostname": "FINANCE-LAPTOP-04",
      "os_version": "Microsoft Windows 11 Enterprise 10.0.22631",
      "sensor_version": "1.0.0"
    }
    ```
*   **Response Payload (200 OK)**:
    ```json
    {
      "endpoint_id": "ep_98812a4b",
      "tenant_id": "tenant_acme_corp",
      "agent_secret": "sec_crypt_99182374981273981273",
      "websocket_url": "wss://app.sentravane.com/hubs/fleet",
      "heartbeat_interval_seconds": 30,
      "policies": {
        "usb_enforcement_enabled": true,
        "usb_whitelist_serials": ["078155830129", "13FE43009812"],
        "override_timeout_seconds": 20
      }
    }
    ```

#### B. Forensic Snapshot Contract (Incident Telemetry Packet)
*   Transmitted over SignalR when containment triggers:
    ```json
    {
      "endpoint_id": "ep_98812a4b",
      "incident_id": "inc_7712a",
      "timestamp_utc": "2026-11-12T23:30:00Z",
      "trigger_reason": "CANARY_MODIFICATION",
      "canary_file_path": "C:\\Users\\alice\\Documents\\!00_sentravane_canary.docx",
      "offending_process": {
        "pid": 4812,
        "process_name": "svchost_updater.exe",
        "image_path": "C:\\Users\\alice\\AppData\\Local\\Temp\\svchost_updater.exe",
        "command_line": "\"C:\\Users\\alice\\AppData\\Local\\Temp\\svchost_updater.exe\" -enc",
        "user_sid": "S-1-5-21-123456789-...",
        "parent_pid": 2104,
        "parent_name": "powershell.exe",
        "parent_command_line": "powershell.exe -ExecutionPolicy Bypass -File script.ps1"
      },
      "active_network_sockets": [
        {
          "local_ip": "192.168.1.104",
          "local_port": 49812,
          "remote_ip": "198.51.100.44",
          "remote_port": 443,
          "state": "ESTABLISHED"
        }
      ],
      "recent_modified_files": [
        "C:\\Users\\alice\\Documents\\Financials_Q3.xlsx",
        "C:\\Users\\alice\\Documents\\Passwords.kdbx",
        "C:\\Users\\alice\\Documents\\!00_sentravane_canary.docx"
      ],
      "containment_actions_taken": [
        "NT_SUSPEND_PROCESS_TREE_SUCCESS",
        "WFP_NETWORK_QUARANTINE_APPLIED"
      ]
    }
    ```

--------------------------------------------------------------------------------

## Alternatives Considered

### Alternative 1: Ring-0 Kernel Filter Drivers (Minifilter & WFP Kernel Callout)
*   **Pros**: Enables true pre-execution blocking of binaries and packet drops at the kernel boundary before user-space processes receive them.
*   **Cons**: Introduces high fleet-wide stability risks (Blue Screens of Death); requires Extended Validation (EV) certificates, Microsoft Hardware Dev Center attestation, and WHQL signing; slows automated release pipelines.
*   **Reason for Rejection**: The operational, testing, and compliance overhead of kernel drivers contradicts the goal of an agile, rapidly deployable containment engine. Over 90% of commodity ransomware executes in user space, which can be neutralized using ETW, WFP, and `NtSuspendProcess`.

### Alternative 2: Standalone `FileSystemWatcher` for Ransomware Detection
*   **Pros**: Built-in high-level .NET class; trivial to implement with zero native P/Invoke code.
*   **Cons**: Relies on a fixed internal buffer that overflows (`InternalBufferOverflowException`) during burst write activity, dropping file modification events. Crucially, `FileSystemWatcher` does not identify the Process ID (PID) responsible for file writes.
*   **Reason for Rejection**: Without PID attribution, targeted process suspension is impossible. Sentravane requires ETW kernel event correlation (`Microsoft-Windows-Kernel-File`) to pinpoint the offending PID.

### Alternative 3: Immediate System Shutdown / Power Off upon Incident
*   **Pros**: Guarantees all malicious execution ceases immediately.
*   **Cons**: Destroys volatile RAM containing crucial forensic evidence (in-memory ransomware decryption keys, injected DLLs, C2 IP addresses); risks severe NTFS file system corruption; disconnects the endpoint from the cloud console.
*   **Reason for Rejection**: Programmatic WFP network isolation halts lateral spread and C2 communication while preserving machine state, volatile RAM, and management connectivity.

### Alternative 4: CLI Firewall Manipulation (`netsh advfirewall`)
*   **Pros**: Standard built-in Windows command-line utility.
*   **Cons**: Spawns sub-processes (`cmd.exe` / `netsh.exe`) causing multi-second latency; can be bypassed or overridden by local group policy resets; lacks programmatic sublayer priority management.
*   **Reason for Rejection**: Replaced with direct user-mode P/Invoke calls into `fwpuclnt.dll` (WFP), which execute in sub-millisecond time and inject high-weight sublayer rules directly into the filtering engine.

### Alternative 5: Electron for Desktop Companion Application
*   **Pros**: Cross-platform web development ecosystem with rich UI libraries.
*   **Cons**: Spawns multiple Chromium and Node.js processes; consumes 150–300 MB of RAM at idle; slow cold-boot times.
*   **Reason for Rejection**: A background security companion running on enterprise endpoints must minimize memory and CPU overhead. C# .NET 8/9 Native AOT with WPF/WinUI 3 (or Tauri Rust + WebView2) operates within a <45 MB working set.

### Alternative 6: Client-Side License Key Validation
*   **Pros**: Operates offline without server connectivity.
*   **Cons**: Vulnerable to decompilation, license cracking, and key sharing across corporate machines.
*   **Reason for Rejection**: Replaced with central server-side seat quota validation at enrollment and SignalR WebSocket handshake.

--------------------------------------------------------------------------------

## Detailed Implementation Plan

The implementation is structured across four primary projects and supporting packages in a unified solution structure:

### 1. Root & Solution Configuration
*   **[NEW] `Sentravane.sln`**: Master Visual Studio / .NET solution referencing `Sentravane.Shared`, `Sentravane.Sensor.Service`, `Sentravane.Sensor.Tray`, `Sentravane.Server.Web`, and test suites.
*   **[NEW] `Directory.Build.props`**: Standardizes C# language version (`12.0`), target framework (`net8.0` / `net9.0`), nullable reference types, and shared build constants across all projects.

### 2. `src/Sentravane.Shared/` (Common Contracts & IPC Models)
*   **[NEW] `src/Sentravane.Shared/Sentravane.Shared.csproj`**: Multi-targeted class library referenced by both sensor and server.
*   **[NEW] `src/Sentravane.Shared/Models/Enums.cs`**: Defines `IncidentReason`, `ContainmentStatus`, `DeviceStatus`, `USBPolicyAction`.
*   **[NEW] `src/Sentravane.Shared/Models/EnrollmentContracts.cs`**: Strongly-typed DTOs for `EnrollmentRequest` and `EnrollmentResponse`.
*   **[NEW] `src/Sentravane.Shared/Models/TelemetryContracts.cs`**: DTOs for `HeartbeatPayload`, `ForensicSnapshotPayload`, `SocketEntry`, and `ProcessEntry`.
*   **[NEW] `src/Sentravane.Shared/Models/PolicyContracts.cs`**: DTOs for `EndpointPolicy` and `UsbWhitelistEntry`.
*   **[NEW] `src/Sentravane.Shared/IPC/NamedPipeContracts.cs`**: Framing and message schemas (`IPCMessage`, `OverrideRequest`, `OverrideDecision`) for cross-session communication.

### 3. `src/Sentravane.Sensor.Service/` (Windows Background Engine)
*   **[NEW] `src/Sentravane.Sensor.Service/Sentravane.Sensor.Service.csproj`**: .NET Worker Service configured with `PublishAot=true` and dependencies on `Microsoft.Diagnostics.Tracing.TraceEvent`, `System.Data.SQLite`, and `Microsoft.AspNetCore.SignalR.Client`.
*   **[NEW] `src/Sentravane.Sensor.Service/Program.cs`**: Service bootstrapping, Windows Service lifecycle hosting (`UseWindowsService`), and dependency injection registration.
*   **[NEW] `src/Sentravane.Sensor.Service/Worker.cs`**: Background service orchestration managing initialization sequences, graceful shutdown, and recovery.
*   **[NEW] `src/Sentravane.Sensor.Service/Native/Ntdll.cs`**: P/Invoke definitions for `NtSuspendProcess` and `NtResumeProcess`.
*   **[NEW] `src/Sentravane.Sensor.Service/Native/WfpNative.cs`**: P/Invoke declarations for `fwpuclnt.dll` (`FwpmEngineOpen0`, `FwpmSubLayerAdd0`, `FwpmFilterAdd0`, `FWPM_FILTER_CONDITION0`).
*   **[NEW] `src/Sentravane.Sensor.Service/Native/CfgMgrNative.cs`**: P/Invoke declarations for `cfgmgr32.dll` (`CM_Request_Device_Eject`, `CM_Disable_DevNode`) and `setupapi.dll`.
*   **[NEW] `src/Sentravane.Sensor.Service/Native/AdvapiNative.cs`**: P/Invoke declarations for `SetServiceObjectSecurity` and SDDL security descriptor conversions.
*   **[NEW] `src/Sentravane.Sensor.Service/Native/IpHelperNative.cs`**: P/Invoke declarations for `iphlpapi.dll` (`GetExtendedTcpTable`, `MIB_TCPTABLE_OWNER_PID`).
*   **[NEW] `src/Sentravane.Sensor.Service/Services/EtwCollectorService.cs`**: Manages the `TraceEventSession`, non-paged pool buffer allocations (64 MB), and event parsing for kernel file/process streams.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/CanaryManager.cs`**: Deploys realistic dummy decoy files into `%USERPROFILE%\Documents`, `Desktop`, and `AppData`; monitors for tampering via `ReadDirectoryChangesW`.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/CorrelationEngine.cs`**: Correlates canary write events with the ETW ring buffer to extract the offending PID.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/ContainmentService.cs`**: Executes atomic process suspension on offending process hierarchies and injects WFP network quarantine rules.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/UsbDeviceWatcher.cs`**: Message-only window receiving `WM_DEVICECHANGE` events, extracting hardware serials, evaluating whitelist policies, and executing eject calls.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/ForensicCollector.cs`**: Captures process lineage, queries IP Helper for open sockets, and aggregates recent file modification logs into JSON.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/LocalStoreService.cs`**: Manages an encrypted SQLite circular buffer for offline event storage using DPAPI machine-scope encryption.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/FleetSignalRClient.cs`**: Manages persistent TLS WebSocket connection to the cloud hub with exponential backoff and jittered reconnection.
*   **[NEW] `src/Sentravane.Sensor.Service/Services/NamedPipeServer.cs`**: Duplex named pipe server (`\\.\pipe\SentravaneSensorIPC`) with custom SDDL restricting access to the interactive user.

### 4. `src/Sentravane.Sensor.Tray/` (Interactive User-Session Companion)
*   **[NEW] `src/Sentravane.Sensor.Tray/Sentravane.Sensor.Tray.csproj`**: WPF/WinUI 3 application compiled with Native AOT for minimal memory footprint.
*   **[NEW] `src/Sentravane.Sensor.Tray/App.xaml`, `App.xaml.cs`**: Application entry point with single-instance named mutex enforcement.
*   **[NEW] `src/Sentravane.Sensor.Tray/Views/TrayIconManager.cs`**: Manages system tray lifecycle, context menu, and health indicator icons.
*   **[NEW] `src/Sentravane.Sensor.Tray/Views/OverrideDialog.xaml`, `OverrideDialog.xaml.cs`**: UIPI-hardened countdown modal with randomized confirmation challenge buttons.
*   **[NEW] `src/Sentravane.Sensor.Tray/Services/NamedPipeClient.cs`**: IPC client connecting to the Session 0 service over named pipes.

### 5. `src/Sentravane.Server.Web/` (ASP.NET Core Management Plane)
*   **[NEW] `src/Sentravane.Server.Web/Sentravane.Server.Web.csproj`**: ASP.NET Core Web API with Entity Framework Core, PostgreSQL driver (`Npgsql`), and StackExchange.Redis.
*   **[NEW] `src/Sentravane.Server.Web/Program.cs`**: Service dependencies, SignalR hub mapping, EF Core setup, and authentication middleware.
*   **[NEW] `src/Sentravane.Server.Web/Data/AppDbContext.cs`**: PostgreSQL EF Core database context.
*   **[NEW] `src/Sentravane.Server.Web/Data/Entities/Tenant.cs`**: Multi-tenant organization entity with seat limits and subscription tiers.
*   **[NEW] `src/Sentravane.Server.Web/Data/Entities/Endpoint.cs`**: Enrolled device record with hardware UUID, MAC address, hostname, and status.
*   **[NEW] `src/Sentravane.Server.Web/Data/Entities/Incident.cs`**: Security incident log storing forensic JSON packages.
*   **[NEW] `src/Sentravane.Server.Web/Data/Entities/Policy.cs`**: Fleet policy schema storing USB whitelists and canary settings.
*   **[NEW] `src/Sentravane.Server.Web/Controllers/EnrollmentController.cs`**: Handles `POST /api/v1/fleet/enroll`, validating tokens and seat quotas.
*   **[NEW] `src/Sentravane.Server.Web/Controllers/PolicyController.cs`**: API endpoints for configuring USB whitelist serials and threat sensitivity.
*   **[NEW] `src/Sentravane.Server.Web/Controllers/IncidentController.cs`**: Endpoints for querying incidents, downloading forensic bundles, and dispatching quarantine lift commands.
*   **[NEW] `src/Sentravane.Server.Web/Hubs/FleetHub.cs`**: High-performance SignalR hub handling agent connections, telemetry streams, and remote dispatch.
*   **[NEW] `src/Sentravane.Server.Web/Services/RedisConnectionManager.cs`**: Redis adapter tracking active `ConnectionId <-> EndpointId <-> TenantId` mappings.
*   **[NEW] `src/Sentravane.Server.Web/Services/LicenseManager.cs`**: Dynamic seat quota validation during enrollment and WebSocket handshakes.
*   **[NEW] `src/Sentravane.Server.Web/Services/WebhookDispatcher.cs`**: HTTP client dispatching incident JSON webhooks to configured URLs.

### 6. `src/Sentravane.Client.Web/` (React Admin Dashboard)
*   **[NEW] `src/Sentravane.Client.Web/package.json`**: React 18, TypeScript, Tailwind CSS, Vite, Lucide icons, and `@microsoft/signalr`.
*   **[NEW] `src/Sentravane.Client.Web/src/App.tsx`**: Dashboard layout, navigation sidebar, and real-time alert toast provider.
*   **[NEW] `src/Sentravane.Client.Web/src/pages/Dashboard.tsx`**: High-level security metrics, active endpoints count, and recent incident cards.
*   **[NEW] `src/Sentravane.Client.Web/src/pages/Endpoints.tsx`**: Real-time table of all fleet endpoints with status indicators and action buttons (Isolate / Lift).
*   **[NEW] `src/Sentravane.Client.Web/src/pages/IncidentDetail.tsx`**: Interactive forensic view rendering process ancestry trees, socket connections, and affected files.
*   **[NEW] `src/Sentravane.Client.Web/src/pages/Policies.tsx`**: Management UI for USB serial whitelists and containment timeouts.

### 7. Deployment & Verification Artifacts
*   **[NEW] `installer/SentravaneSensor.wxs`**: WiX Toolset installer definition compiling `Sentravane.Sensor.Service` and `Sentravane.Sensor.Tray` into a silent `.msi`.
*   **[NEW] `tests/Sentravane.Sensor.Tests/CorrelationEngineTests.cs`**: Unit tests verifying millisecond PID attribution between simulated canary writes and ETW events.
*   **[NEW] `tests/Sentravane.Sensor.Tests/UsbWatcherTests.cs`**: Unit tests validating USB serial extraction and whitelist evaluation logic.
*   **[NEW] `tests/Sentravane.Server.Tests/LicenseManagerTests.cs`**: Unit tests verifying per-seat quota rejection and seat recycling upon device decommissioning.
*   **[NEW] `tests/Sentravane.Server.Tests/EnrollmentControllerTests.cs`**: Integration tests for device registration, token validation, and DPAPI credential issuance.

