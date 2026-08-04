# Virtua Display Standalone Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship one self-contained x64 Virtua Display installer that safely installs SudoVDA when missing and coexists with Apollo.

**Architecture:** The application exposes a no-mutation driver probe used by both its UI and Inno Setup. The installer embeds a pinned driver payload and nefcon, verifies SHA-256 hashes, reuses any compatible installed SudoVDA instance, and never replaces or uninstalls a shared driver.

**Tech Stack:** .NET 10 WPF, C#, Inno Setup 7, PowerShell, SudoVDA 1.10.9.289, nefcon 1.17.40

## Global Constraints

- Windows 10/11 x64 only.
- Publish .NET self-contained; no separate .NET runtime installation.
- Installer product identity is `Virtua Display`; stable AppId is `{dfc943fb-8a0c-4c66-93c4-de7e92f75d3f}`.
- Reuse compatible Apollo-installed SudoVDA unchanged.
- Abort on incompatible SudoVDA; never silently replace, downgrade, or remove it.
- Uninstall removes Virtua Display only and leaves SudoVDA and its certificate installed.
- Installer may be unsigned; document the expected SmartScreen warning.
- No updater, service, telemetry, package manager, or separate dependency installer.

---

### Task 1: Add a read-only driver-status contract

**Files:**
- Create: `src/Virtua.Display/DriverStatus.cs`
- Modify: `src/Virtua.Display/SudoVdaClient.cs`
- Modify: `src/Virtua.Display/App.xaml.cs`
- Modify: `src/Virtua.Display/MainWindow.xaml.cs`
- Test: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Consumes: `SudoVdaClient.OpenDevice()` and `GetProtocolVersion()`.
- Produces: `DriverStatus SudoVdaClient.Probe()`, `--driver-status`, exit codes 0/2/3/4, and injected `DriverStatus?` in `MainWindow`.

- [ ] **Step 1: Add failing status-classification checks**

Add checks for this exact contract:

```csharp
Check(DriverStatus.FromProtocol(0, 2, 0).Kind == DriverStatusKind.Ready,
    "minimum driver protocol ready");
Check(DriverStatus.FromProtocol(0, 1, 9).Kind == DriverStatusKind.Incompatible,
    "old driver protocol incompatible");
Check(DriverStatus.FromProtocol(1, 0, 0).Kind == DriverStatusKind.Incompatible,
    "different driver major incompatible");
Check((int)DriverStatusKind.Ready == 0 &&
      (int)DriverStatusKind.Missing == 2 &&
      (int)DriverStatusKind.Incompatible == 3 &&
      (int)DriverStatusKind.Error == 4,
    "driver status exit codes");
```

- [ ] **Step 2: Run self-test and verify compilation fails**

Run:

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
```

Expected: compile failure because `DriverStatus` does not exist.

- [ ] **Step 3: Implement the status value**

Create:

```csharp
namespace Virtua.Display;

internal enum DriverStatusKind
{
    Ready = 0,
    Missing = 2,
    Incompatible = 3,
    Error = 4
}

internal readonly record struct DriverStatus(DriverStatusKind Kind, string Message)
{
    internal static DriverStatus FromProtocol(byte major, byte minor, byte incremental) =>
        major == 0 && minor >= 2
            ? new(DriverStatusKind.Ready,
                $"SudoVDA protocol {major}.{minor}.{incremental} is ready.")
            : new(DriverStatusKind.Incompatible,
                $"SudoVDA protocol {major}.{minor}.{incremental} is incompatible; need 0.2 or newer minor version.");
}
```

- [ ] **Step 4: Implement one-shot probing without polling**

Add `internal static DriverStatus Probe()` to `SudoVdaClient`. Open the interface, read the protocol once, and return `DriverStatus.FromProtocol`. Return `Missing` only for the existing no-interface path. Return `Error` with the Win32/exception message for other failures. Refactor `Open()` to use the same `FromProtocol` result before returning a client, so runtime and installer compatibility rules cannot diverge.

Replace the missing-driver text with:

```csharp
"SudoVDA device interface not found. Install or repair the Virtua Display driver."
```

- [ ] **Step 5: Expose the installer probe command**

Handle `--driver-status` before smoke/self-test and single-instance acquisition:

```csharp
if (eventArgs.Args.Contains("--driver-status", StringComparer.OrdinalIgnoreCase))
{
    var status = SudoVdaClient.Probe();
    Console.WriteLine($"{status.Kind}|{status.Message}");
    Shutdown((int)status.Kind);
    return;
}
```

- [ ] **Step 6: Surface status in the UI**

Add optional `DriverStatus? driverStatus = null` to the internal `MainWindow` constructor. Probe once when not injected. For `Missing`, `Incompatible`, or `Error`, set `_statusLabel.Text` to the status message, use `ErrorBrush`, and keep Start disabled. For `Ready`, retain normal `Stopped` behavior. Do not add a timer or background worker.

- [ ] **Step 7: Verify all four paths**

Run:

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --driver-status
```

