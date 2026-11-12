# Comprehension Test Audit: Sentravane Fleet Platform

* **Document:** [docs/designs/sentravane_fleet_platform.md](file:///f:/Github%20Projects/Sentravane/docs/designs/sentravane_fleet_platform.md)  
* **Reviewer:** Naive Reader (`egm-comprehension`)  
* **Date:** 2026-11-12  

---

## Feature Range Paraphrase
**What this proposes:**
The document proposes Sentravane, a multi-tenant enterprise endpoint security platform for Windows fleets that provides ransomware containment and USB device control entirely in user space without kernel-mode drivers. An autonomous Windows background service running under `NT AUTHORITY\SYSTEM` continuously correlates ETW kernel file events with planted canary decoy files; upon detecting unauthorized modification, it immediately freezes offending process trees using `NtSuspendProcess` and enforces network isolation via user-mode Windows Filtering Platform (WFP) APIs. The agent maintains persistent duplex SignalR WebSocket connections to an ASP.NET Core cloud plane backed by PostgreSQL and Redis for real-time telemetry, remote commands, and seat-governed policy synchronization, while an interactive user-session tray companion provides emergency override prompts.

**How the existing system works, per this document:**
Enterprises currently rely on cloud-dependent Endpoint Detection and Response (EDR) solutions that suffer from detection-to-cloud roundtrip latency, allowing rapid ransomware to encrypt hundreds of files per second and spread laterally across local shares before remote isolation commands arrive. Conventional endpoint security agents also depend on Ring-0 kernel drivers (Minifilters and WFP callouts), which introduce fleet-wide crash risks (BSODs) and onerous WHQL signing requirements. Concurrently, unmonitored USB peripherals introduce insider data theft and malware entry points that administrators cannot easily restrict without disrupting standard input hardware.

---

## Detailed Range Paraphrase
**What this proposes:**
The detailed plan establishes a unified .NET solution containing shared DTO models and IPC definitions (`Sentravane.Shared`), a Native AOT Windows background service (`Sentravane.Sensor.Service`) encapsulating Win32/NT native P/Invoke calls and an encrypted SQLite local store, a Native AOT desktop companion (`Sentravane.Sensor.Tray`), an ASP.NET Core management plane (`Sentravane.Server.Web`) integrating EF Core, PostgreSQL, Redis, and SignalR, and a React/TypeScript administrative console (`Sentravane.Client.Web`). Deployment is packaged through a silent WiX MSI installer that deploys the background service and tray application with enrollment token parameters, accompanied by unit and integration test fixtures covering correlation logic, USB filtering, seat licensing, and enrollment.

**How the existing system works, per this document:**
The existing repository currently lacks the planned four-component architecture, requiring greenfield creation of the solution structure, native Win32/WFP/ETW integration layers, cross-session named pipe IPC, database models, and automated test fixtures to realize the proposed driverless containment workflows.

---

## Prioritized Findings

### 1. Blocker: Canary Directory Resolution under Session 0 `NT AUTHORITY\SYSTEM`
* **Severity**: Blocker
* **Affected Scope**: Section 3.A (lines 175–178), Section 2.1 (line 137), and `CanaryManager.cs` (line 361)
* **Discrepancy Description & Rationale**:
  Lines 175–178 state that the service deploys canary files into `%USERPROFILE%\Documents\!00_sentravane_canary.docx`, `%USERPROFILE%\Desktop\!00_sentravane_canary.xlsx`, and `%APPDATA%\!00_sentravane_canary.pdf`. However, the service executes in Session 0 under `NT AUTHORITY\SYSTEM` (line 134). In a Windows SYSTEM service context, `%USERPROFILE%` resolves to `C:\Windows\System32\config\systemprofile`, and `%APPDATA%` resolves to `systemprofile\AppData\Roaming`. Planted canaries will never be created in interactive user directories where ransomware actually encrypts user files. The document does not describe how the service determines interactive user profile paths (e.g., enumerating `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList` or querying active explorer sessions). An engineer following this specification will implement canaries that only protect the inactive system profile.

### 2. Major: Premise Gap Regarding SCM DACL Tamper-Resistance vs Process Termination
* **Severity**: Major
* **Affected Scope**: Section 3.E (lines 220–228) and Goals (line 28)
* **Discrepancy Description & Rationale**:
  The document claims that hardening the Service Control Manager (SCM) security descriptor (DACL) via `SetServiceObjectSecurity` to deny `SERVICE_STOP`, `SERVICE_PAUSE_CONTINUE`, and `DELETE` prevents local administrators and malware from terminating or pausing the agent. While modifying SCM DACLs prevents stopping the service via `net stop` or `sc.exe`, any elevated administrator or malware executing with `SeDebugPrivilege` can bypass the SCM entirely by opening a direct handle to the process (`OpenProcess` with `PROCESS_TERMINATE`) and invoking `TerminateProcess`. Presenting SCM DACLs as a complete driverless tamper-resistance boundary without documenting how direct process termination is mitigated (or acknowledging this fundamental boundary limitation) will mislead developers and security auditors.

### 3. Major: Asynchronous User-Mode USB Ejection Race Condition
* **Severity**: Major
* **Affected Scope**: CUJ 3 (line 60), Section 3.D (lines 202–219), and `UsbDeviceWatcher.cs` (line 364)
* **Discrepancy Description & Rationale**:
  CUJ 3 states that the service invokes `CM_Request_Device_Eject` "to dismount the drive before Windows Explorer can mount the volume." However, receiving a `WM_DEVICECHANGE` notification (`DBT_DEVICEARRIVAL`) in user space is strictly asynchronous. By the time a user-mode service receives the window message, extracts hardware IDs via SetupAPI, checks the whitelist, and issues `CM_Request_Device_Eject`, the Windows kernel Mount Manager (`mountmgr.sys`) and volume managers have already mounted the filesystem, opened volume handles, and initiated shell auto-discovery. Once a handle is active, `CM_Request_Device_Eject` returns `PNP_VETO_TYPE` veto status. While the document mentions falling back to `CM_Disable_DevNode`, it fails to explain how it prevents read/write access during that multi-millisecond race window without a filesystem minifilter.

### 4. Major: Native AOT Incompatibility with `System.Data.SQLite`
* **Severity**: Major
* **Affected Scope**: Section 3 (line 352: `src/Sentravane.Sensor.Service/Sentravane.Sensor.Service.csproj`)
* **Discrepancy Description & Rationale**:
  Line 352 specifies compiling `Sentravane.Sensor.Service` with `PublishAot=true` while taking a dependency on `System.Data.SQLite`. `System.Data.SQLite` relies on legacy mixed-mode assemblies, reflection, and COM-style interop that break under .NET Native AOT compilation. For Native AOT compatibility in .NET 8/9, Microsoft officially requires `Microsoft.Data.Sqlite` backed by SQLitePCLRaw (`SQLitePCLRaw.bundle_e_sqlite3`). Following the specification as written will trigger compilation/trimming failures during Native AOT build.

### 5. Major: Ambiguous Session 0 Window Messaging for Device Notifications
* **Severity**: Major
* **Affected Scope**: Section 3.D (line 203) vs Section 2.1 (line 138)
* **Discrepancy Description & Rationale**:
  Line 203 states: "The Windows Service registers a window class with a message-only HWND to receive `WM_DEVICECHANGE` notifications." However, in Windows Session 0, message-only windows (`HWND_MESSAGE`) do not receive broadcast system device events (`WM_DEVICECHANGE`) without an interactive message pump attached to an active desktop. To receive device arrival notifications in a non-interactive Windows service, developers must call `RegisterDeviceNotification` using `DEVICE_NOTIFY_SERVICE_HANDLE` and process device events via the service control handler (`HandlerEx` receiving `SERVICE_CONTROL_DEVICEEVENT`), or bind to a specific device interface GUID class. The architecture gives contradictory instructions (message-only HWND vs `RegisterDeviceNotification` in line 138) without outlining the necessary service registration flags.

### 6. Nit: Undefined / Unexpanded Acronyms and Concepts
* **Severity**: Nit
* **Affected Scope**: Lines 45, 111, 116, 122, 150, 160, 199, 222
* **Discrepancy Description & Rationale**:
  The following domain acronyms and architectural concepts are used without inline definitions, expansions, or reference links:
  - **UIPI** (lines 122, 150, 374): "UIPI-Hardened Emergency Override Dialog". User Interface Privilege Isolation is never defined, nor does the document explain how a medium-integrity desktop companion in Session 1 applies UIPI hardening.
  - **DPAPI** (lines 45, 111, 141, 366, 406): Data Protection API is not defined or expanded.
  - **SDDL** (lines 116, 222, 358, 368): Security Descriptor Definition Language is referenced with concrete SDDL strings without defining the syntax standard or format.
  - **C2** (lines 11, 199, 200, 315): Used interchangeably as "cloud command-and-control" and "Sentravane C2" without explicit definition for general readers.
  - **SIEM** (lines 83, 160): Security Information and Event Management is not expanded.

