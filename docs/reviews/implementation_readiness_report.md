# Implementation Readiness Review Report: Sentravane Fleet Platform

* **Document:** [docs/designs/sentravane_fleet_platform.md](file:///f:/Github%20Projects/Sentravane/docs/designs/sentravane_fleet_platform.md) (DetailedRange: lines 335–406)  
* **Reviewers:** `egm-implementation-readiness`, `egm-codebase-details`, `egm-style`  
* **Date:** 2026-11-12  
* **Mode:** `IMPLEMENTATION_READINESS`  

---

## Reviewer Status

| Reviewer | Status |
| :--- | :--- |
| `egm-implementation-readiness` (Pragmatic Tech Lead) | Completed |
| `egm-codebase-details` (Pedantic Quality Auditor) | Completed |
| `egm-style` (Technical Copy Editor) | Completed |

---

## Summary of Findings

| Severity | Count | Summary |
| :--- | :--- | :--- |
| 🔴 **Blocker** | 4 | Build-breaking AOT package choices, unworkable WPF Native AOT requirement, Session 0 HWND PnP failure, and `%USERPROFILE%` resolution failure in Session 0 |
| 🟡 **Major** | 6 | Undefined IPC SDDL security, undefined SQLite DPAPI encryption model, unmanaged WFP filter lifecycles, missing Kernel32 P/Invoke layer, missing test `.csproj` definitions, and server-side DPAPI test conflation |
| 🟢 **Nit** | 4 | "Four primary projects" section count contradiction, dual-target TFM ambiguity, frontend inclusion in `.sln`, and missing SignalR Hub interface contracts |
| **Total** | **14** | All findings prioritized and actionable |

---

## Prioritized Findings (Blockers -> Major -> Nits)

### 1. Incompatible Native AOT Dependencies in Worker Service
* **Severity:** **Blocker**
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** Lines 352–353 (`### 3. src/Sentravane.Sensor.Service/`)
* **Discrepancy Description:** Line 352 specifies `PublishAot=true` for `Sentravane.Sensor.Service.csproj` while taking dependencies on `System.Data.SQLite` and `Microsoft.Diagnostics.Tracing.TraceEvent`. `System.Data.SQLite` relies on legacy unmanaged C++/CLI interop and runtime reflection that is neither trim-compatible nor supported under Native AOT. Similarly, `Microsoft.Diagnostics.Tracing.TraceEvent` relies heavily on runtime IL code generation (`DynamicMethod`) to parse dynamic event schemas, which fails under AOT compilation.
* **Rationale & Recommendation:** Replace `System.Data.SQLite` with `Microsoft.Data.Sqlite` (backed by `SQLitePCLRaw.bundle_e_sqlite3`), which is officially certified for Native AOT and trimming in .NET 8/9. For `TraceEvent`, either document explicit trimming suppressions/AOT root annotations (`TrimmerRootAssembly`) with pre-compiled manifests, or evaluate the native C-based ETW consumer APIs via P/Invoke (`OpenTrace`, `ProcessTrace`) to maintain zero-JIT Native AOT integrity.

### 2. Contradictory UI Framework Choice and WPF Native AOT Impossibility
* **Severity:** **Blocker**
* **Reviewer Lens:** `egm-codebase-details`, `egm-implementation-readiness`
* **Affected Scope:** Line 371 (`### 4. src/Sentravane.Sensor.Tray/`)
* **Discrepancy Description:** Line 371 declares `Sentravane.Sensor.Tray.csproj` as a "WPF/WinUI 3 application compiled with Native AOT". WPF has no Native AOT support in .NET 8 or .NET 9 due to deep dependencies on reflection, `System.Xaml`, and non-annotated assemblies. Furthermore, phrasing the UI framework as "WPF/WinUI 3 (or Tauri Rust + WebView2)" (from lines 144, 326, and 371) forces the implementer to make a fundamental architectural framework decision.
* **Rationale & Recommendation:** Explicitly commit to a single framework. If Native AOT is mandatory, select **WinUI 3** with `Microsoft.WindowsAppSDK` and CsWinRT AOT tooling, or **Tauri (Rust + WebView2)**. If WPF is chosen, remove `PublishAot=true` and configure ready-to-run (`PublishReadyToRun=true`) single-file deployment instead.

### 3. Session 0 PnP Arrival Failure via Message-Only HWND
* **Severity:** **Blocker**
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Line 364 (`### 3. src/Sentravane.Sensor.Service/Services/UsbDeviceWatcher.cs`)
* **Discrepancy Description:** Line 364 specifies a "message-only window receiving `WM_DEVICECHANGE` events" to detect USB device arrivals. `Sentravane.Sensor.Service` executes in Windows Session 0 under `NT AUTHORITY\SYSTEM`. Windows Session 0 is isolated from interactive window stations (`WinSta0`), and message-only windows (`HWND_MESSAGE`) in Session 0 do not reliably receive PnP hardware broadcast events without an interactive desktop pump.
* **Rationale & Recommendation:** For Windows Services, the Microsoft-sanctioned mechanism to receive `WM_DEVICECHANGE` is calling `RegisterDeviceNotification` using the service's `SERVICE_STATUS_HANDLE` with the `DEVICE_NOTIFY_SERVICE_HANDLE` flag, processed inside the `ServiceBase.OnCustomCommand` / `HandlerEx` callback loop, rather than spinning up a message-only HWND.

### 4. Broken Canary Path Resolution in Session 0 (`%USERPROFILE%`)
* **Severity:** **Blocker**
* **Reviewer Lens:** `egm-implementation-readiness`, `egm-codebase-details`
* **Affected Scope:** Line 361 (`### 3. src/Sentravane.Sensor.Service/Services/CanaryManager.cs`)
* **Discrepancy Description:** Line 361 states that `CanaryManager.cs` "Deploys realistic dummy decoy files into `%USERPROFILE%\Documents`, `Desktop`, and `AppData`". Because the service runs in Session 0 under `NT AUTHORITY\SYSTEM`, `%USERPROFILE%` evaluates to `C:\Windows\System32\config\systemprofile`. Decoys will be deployed into the System profile rather than interactive corporate user directories, rendering canary-based ransomware detection completely inert.
* **Rationale & Recommendation:** Update the plan for `CanaryManager` to enumerate active user profiles from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList` (filtering out well-known service accounts like `S-1-5-18`, `19`, `20`), or query the interactive logged-on user token using `WTSQueryUserToken(WTSGetActiveConsoleSessionId())` to resolve `CSIDL_PERSONAL` / `FOLDERID_Documents` dynamically.

### 5. Undefined Named Pipe IPC Security Descriptor (SDDL)
* **Severity:** **Major**
* **Reviewer Lens:** `egm-implementation-readiness`, `egm-codebase-details`
* **Affected Scope:** Line 368 (`### 3. src/Sentravane.Sensor.Service/Services/NamedPipeServer.cs`)
* **Discrepancy Description:** Line 368 specifies a named pipe server (`\\.\pipe\SentravaneSensorIPC`) with "custom SDDL restricting access to the interactive user" without providing the SDDL schema or implementation method. Unlike the service SCM SDDL explicitly defined in Line 224, the named pipe SDDL is left completely to the implementer. An incorrect SDDL permits local unprivileged processes to spoof override requests or cause denial of service.
* **Rationale & Recommendation:** Provide the explicit SDDL string for `PipeSecurity` / `NamedPipeServerStreamAcl`, e.g., `D:(A;;GRGW;;;IU)(A;;GRGW;;;SY)`, explicitly restricting read/write access to the Interactive User (`IU`) and Local System (`SY`), and define the token impersonation check via `NamedPipeServerStream.RunAsClient`.

### 6. Undefined SQLite DPAPI Encryption Implementation
* **Severity:** **Major**
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Line 366 (`### 3. src/Sentravane.Sensor.Service/Services/LocalStoreService.cs`)
* **Discrepancy Description:** Line 366 describes an "encrypted SQLite circular buffer for offline event storage using DPAPI machine-scope encryption". Standard SQLite engines do not natively use DPAPI for database file encryption (which typically requires SQLite Encryption Extension or SQLCipher with a symmetric master key). It is unclear whether DPAPI is applied at the file level via a DPAPI-wrapped symmetric key or if individual telemetry records are encrypted column-by-column before insertion (`ProtectedData.Protect(..., DataProtectionScope.LocalMachine)`).
* **Rationale & Recommendation:** Explicitly define the cryptographic approach: specify that `LocalStoreService` persists SQLite unencrypted at the engine/container level within a restricted directory (`%ProgramData%\Sentravane\Store` with SYSTEM-only DACL) and uses `System.Security.Cryptography.ProtectedData` at the application layer to encrypt the payload blob before inserting into `offline_events(id, timestamp, encrypted_payload)`, OR specify SQLCipher with a DPAPI-protected key blob.

### 7. Missing WFP Filter Lifecycle & Dynamic vs. Persistent Session Spec
* **Severity:** **Major**
* **Reviewer Lens:** `egm-implementation-readiness`
* **Affected Scope:** Lines 356, 363 (`WfpNative.cs`, `ContainmentService.cs`)
* **Discrepancy Description:** The plan specifies injecting blocking filters via `FwpmFilterAdd0` and `FwpmSubLayerAdd0`, but does not define how filter IDs (`filterId`) are tracked, whether the WFP engine session is created with `FWPM_SESSION_FLAG_DYNAMIC`, and how quarantine rollback is executed. If persistent filters (`FWPM_SUBLAYER_FLAG_PERSISTENT`) are injected without dynamic session management, a service crash or unexpected reboot leaves the workstation permanently quarantined from the corporate network with no way to recover.
* **Rationale & Recommendation:** Specify that `FwpmEngineOpen0` uses a persistent sublayer but ephemeral filters bound to a dynamic session (`FWPM_SESSION_FLAG_DYNAMIC`), or specify an explicit recovery routine in `ContainmentService` that tracks `filterId` GUIDs in `LocalStoreService` to remove them upon `LiftQuarantine` or service recovery.

### 8. Missing Kernel32 Native Interop Layer
* **Severity:** **Major**
* **Reviewer Lens:** `egm-codebase-details`
* **Affected Scope:** Lines 355–359 (`### 3. Native/`)
* **Discrepancy Description:** The native interop section defines `Ntdll.cs`, `WfpNative.cs`, `CfgMgrNative.cs`, `AdvapiNative.cs`, and `IpHelperNative.cs`, but omits `Kernel32Native.cs`. The implementation plan and technical spec mandate `ReadDirectoryChangesW` (Line 361), `CreateToolhelp32Snapshot`, `Process32First/Next` (Line 189), `OpenProcess`, and `CloseHandle` (Line 185), none of which belong in `Ntdll` or `Advapi32`.
* **Rationale & Recommendation:** Add `[NEW] src/Sentravane.Sensor.Service/Native/Kernel32Native.cs` defining `ReadDirectoryChangesW`, `CreateFileW`, `CreateToolhelp32Snapshot`, `Process32FirstW`, `Process32NextW`, and handle lifecycle APIs.

### 9. Missing Test Project Declarations in Implementation Tree
* **Severity:** **Major**
* **Reviewer Lens:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** Lines 403–406 (`### 7. Deployment & Verification Artifacts`)
* **Discrepancy Description:** Lines 403–406 list test files (`CorrelationEngineTests.cs`, `UsbWatcherTests.cs`, `LicenseManagerTests.cs`, `EnrollmentControllerTests.cs`), but the parent test project files (`Sentravane.Sensor.Tests.csproj` and `Sentravane.Server.Tests.csproj`) are omitted from the detailed implementation plan.
* **Rationale & Recommendation:** Explicitly list the test project manifests under Section 7:
  - `[NEW] tests/Sentravane.Sensor.Tests/Sentravane.Sensor.Tests.csproj` (referencing xUnit, Moq, and `Sentravane.Sensor.Service`)
  - `[NEW] tests/Sentravane.Server.Tests/Sentravane.Server.Tests.csproj` (referencing `Microsoft.AspNetCore.Mvc.Testing` and `Sentravane.Server.Web`).

### 10. Conflation of Server Integration Tests with Client DPAPI
* **Severity:** **Major**
* **Reviewer Lens:** `egm-implementation-readiness`, `egm-codebase-details`
* **Affected Scope:** Line 406 (`### 7. Deployment & Verification Artifacts`)
* **Discrepancy Description:** Line 406 states that `EnrollmentControllerTests.cs` performs "Integration tests for device registration, token validation, and DPAPI credential issuance." DPAPI is a Windows-specific client DPAPI call (`CryptProtectData`) performed locally on the endpoint sensor. The server (`Sentravane.Server.Web`) is a platform-agnostic ASP.NET Core service that generates standard cryptographic tokens (e.g., HMAC-SHA256 agent secrets), and does not issue DPAPI credentials.
* **Rationale & Recommendation:** Correct the test description to: "Integration tests for device registration, enrollment token validation, seat quota decrementing, and agent secret key issuance." Relocate DPAPI credential storage verification to `Sentravane.Sensor.Tests`.

### 11. Project Count Misstatement in Section Header
* **Severity:** **Nit**
* **Reviewer Lens:** `egm-style`
* **Affected Scope:** Line 337 (`## Detailed Implementation Plan`)
* **Discrepancy Description:** Line 337 states: "The implementation is structured across four primary projects and supporting packages in a unified solution structure:", but the section lists **7 numbered subsections**, containing five distinct application projects (`Shared`, `Sensor.Service`, `Sensor.Tray`, `Server.Web`, `Client.Web`), plus WiX installer and test suites.
* **Rationale & Recommendation:** Update Line 337 to: "The implementation is structured across five primary projects, deployment packages, and test suites in a unified solution structure:".

### 12. Ambiguous Target Framework Specification
* **Severity:** **Nit**
* **Reviewer Lens:** `egm-codebase-details`, `egm-style`
* **Affected Scope:** Line 341 (`### 1. Root & Solution Configuration`)
* **Discrepancy Description:** Line 341 states: "target framework (`net8.0` / `net9.0`)". Using slash notation creates ambiguity on whether multi-targeting (`<TargetFrameworks>net8.0;net9.0</TargetFrameworks>`) is intended or if a single LTS framework is selected. Furthermore, Windows-specific projects (`Sensor.Service` and `Sensor.Tray`) require Windows TFM monikers (e.g. `net8.0-windows` or `net8.0-windows10.0.19041.0`) to access Win32/WPF/WinUI components.
* **Rationale & Recommendation:** Pin `Directory.Build.props` to `net8.0` (LTS baseline), and configure `net8.0-windows` for `Sentravane.Sensor.Service` and `Sentravane.Sensor.Tray`.

### 13. Solution Scope & Client Dashboard Integration
* **Severity:** **Nit**
* **Reviewer Lens:** `egm-style`, `egm-codebase-details`
* **Affected Scope:** Lines 340, 393 (`Sentravane.sln`, `src/Sentravane.Client.Web/`)
* **Discrepancy Description:** Line 340 lists the projects included in `Sentravane.sln`, but does not clarify whether the frontend React app (`src/Sentravane.Client.Web`) is wrapped in a `.esproj` (Visual Studio JavaScript Project System) or managed independently via npm/pnpm workspaces.
* **Rationale & Recommendation:** Explicitly state in Section 1 whether `Sentravane.Client.Web` is wrapped with `Sentravane.Client.Web.esproj` for single-command Visual Studio F5 debugging, or managed as an independent frontend workspace.

### 14. Missing Typed SignalR Client & Server Interfaces
* **Severity:** **Nit**
* **Reviewer Lens:** `egm-codebase-details`, `egm-implementation-readiness`
* **Affected Scope:** Lines 343–350 (`### 2. src/Sentravane.Shared/`)
* **Discrepancy Description:** Section 2 lists DTO contracts for enrollment, telemetry, and named pipes, but does not specify strongly-typed SignalR Hub contracts (e.g., `IFleetHubClient` for server-to-agent commands like `IsolateEndpoint`, `LiftQuarantine`, and `IFleetHubServer` for client-to-server invocations).
* **Rationale & Recommendation:** Add `[NEW] src/Sentravane.Shared/Contracts/IFleetHub.cs` defining `IFleetHubClient` and `IFleetHubServer` to enable compile-time contract enforcement for SignalR streaming.

---

### Conclusion & Readiness Verdict
* **Readiness Status:** **NOT READY (Requires Revision)**
* **Summary:** The detailed plan contains 4 build-breaking Blockers and 6 Major technical omissions that must be resolved before implementation code can be executed cleanly.