Expected on the development system: self-test passes; status prints `Ready|SudoVDA protocol ... is ready.` and exits 0.

- [ ] **Step 8: Commit**

```powershell
git add src\Virtua.Display
git commit -m "feat: expose SudoVDA driver status"
```

### Task 2: Vendor and verify the pinned third-party payload

**Files:**
- Create: `packaging/driver/sudovda/SudoVDA.inf`
- Create: `packaging/driver/sudovda/SudoVDA.dll`
- Create: `packaging/driver/sudovda/sudovda.cat`
- Create: `packaging/driver/sudovda/sudovda.cer`
- Create: `packaging/tools/nefconc.exe`
- Create: `packaging/licenses/SudoVDA-README.md`
- Create: `packaging/licenses/nefcon-LICENSE.txt`
- Create: `packaging/payload.sha256`
- Create: `packaging/verify-payload.ps1`
- Create: `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: signed SudoVDA 1.10.9.289 payload currently installed with Apollo and official nefcon 1.17.40 release.
- Produces: immutable local installer payload verified by `packaging/verify-payload.ps1`.

- [ ] **Step 1: Stage the signed SudoVDA files**

Copy only `SudoVDA.inf`, `SudoVDA.dll`, `sudovda.cat`, and `sudovda.cer` from `C:\Program Files\Apollo\drivers\sudovda` into `packaging/driver/sudovda`. Require these SHA-256 values:

```text
AD69AC682756F0CF339B081FAC7E6E8159FDF2CA01CA69DF8945C7246C286925  driver/sudovda/SudoVDA.inf
47EE263CB5DE9382C6630A2D7F3DAFEC4A49419F953BEEC869CA5DD0C460FF63  driver/sudovda/SudoVDA.dll
2F9189DE5604BEC9D86F51640CC540639E394D9AD0F8E689129375E95F2D22F8  driver/sudovda/sudovda.cat
6ACCDCD519F6179D967DB4EAA20ECF25A732BA30E87F4CFFEBC768B2C13C9007  driver/sudovda/sudovda.cer
```

Verify `SudoVDA.inf` contains `DriverVer = 07/14/2025,1.10.9.289` and `Root\SudoMaker\SudoVDA`.

- [ ] **Step 2: Stage official nefcon 1.17.40**

Download `https://github.com/nefarius/nefcon/releases/download/v1.17.40/nefcon_v1.17.40.zip`; require archive SHA-256 `812BAE7ED7DFB7D6D2284BC7DE2F8CCEBC92ED2A0B1AE893C53B337096E50C1A`; extract `x64/nefconc.exe` to `packaging/tools/nefconc.exe`.

- [ ] **Step 3: Add deterministic payload verification**

Write `payload.sha256` with one uppercase hash and repository-relative path per line. Implement `verify-payload.ps1` using only `Get-FileHash`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($line in Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload.sha256')) {
    $expected, $relative = $line -split '\s+', 2
    $path = Join-Path $PSScriptRoot $relative
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "SHA-256 mismatch: $relative" }
}
```

Include the actual nefconc hash produced after extraction in the manifest.

- [ ] **Step 4: Add provenance and licenses**

Record SudoVDA source `https://github.com/SudoMaker/SudoVDA`, package provenance `ClassicOldSong/Apollo` driver 1.10.9.289, signer `CN=sudovda@su.mk`, nefcon source/tag, license names, and all hashes in `THIRD-PARTY-NOTICES.md`. Store the upstream SudoVDA README license section and nefcon MIT license under `packaging/licenses`.

