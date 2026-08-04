# Virtual Display Session Recovery Design

## Goal

When Virtua Display is forcibly terminated while its virtual monitor remains active, the next app launch automatically adopts that exact monitor and resumes normal ownership. Recovery must never create a monitor, adopt an Apollo-owned monitor, or guess from a friendly name.

## Root Cause

`MainWindow` stores the active `MonitorSession` only in memory. A forced process exit loses the original display topology, SudoVDA target identity, watchdog task, and routing ownership. The next process therefore initializes as stopped even when the driver still exposes the app-created monitor.

SudoVDA already treats `ADD` with an existing monitor GUID as idempotent and returns that monitor's adapter LUID and target ID. The app has a stable monitor GUID, but it currently has no durable record proving which active Windows target belongs to that GUID.

## Considered Approaches

### Chosen: durable recovery record plus exact Windows identity validation

Persist the original topology and returned SudoVDA target immediately after `ADD`. On the next launch, resolve that active target through DisplayConfig and verify its Windows device container ID equals the app's stable monitor GUID before resuming watchdog pings and adopting it.

This preserves exact ownership, supports normal Stop behavior after recovery, and does not collide with Apollo monitors.

### Rejected: call `ADD` blindly at startup

This adopts an existing same-GUID monitor, but creates a new monitor when none exists. Launching the app must not implicitly start a display.

### Rejected: match the display friendly name

Names and EDID labels are not unique ownership proof. Another SudoVDA client can use similar labels, making this unsafe on systems that also run Apollo.

## Recovery State

Add a focused `SessionRecoveryStore` beside the existing registry-backed settings store. It writes one transient recovery record under the existing `HKCU\\Software\\Virtua\\Display` application key. The record contains only primitives needed to reconstruct ownership:

- adapter LUID and target ID returned by SudoVDA;
- the requested virtual display mode;
- every display in the pre-start snapshot: device name, position, mode, and primary flag.

The record is written immediately after a successful `ADD`, before waiting for Windows display activation. Registry value replacement is atomic enough for this small per-user record. Successful removal deletes the record. No general settings migration or compatibility layer is added.

## Startup Recovery Flow

On application launch:

1. Load the recovery record. With no record, follow current startup unchanged.
2. Open SudoVDA and send one ping promptly so a short watchdog countdown does not expire during recovery.
3. Resolve the stored adapter LUID and target ID through active DisplayConfig paths.
4. Resolve that target's monitor device and read `DEVPKEY_Device_ContainerId` through SetupAPI.
5. Require the container ID to equal Virtua Display's stable monitor GUID.
6. Restart watchdog pings and optional new-window routing, rebuild `MonitorSession` using the persisted original topology, and show the existing Active UI state. Recovery never calls `ADD`, avoiding any race that could create a replacement after identity validation.

If the stored target is absent or its container ID differs, delete the stale recovery record and remain stopped. If driver or Windows identity inspection fails unexpectedly, retain the record, show a recovery error, and do not create or remove a monitor.

## Start and Stop Integration

Normal Start keeps its current behavior, with two additions:

- persist recovery state immediately after `ADD` returns;
- delete that state when partial-start cleanup confirms the monitor was removed or the target no longer exists.

Normal Stop continues routing shutdown, window relocation, topology restoration, watchdog cancellation, and monitor removal. Delete recovery state only after SudoVDA confirms removal. If removal fails, retain it so the next launch can recover again.

Normal app close still calls Stop. Forced termination leaves the recovery record intact by design.

## Boundaries

- Recovery only adopts Virtua Display's stable container GUID.
- Recovery never creates a display.
- Recovery does not adopt Apollo or other SudoVDA clients' monitors.
- If SudoVDA's watchdog already removed the monitor, startup remains stopped.
- Recovery cannot keep a monitor alive after the app is killed; it can only reclaim one that still exists.
- No background service, helper process, installer change, or driver protocol change is introduced.

## Verification

Automated self-tests cover recovery-record round trips, stale-record deletion, exact target/container matching, and the rule that recovery failure never enters Active state. Existing Release build and self-tests must remain green.

Manual validation:

1. Start one virtual display.
2. Force-kill Virtua Display while leaving SudoVDA's monitor active.
3. Relaunch before the monitor disappears, or temporarily use a disabled/long watchdog.
4. Confirm the UI automatically shows Active for the same Windows display.
5. Press Stop and confirm windows relocate, the original topology is restored, the monitor is removed, and the recovery record is cleared.
6. Launch with Apollo-owned SudoVDA monitors present and confirm none are adopted.
