# Sentravane: Driverless Enterprise Fleet Security & Containment Platform

* **Author:** Sentravane Engineering Team
* **Date:** 2026-11-12

## Overview

Sentravane is an enterprise endpoint security, device control, and ransomware containment platform designed around a driverless Windows architecture. It pairs real-time user-mode telemetry, canary tripwires, automated hardware device control (unauthorized USB auto-ejection), and incident forensic snapshots with instant local containment mechanisms—atomic process suspension and programmatic network quarantine via the Windows Filtering Platform (WFP)—while streaming fleet state and receiving administrative commands via an ASP.NET Core SignalR web console. Operating as an `NT AUTHORITY\SYSTEM` service, Sentravane avoids the stability hazards, BSOD risks, and code-signing/WHQL deployment bottlenecks of kernel drivers.

---

## Analysis

The platform architecture incorporates the following technical systems:

### 1. Endpoint Sensor Architecture & Performance Stack
* **Windows Background Service (`Sentravane.Sensor.Service`):**
  * Built on **C# .NET 8/9** compiled with **Native AOT** (`PublishAot=true`).
  * Runs in Session 0 as `NT AUTHORITY\SYSTEM`.
  * Memory footprint: <45 MB working set; average CPU usage <1.0%.
  * Interfaces with Win32, SetupAPI, and ETW with zero runtime dependencies on external framework installations.
* **Desktop Companion / Tray Application (`Sentravane.Sensor.Tray`):**
  * Recommended stack: **C# .NET 8/9 Native AOT with WPF/WinUI 3** or **Tauri (Rust + Windows WebView2)**.
  * *Excluded:* **Electron** is explicitly rejected due to excessive idle memory consumption (150–300 MB) and Chromium process bloat.
  * Connects to the local Windows Service via an ACL-restricted Named Pipe (`\\.\pipe\SentravaneSensorIPC`).
  * Surfaces emergency override prompt dialogues, local quarantine status, and USB authorization notifications.

### 2. Fleet Enrollment, Identity & Licensing Architecture
* **Tenant-Scoped Enrollment Tokens:**
  * Administrators generate enrollment tokens in the Web Console with seat quotas, expiration windows, and optional department tags.
* **Silent Mass Deployment:**
  * Distributed via standard Windows Installer (`.msi`) through Microsoft Intune, SCCM, or Group Policy:
    `msiexec /i SentravaneSensor.msi ENROLL_TOKEN="sentra_enroll_..." SERVER_URL="https://app.sentravane.com" /quiet`
* **Hardware Fingerprinting & Credential Provisioning:**
  * On initial boot, the Windows Service captures hardware identity: Motherboard UUID (`Win32_ComputerSystemProduct.UUID`), primary MAC address, machine hostname, and OS version.
  * The service transmits these parameters with the enrollment token to `POST /api/v1/fleet/enroll`.
  * The server checks seat limits against the tenant's license tier, records the device, and issues a permanent cryptographic **Endpoint Secret**.
  * The secret is stored on disk using Windows Data Protection API (DPAPI) machine-level encryption, accessible only by `SYSTEM`.
* **Central Cloud Licensing Model (Per-Seat Subscription):**
  * License enforcement is strictly server-side: each enrolled device consumes 1 seat. Over-quota enrollments return `HTTP 402/403 Quota Exceeded`.
  * Admins can decommission endpoints via the web dashboard, immediately revoking credentials and re-allocating the license seat.

### 3. Real-Time Fleet Communication & Identity Mapping
* **Persistent SignalR WebSocket Channel:**
  * The agent opens a persistent TLS WebSocket connection (`wss://app.sentravane.com/hubs/fleet`) passing its `Endpoint-ID` and DPAPI secret in authentication headers.
* **Connection Registry & State Tracking:**
  * The server maps active SignalR `ConnectionId` values to `EndpointId` in an in-memory/Redis registry (`ConnectionId <-> EndpointId <-> TenantId`).
  * Endpoints reflect real-time status in the web UI (Online, Offline, Quarantined).