- [ ] **Step 5: Verify payload**

Run:

```powershell
.\packaging\verify-payload.ps1
Get-AuthenticodeSignature packaging\driver\sudovda\SudoVDA.dll
Get-AuthenticodeSignature packaging\driver\sudovda\sudovda.cat
Get-AuthenticodeSignature packaging\tools\nefconc.exe
```

Expected: verifier exits 0; all three signatures report `Valid`.

- [ ] **Step 6: Commit**

```powershell
git add packaging THIRD-PARTY-NOTICES.md
git commit -m "build: vendor verified SudoVDA payload"
```

### Task 3: Build the self-contained Inno Setup installer

**Files:**
- Create: `packaging/VirtuaDisplay.iss`
- Create: `packaging/build-installer.ps1`
- Modify: `.gitignore`
- Test: generated `artifacts/VirtuaDisplay-Setup-<version>.exe`

**Interfaces:**
- Consumes: `VirtuaDisplay.exe --driver-status`, verified payload, .NET publish output.
- Produces: one conventional x64 installer; compatible=continue, missing=install, incompatible/error=abort.

- [ ] **Step 1: Add build output ignores**

Add:

```gitignore
artifacts/
```

- [ ] **Step 2: Create the installer metadata and files**

Set these core directives in `VirtuaDisplay.iss`:

```ini
#define MyAppName "Virtua Display"
#define MyAppExeName "VirtuaDisplay.exe"

[Setup]
AppId={{dfc943fb-8a0c-4c66-93c4-de7e92f75d3f}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Virtua Display
DefaultGroupName=Virtua Display
OutputBaseFilename=VirtuaDisplay-Setup-{#MyAppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\src\Virtua.Display\assets\icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
AppMutex=Local\Virtua.Display
SetupLogging=yes

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "driver\sudovda\*"; Flags: dontcopy
Source: "tools\nefconc.exe"; Flags: dontcopy
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{group}\Virtua Display"; Filename: "{app}\VirtuaDisplay.exe"
Name: "{autodesktop}\Virtua Display"; Filename: "{app}\VirtuaDisplay.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Registry]
Root: HKCU; Subkey: "Software\Virtua\Display"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Virtua Display"; Flags: uninsdeletevalue
```

Do not add any driver or certificate uninstall action.

- [ ] **Step 3: Implement pre-install probing and hash checks**

In `PrepareToInstall`, extract the app and payload to `{tmp}`. Run `VirtuaDisplay.exe --driver-status` through `ExecAndCaptureOutput`. Interpret result codes exactly:

```pascal
const
  DriverReady = 0;
  DriverMissing = 2;
  DriverIncompatible = 3;
  DriverError = 4;
```

For incompatible/error, return the captured first output line as the installation-blocking message. Before any driver command, compare `GetSHA256OfFile` for all five payload files with `packaging/payload.sha256`; return `Bundled driver verification failed.` on any mismatch.

- [ ] **Step 4: Install only when missing**

Execute these commands only after probe exit code 2:

```text
certutil.exe -addstore -f root "{tmp}\sudovda.cer"
certutil.exe -addstore -f TrustedPublisher "{tmp}\sudovda.cer"
nefconc.exe --create-device-node --class-name Display --class-guid "4D36E968-E325-11CE-BFC1-08002BE10318" --hardware-id root\sudomaker\sudovda --no-duplicates
nefconc.exe --install-driver --inf-path "{tmp}\SudoVDA.inf"
```

Treat exit codes 0 and 3010 as success; set `NeedsRestart := True` for 3010. Re-run `VirtuaDisplay.exe --driver-status`; continue only on exit code 0. Never call `--remove-device-node`.

- [ ] **Step 5: Create the build wrapper**

Implement `build-installer.ps1` with mandatory semantic `-Version`. It must run:

```powershell
.\packaging\verify-payload.ps1
dotnet publish .\src\Virtua.Display\Virtua.Display.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=false `
    -p:DebugType=None `
    -o .\artifacts\publish
& "$env:ProgramFiles(x86)\Inno Setup 7\ISCC.exe" `
    "/DMyAppVersion=$Version" .\packaging\VirtuaDisplay.iss
