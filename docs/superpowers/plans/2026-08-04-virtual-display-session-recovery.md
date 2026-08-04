# Virtual Display Session Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reclaim Virtua Display's exact still-active monitor after forced exit without creating or adopting another monitor.

**Architecture:** Persist the SudoVDA target and pre-start topology in one transient registry value. At launch, validate the active target's Windows container GUID through DisplayConfig and SetupAPI, then rebuild the current session with watchdog, routing, and saved topology. Recovery never calls SudoVDA `ADD`.

**Tech Stack:** C# 14, .NET 10 WPF, Windows Registry, System.Text.Json, DisplayConfig, SetupAPI, existing self-test harness

## Global Constraints

- Adopt only container GUID `8d6a8a70-67e9-4af0-9e57-0fcb401ca31b`.
- Recovery never calls `ADD` or creates a monitor.
- Apollo monitors remain untouched.
- Successful removal clears recovery state; failed removal retains it.
- No service, helper, installer change, dependency, or migration.
- Preserve the user's updated `assets/logo.png` unchanged.

---

### Task 1: Persist recovery state

**Files:**
- Create: `src/Virtua.Display/SessionRecoveryStore.cs`
- Modify: `src/Virtua.Display/Models.cs`
- Modify: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Produces `SessionRecoveryState(AddedDisplay Display, DisplayMode Mode, DisplaySnapshot Snapshot)`.
- Produces `SessionRecoveryStore.Load`, `Save`, and `Clear`, each accepting the existing registry path override used by self-tests.

- [ ] Write failing self-tests for registry round-trip, corrupt data rejection, and clear.
- [ ] Run `dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test`; require failure because recovery types are missing.
- [ ] Implement one JSON string value named `ActiveSession` under `HKCU\Software\Virtua\Display`. Validate nonempty snapshot, supported modes, nonblank device names, and exactly one primary.
- [ ] Rerun self-test; require `Self-test passed.`
- [ ] Commit plan, models, store, and tests as `feat: persist recoverable display sessions`.

Expected test shape:

```csharp
var state = new SessionRecoveryState(
    new AddedDisplay(1234, 7),
    new DisplayMode(1920, 1080, 120),
    new DisplaySnapshot([
        new DisplayState(@"\\.\DISPLAY1", new Point(0, 0), primary, true)
    ]));
SessionRecoveryStore.Save(state, path);
Check(SessionRecoveryStore.Load(path) == state, "session recovery registry round-trip");
SessionRecoveryStore.Clear(path);
Check(SessionRecoveryStore.Load(path) is null, "session recovery clear");
```

### Task 2: Resolve exact monitor ownership

**Files:**
- Modify: `src/Virtua.Display/DisplayController.cs`
- Modify: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Produces `TryGetOwnedDisplayName(AddedDisplay display, Guid containerId, out string deviceName)`.
- Produces `MatchesRecoveryTarget(AddedDisplay expected, Guid expectedContainerId, AddedDisplay actual, Guid actualContainerId)`.

- [ ] Write failing checks for exact match, target mismatch, and container mismatch.
- [ ] Run self-test; require missing-method failure.
- [ ] Reuse the active DisplayConfig query. Request `DISPLAYCONFIG_TARGET_DEVICE_NAME` (`Type = 2`), enumerate present `GUID_DEVINTERFACE_MONITOR` interfaces, match `monitorDevicePath`, and read `DEVPKEY_Device_ContainerId` (`{8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C}`, property 2) using `SetupDiGetDevicePropertyW`.
- [ ] Run Release build and self-test; require zero warnings/errors and `Self-test passed.`
- [ ] Commit controller and tests as `feat: identify recoverable virtual displays`.

### Task 3: Recover ownership at launch

**Files:**
- Modify: `src/Virtua.Display/MainWindow.xaml.cs`
- Modify: `src/Virtua.Display/SelfTest.cs`
- Modify: `docs/superpowers/specs/2026-08-04-virtual-display-session-recovery-design.md`

**Interfaces:**
- Consumes the recovery store and exact identity lookup.
- Produces one-shot `RecoverSessionAsync()` registered only by the parameterless production constructor.

- [ ] Write failing source-structure checks proving startup recovery exists, never calls `Add`, Start saves after `Add`, and successful cleanup clears.
- [ ] Run self-test; require named recovery failures.
- [ ] Implement recovery: load state, open SudoVDA, ping, validate exact ownership, start watchdog and optional routing, rebuild `MonitorSession`, and show Active. Clear absent/foreign state. Retain state and report unexpected errors. Dispose partial resources.
- [ ] Persist state immediately after normal `Add`. Clear only after confirmed removal in partial cleanup or Stop.
- [ ] Run Release build and self-test; require zero warnings/errors and `Self-test passed.`
- [ ] Commit lifecycle, tests, and corrected spec as `feat: recover active virtual display sessions`.

### Task 4: Logo and final artifacts

**Files:**
- Modify: `assets/logo.png`

- [ ] Verify supplied logo is 1126 x 706, `Format32bppArgb`, SHA-256 `2F31EE1666722C3B003E11508EE88758C1A64882E49E3979A5090A58C65A6C9B`.
- [ ] Run Release build, self-test, `packaging\build-installer.ps1 -Version 0.1.0`, and `git diff --check`.
- [ ] Do not run `--smoke-test`; forced-close recovery stays manual.
- [ ] Commit only the supplied logo as `docs: update Virtua Display logo`.
