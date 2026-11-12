# Goldfish Annotated Review Report

**Target:** `docs/designs/sentravane_fleet_platform.md` (`DetailedRange: lines 335 to 406`)  
**Status:** Concluded (Review Cycle Finished; Findings Documented)  
**Date:** 2026-11-13  

---

## Prioritized Findings & AI Suggestions

### 1. Incompatible Native AOT Dependencies in Worker Service [`egm-codebase-details`] - Severity: Blocker
*   **Affected Scope:** [Line 352](docs/designs/sentravane_fleet_platform.md#L352) (`### 3. src/Sentravane.Sensor.Service/`)
*   **Discrepancy Description:** Line 352 specifies `PublishAot=true` for `Sentravane.Sensor.Service.csproj` while depending on `System.Data.SQLite` and `Microsoft.Diagnostics.Tracing.TraceEvent`. `System.Data.SQLite` relies on unmanaged C++/CLI interop and reflection incompatible with Native AOT; `TraceEvent` uses dynamic IL emit (`DynamicMethod`).
*   **Rationale:** Build fails under `dotnet publish -c Release -r win-x64 /p:PublishAot=true`.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Replace `System.Data.SQLite` with `Microsoft.Data.Sqlite` (`SQLitePCLRaw.bundle_e_sqlite3`), which is certified for Native AOT in .NET 8/9. For `TraceEvent`, add ILLink trim descriptor roots (`TrimmerRootAssembly`) and manifest generation.
*   ***Proposed Edit:***
    *   Location: Line 352
    *   Change: Replace `System.Data.SQLite` with `Microsoft.Data.Sqlite` and specify `TrimmerRootAssembly` for `TraceEvent`.

---

### 2. Contradictory UI Framework Choice and WPF Native AOT Impossibility [`egm-codebase-details`, `egm-implementation-readiness`] - Severity: Blocker
*   **Affected Scope:** [Line 371](docs/designs/sentravane_fleet_platform.md#L371) (`### 4. src/Sentravane.Sensor.Tray/`)
*   **Discrepancy Description:** Declares `Sentravane.Sensor.Tray.csproj` as a "WPF/WinUI 3 application compiled with Native AOT". WPF has no Native AOT support in .NET 8/9.
*   **Rationale:** The implementer cannot simultaneously fulfill "WPF" and "Native AOT".
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Explicitly pin `Sentravane.Sensor.Tray` to **WinUI 3** (`Microsoft.WindowsAppSDK` with CsWinRT AOT), OR if WPF is selected, change compilation to standard self-contained single-file publish (`PublishReadyToRun=true`).
*   ***Proposed Edit:***
    *   Location: Line 371
    *   Change: Update framework to WinUI 3 with CsWinRT Native AOT tooling (`net8.0-windows10.0.19041.0`).

---

### 3. Session 0 PnP Arrival Failure via Message-Only HWND [`egm-implementation-readiness`] - Severity: Blocker
*   **Affected Scope:** [Line 364](docs/designs/sentravane_fleet_platform.md#L364) (`### 3. src/Sentravane.Sensor.Service/Services/UsbDeviceWatcher.cs`)
*   **Discrepancy Description:** Specifies a message-only window (`HWND_MESSAGE`) receiving `WM_DEVICECHANGE` events in Session 0 under `SYSTEM`. Message-only windows do not receive broadcast device events without an interactive window station.
*   **Rationale:** USB insertion events will never trigger in the background service.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Register device notifications using `RegisterDeviceNotification` with `DEVICE_NOTIFY_SERVICE_HANDLE` and hook the service control callback (`HandlerEx` receiving `SERVICE_CONTROL_DEVICEEVENT`).
*   ***Proposed Edit:***
    *   Location: Line 364
    *   Change: Replace message-only window with `RegisterDeviceNotification(SERVICE_STATUS_HANDLE, DEVICE_NOTIFY_SERVICE_HANDLE)`.

---

### 4. Broken Canary Path Resolution in Session 0 (`%USERPROFILE%`) [`egm-implementation-readiness`] - Severity: Blocker
*   **Affected Scope:** [Line 361](docs/designs/sentravane_fleet_platform.md#L361) (`### 3. src/Sentravane.Sensor.Service/Services/CanaryManager.cs`)
*   **Discrepancy Description:** `CanaryManager.cs` resolves `%USERPROFILE%\Documents`, which in Session 0 evaluates to `C:\Windows\System32\config\systemprofile` instead of real interactive user directories.
*   **Rationale:** Tripwire canaries will be deployed into the system profile, completely failing to detect ransomware encrypting user documents.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Enumerate user profiles from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList` (excluding system SIDs `S-1-5-18/19/20`) and query active session tokens via `WTSQueryUserToken` to locate interactive user `Documents` and `Desktop` paths.
*   ***Proposed Edit:***
    *   Location: Line 361
    *   Change: Document active user profile discovery via `ProfileList` registry enumeration and `WTSQueryUserToken`.

---

### 5. Undefined Named Pipe IPC Security Descriptor (SDDL) [`egm-implementation-readiness`] - Severity: Major
*   **Affected Scope:** [Line 368](docs/designs/sentravane_fleet_platform.md#L368) (`### 3. NamedPipeServer.cs`)
*   **Discrepancy Description:** Specifies custom SDDL restricting access to the interactive user without providing the SDDL string or impersonation rules.
*   **Rationale:** Implementers risk leaving named pipes open or misconfigured, enabling local privilege escalation or spoofing.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Supply the exact SDDL string `D:(A;;GRGW;;;IU)(A;;GRGW;;;SY)` restricting access to the Interactive User (`IU`) and Local System (`SY`), with `NamedPipeServerStream.RunAsClient` token verification.
*   ***Proposed Edit:***
    *   Location: Line 368
    *   Change: Add explicit SDDL string `D:(A;;GRGW;;;IU)(A;;GRGW;;;SY)` and token impersonation checks.

---

### 6. Undefined SQLite DPAPI Encryption Implementation [`egm-implementation-readiness`] - Severity: Major
*   **Affected Scope:** [Line 366](docs/designs/sentravane_fleet_platform.md#L366) (`### 3. LocalStoreService.cs`)
*   **Discrepancy Description:** Describes an "encrypted SQLite circular buffer using DPAPI machine-scope encryption" without specifying whether it is column-level encryption or database file encryption.
*   **Rationale:** SQLite does not natively use DPAPI for full database file encryption.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Specify application-level column encryption: payload blobs are encrypted via `ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine)` before insertion into `offline_events(id, timestamp, encrypted_payload BLOB)`.
*   ***Proposed Edit:***
    *   Location: Line 366
    *   Change: Explicitly define column-level payload encryption via `System.Security.Cryptography.ProtectedData`.

---

### 7. Missing WFP Dynamic Filter Session Lifecycle [`egm-implementation-readiness`] - Severity: Major
*   **Affected Scope:** [Lines 356, 363](docs/designs/sentravane_fleet_platform.md#L356-L363) (`WfpNative.cs`, `ContainmentService.cs`)
*   **Discrepancy Description:** Lacks specification of dynamic vs persistent WFP filter sessions. Persistent filters survive crashes and can permanently brick endpoints.
*   **Rationale:** A service crash or reboot during quarantine must not permanently isolate a corporate machine.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Configure `FwpmEngineOpen0` with `FWPM_SESSION_FLAG_DYNAMIC`, ensuring filters automatically tear down if the service process terminates unexpectedly.
*   ***Proposed Edit:***
    *   Location: Lines 356, 363
    *   Change: Specify `FWPM_SESSION_FLAG_DYNAMIC` for all quarantine filters and add automatic 4-hour fail-safe decay.

---

### 8. Missing Kernel32 Native Interop Layer [`egm-codebase-details`] - Severity: Major
*   **Affected Scope:** [Lines 355-359](docs/designs/sentravane_fleet_platform.md#L355-L359) (`### 3. Native/`)
*   **Discrepancy Description:** Omits `Kernel32Native.cs` despite requiring `ReadDirectoryChangesW`, `CreateToolhelp32Snapshot`, `Process32First/Next`, `OpenProcess`, and `CloseHandle`.
*   **Rationale:** Fundamental Win32 process and file APIs lack an architectural home in the implementation tree.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Add `[NEW] src/Sentravane.Sensor.Service/Native/Kernel32Native.cs` declaring `ReadDirectoryChangesW`, `CreateToolhelp32Snapshot`, `Process32FirstW`, `Process32NextW`, `OpenProcess`, and `CloseHandle`.
*   ***Proposed Edit:***
    *   Location: Line 359
    *   Change: Add `Kernel32Native.cs` entry under Section 3 Native interop.

---

### 9. Missing Test Project Declarations [`egm-codebase-details`] - Severity: Major
*   **Affected Scope:** [Lines 403-406](docs/designs/sentravane_fleet_platform.md#L403-L406) (`### 7. Deployment & Verification Artifacts`)
*   **Discrepancy Description:** Test files are listed without defining their parent project manifests (`Sentravane.Sensor.Tests.csproj` and `Sentravane.Server.Tests.csproj`).
*   **Rationale:** Build scripts and solution configurations require explicit `.csproj` project files.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Explicitly add project entries for `Sentravane.Sensor.Tests.csproj` and `Sentravane.Server.Tests.csproj`.
*   ***Proposed Edit:***
    *   Location: Lines 403, 405
    *   Change: Add `[NEW] tests/Sentravane.Sensor.Tests/Sentravane.Sensor.Tests.csproj` and `[NEW] tests/Sentravane.Server.Tests/Sentravane.Server.Tests.csproj`.

---

### 10. Conflation of Server Integration Tests with Client DPAPI [`egm-implementation-readiness`] - Severity: Major
*   **Affected Scope:** [Line 406](docs/designs/sentravane_fleet_platform.md#L406)
*   **Discrepancy Description:** States that `EnrollmentControllerTests.cs` tests "DPAPI credential issuance". DPAPI is client-side only.
*   **Rationale:** The ASP.NET Core server cannot run or verify Windows DPAPI machine keys.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Correct `EnrollmentControllerTests.cs` to test enrollment token validation and seat quota decrementing. Relocate DPAPI credential tests to `Sentravane.Sensor.Tests`.
*   ***Proposed Edit:***
    *   Location: Line 406
    *   Change: Rephrase test scope to "Integration tests for device registration, token validation, seat quota decrementing, and agent secret key issuance."

---

### 11. Missing Typed SignalR Client & Server Interfaces [`egm-codebase-details`] - Severity: Nit
*   **Affected Scope:** [Lines 343-350](docs/designs/sentravane_fleet_platform.md#L343-L350) (`### 2. src/Sentravane.Shared/`)
*   **Discrepancy Description:** Lacks strongly-typed SignalR Hub contracts for compile-time RPC validation.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Add `[NEW] src/Sentravane.Shared/Contracts/IFleetHub.cs` with `IFleetHubClient` (commands down) and `IFleetHubServer` (telemetry up).
*   ***Proposed Edit:***
    *   Location: Line 350
    *   Change: Add `src/Sentravane.Shared/Contracts/IFleetHub.cs`.

---

### 12. Ambiguous TFM & Target Framework Specification [`egm-style`] - Severity: Nit
*   **Affected Scope:** [Line 341](docs/designs/sentravane_fleet_platform.md#L341)
*   **Discrepancy Description:** Specifies `net8.0 / net9.0` with slash notation and omits `-windows` OS suffix for client projects.
*   ***AI Suggestion:*** `PROPOSE FIX`
*   ***Justification:*** Pin `Directory.Build.props` to `net8.0` LTS with `net8.0-windows` for sensor components.
*   ***Proposed Edit:***
    *   Location: Line 341
    *   Change: Pin target framework to `net8.0` with `net8.0-windows` for `Sensor.Service` and `Sensor.Tray`.