```

Resolve repository-relative paths from `$PSScriptRoot`, fail if `ISCC.exe` is absent, and emit the final setup path and SHA-256.

- [ ] **Step 6: Build and inspect installer**

Run:

```powershell
.\packaging\build-installer.ps1 -Version 0.1.0
Get-Item .\artifacts\VirtuaDisplay-Setup-0.1.0.exe
Get-FileHash .\artifacts\VirtuaDisplay-Setup-0.1.0.exe -Algorithm SHA256
```

Expected: one nonempty setup EXE and printed SHA-256.

- [ ] **Step 7: Commit**

```powershell
git add .gitignore packaging
git commit -m "build: add standalone Windows installer"
```

### Task 4: Update end-user documentation

**Files:**
- Modify: `README.md`
- Modify: `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: completed standalone installer.
- Produces: accurate no-Apollo installation instructions and third-party attribution.

- [ ] **Step 1: Replace transitional requirements**

State that the installer bundles and installs SudoVDA when needed; Apollo is not required. Retain Windows 10/11 x64. Remove the `.NET 10 Desktop Runtime` requirement for installer users.

- [ ] **Step 2: Document coexistence and warnings**

Add concise notes: compatible Apollo SudoVDA is reused, incompatible versions block setup, uninstall leaves the shared driver/certificate, and the unsigned proof-of-concept installer may trigger SmartScreen.

- [ ] **Step 3: Document source builds**

Keep `dotnet build` instructions for contributors. Add installer build prerequisites: .NET 10 SDK and Inno Setup 7. Show `.\packaging\build-installer.ps1 -Version 0.1.0`.

- [ ] **Step 4: Verify documentation and old dependency text**

Run:

```powershell
rg -n "requires Apollo|Apollo with its SudoVDA driver|Desktop Runtime" README.md
.\packaging\verify-payload.ps1
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
```

Expected: no stale requirement matches; verifier and build succeed.

- [ ] **Step 5: Commit**

```powershell
git add README.md THIRD-PARTY-NOTICES.md
git commit -m "docs: document standalone installation"
```

### Task 5: Validate installation and coexistence

**Files:**
- Create: `docs/testing/standalone-installer-checklist.md`
- Test: `artifacts/VirtuaDisplay-Setup-0.1.0.exe`

**Interfaces:**
- Consumes: final installer and supported Windows machines/VMs.
- Produces: recorded pass/fail evidence for clean, Apollo-first, and failure-path installations.

- [ ] **Step 1: Record the test matrix**

Create a checklist with exact rows for Windows 10 x64 clean, Windows 11 x64 clean, Apollo-first compatible, Virtua-Display-first then Apollo, repair, upgrade, uninstall, denied UAC, incompatible protocol, and modified-payload hash failure. Columns: OS build, prior driver, installer result, driver status, display start/stop, Apollo result, uninstall result, notes.

- [ ] **Step 2: Run local non-destructive verification**

Run:

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --driver-status
.\packaging\verify-payload.ps1
.\packaging\build-installer.ps1 -Version 0.1.0
```

Expected: all exit 0 on the compatible development system.

- [ ] **Step 3: Run clean-system install checks**

On clean Windows 10 and 11 x64 VMs: install, launch, start/stop one virtual display, repair, upgrade over the same AppId, and uninstall. Confirm Virtua Display files/settings disappear while SudoVDA remains installed.

- [ ] **Step 4: Run Apollo coexistence checks**

On Apollo-first: confirm setup reports/reuses Ready, creates no duplicate device node, and Apollo works after Virtua Display uninstall. On Virtua-Display-first: install a compatible Apollo release and confirm both applications can open the shared driver independently when the other is closed.

- [ ] **Step 5: Run failure checks**

Confirm denied UAC changes nothing; incompatible protocol aborts before cert/driver commands; changing one extracted payload byte causes `Bundled driver verification failed.`; forced nefcon failure aborts setup and writes useful Inno Setup log output.

- [ ] **Step 6: Commit evidence and final verification**

```powershell
git add docs\testing\standalone-installer-checklist.md
git commit -m "test: record standalone installer validation"
git status --short
```

Expected: checklist contains results for every row and final status is clean.