* **Bi-directional Message Pipeline:**
  * **Upstream:** Endpoints push 30-second lightweight telemetry heartbeats (CPU, RAM, canary health) and immediate high-priority alerts upon incident detection.
  * **Downstream:** Server dispatches instant operational commands (Lift Quarantine, Trigger Scan, Update USB Whitelist) down the active socket in sub-second latency.

### 4. Tamper Resistance & Service Hardening
* **Service DACL Lockdown:**
  * Modifies the Service Control Manager security descriptor via `SetServiceObjectSecurity`.
  * Strips `SERVICE_STOP`, `SERVICE_PAUSE_CONTINUE`, and `DELETE` access rights from the local `Administrators` group (`BA`). Only `NT AUTHORITY\SYSTEM` (`SY`) retains full control, preventing local admins or malware from terminating the service via `net stop` or `sc stop`.
* **Filesystem Access Control (ACLs):**
  * Sensor binaries, rules, and local databases in `%ProgramData%\Sentravane` are ACL-locked to grant write and modify permissions exclusively to `SYSTEM`.

### 5. Forensic Snapshot Engine (Post-Incident Evidence Capture)
* **Immediate State Capture on Containment:**
  * When a canary is modified or anomalous encryption is confirmed, the service generates an instant in-memory forensic bundle before network isolation:
    1. **Process Ancestry Tree:** Records full process lineage (Parent PID, Grandparent PID, full command-line arguments, user token, and image path) to trace binary origins.
    2. **Active Socket Snapshot:** Uses the Windows IP Helper API (`GetExtendedTcpTable`) to capture all remote IP addresses and ports connected to the offending PID right before isolation.
    3. **Affected File Ledger:** Aggregates file paths modified or renamed by the offending PID over the preceding 60 seconds from the ETW ring buffer.
    4. **Process Mini-Dump:** Optionally writes a compact memory dump (`dbghelp!MiniDumpWriteDump`) of the suspended process for offline analysis.
  * The forensic bundle is encrypted and streamed to the Central Console.

### 6. Unauthorized USB Detection & Auto-Ejection Engine
* **Device Arrival Monitoring:**
  * The Windows Service registers for device interface notifications via `RegisterDeviceNotification` (`GUID_DEVINTERFACE_USB_DEVICE` and `GUID_DEVINTERFACE_DISK`).
* **Hardware Identification & Whitelist Matching:**
  * Extracts Vendor ID (VID), Product ID (PID), and hardware serial number via `SetupDiGetDeviceRegistryProperty`.
  * Cross-references credentials against the local cached whitelist policy deployed from the cloud.
* **Driverless Auto-Ejection:**
  * If unauthorized, invokes the Configuration Manager API (`cfgmgr32.dll` via `CM_Request_Device_Eject` or `CM_Disable_DevNode`) to dismount and disable the device node before filesystem mounting occurs.

### 7. Ransomware Tripwire & Containment Mechanics
* **Driverless Telemetry via ETW:**
  * Uses `Microsoft.Diagnostics.Tracing.TraceEvent` (`TraceEventSession`) consuming `Microsoft-Windows-Kernel-File` and `Microsoft-Windows-Kernel-Process` event streams with a 32–64 MB non-paged buffer.
* **Canary Decoy Files:**
  * Deployed in standard user directories (`Documents`, `Desktop`, `AppData`) with randomized high-entropy contents and alphabetical naming (`!00_sentravane_canary.docx`, `z99_sentravane_canary.xlsx`).
  * Any modification or rename immediately isolates the offending Process ID (PID) from the ETW event queue.
* **Atomic Process Suspension:**
  * Invokes `ntdll!NtSuspendProcess` directly via P/Invoke, freezing all execution threads in the offending process tree in sub-millisecond time.
* **Programmatic WFP Network Quarantine:**
  * Uses user-mode WFP APIs (`fwpuclnt.dll`) to inject high-weight firewall filters:
    * Blocks all inbound and outbound IPv4/IPv6 traffic across local network interfaces.
    * Exempts localhost loopback (`127.0.0.1/8`, `::1`).
    * Exempts the dedicated outbound IP/port of the Sentravane Web Console to keep management channels open.

### 8. Server & Fleet Hub (Central Management Plane)
* **Technology Stack:**
  * ASP.NET Core 8/9 Web API + SignalR Hub.
  * PostgreSQL for relational inventory, policies, and forensic audit logs.
  * Redis for WebSocket backplane scaling across multiple server instances.
  * React + TypeScript + Tailwind CSS for the administrative web console.
* **Alerting Integrations:**
  * Outbound Webhook notification channels (JSON payloads for Slack, Microsoft Teams, and SIEM endpoints).

---

## Goals vs. Non-Goals

### In-Scope Goals
* Driverless ransomware containment on Windows 10/11 and Windows Server (2019/2022/2025).
* Real-time canary tripwire monitoring paired with ETW file/process event correlation for PID isolation.
* Programmatic WFP network isolation maintaining outbound C2 connectivity.
* Instant process suspension (`NtSuspendProcess`) of suspicious process hierarchies.
* Unauthorized USB mass storage detection, whitelist enforcement, and automatic hardware ejection.
* Forensic snapshot generation (process tree, command line, open network sockets, modified files) upon containment.
* Service DACL hardening preventing termination by local administrators.
* Silent MSI deployment using tenant-scoped enrollment tokens, hardware fingerprinting, and DPAPI storage.
* Per-seat enterprise licensing enforced at the central management server.
* Outbound webhook alerts on security incidents.

### Out-of-Scope Non-Goals
* Kernel-mode drivers (Ring-0 Minifilter or WFP kernel callout drivers).
* Pre-execution binary blocking (blocking binaries prior to launch).
* Protection against Ring-0 rootkits or Bring Your Own Vulnerable Driver (BYOVD) exploits.
* Electron-based desktop front-ends.
* Non-Windows endpoint sensors in Version 1.

---

## Alternatives Weighed

1. **Licensing Enforcement (Client Binary Checks vs. Central Server Quotas):**
   * *Rejected:* Local client license keys can be decompiled or patched.
   * *Adopted:* Server-side seat tracking on enrollment and WebSocket handshake; revocation immediately severs fleet connectivity.
2. **Endpoint Connection Tracking (HTTP Polling vs. State-Mapped WebSockets):**
   * *Rejected:* Polling introduces high latency for emergency quarantine commands and high database query load.
   * *Adopted:* SignalR WebSockets backed by Redis connection mapping for instant two-way communication.
3. **Service Protection (Kernel Driver ObRegisterCallbacks vs. Service DACL Hardening):**
   * *Rejected:* Kernel `ObRegisterCallbacks` requires a signed kernel driver.
   * *Adopted:* Service Control Manager DACL manipulation via `SetServiceObjectSecurity`, preventing administrative process termination without drivers.
4. **Desktop UI Framework (WPF/WinUI 3 vs. Tauri vs. Electron):**
   * *Rejected (Electron):* Heavy memory footprint (150–300 MB) unacceptable for enterprise fleet sensors.
   * *Adopted:* C# .NET 8/9 Native AOT with WPF/WinUI 3 (or Tauri Rust + WebView2), maintaining memory <45 MB.
5. **USB Control Mechanism (Kernel Filter vs. User-Mode Configuration Manager):**
   * *Rejected (Kernel USB filter driver):* Requires WHQL attestation and introduces kernel crash hazards.
   * *Adopted:* User-mode Win32 SetupAPI / Configuration Manager API (`CM_Request_Device_Eject` / `CM_Disable_DevNode`) triggered upon `RegisterDeviceNotification`.

---

## Key Decisions & Staging for Next Steps

1. **Stack Baseline:**
   * **Endpoint Service:** C# .NET 8/9 Worker Service (Native AOT).
   * **Endpoint Tray UI:** C# .NET 8/9 Native AOT with WPF/WinUI 3.
   * **Server & Fleet Hub:** ASP.NET Core Web API + SignalR + PostgreSQL + Redis.
   * **Admin Web UI:** React + TypeScript + Tailwind CSS.
2. **Next Steps:**
   * Invoke `/egm-draft` to initialize the formal technical design document.
