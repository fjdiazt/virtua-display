# Virtual Display Session Recovery Design

## Goal

When Virtua Display is forcibly terminated while its virtual monitor remains active, the next launch adopts that exact monitor without creating or adopting another client's display.

## Ownership

Virtua Display passes one fixed GUID to SudoVDA. Windows exposes that GUID as the monitor container ID, making it the stable ownership identity. Adapter LUID and target ID are session-local and are ignored for recovery.

Recovery never calls SudoVDA `ADD`. It enumerates active targets, resolves each target's monitor container ID through SetupAPI, and adopts only container GUID `8d6a8a70-67e9-4af0-9e57-0fcb401ca31b`.

## Startup Recovery

On application launch:

1. Open SudoVDA and ping its watchdog.
2. Find the active DisplayConfig target with Virtua Display's container GUID.
3. If none exists, remain stopped.
4. Capture the current topology and remove the virtual display from that snapshot.
5. If the virtual display is primary, choose the physical display positioned directly below and closest to its center, then normalize that display to `(0, 0)` as the fallback primary.
6. Restart watchdog pings and optional window routing, rebuild the in-memory session, and show Active.

No registry recovery record, service, helper process, installer change, or driver protocol change is used.

## Start and Stop

Normal Start retains the exact pre-start topology in memory. Normal Stop restores it exactly.

A recovered session cannot know the topology that existed before the killed process. Its Stop operation uses the synthesized physical-only snapshot, relocates windows to the selected physical primary, restores that safe layout, and removes the virtual display.

## Apollo Isolation

Apollo monitors use different container GUIDs and are ignored. A shared SudoVDA watchdog can keep Virtua Display's orphan alive, but it does not change ownership matching.

## Verification

Automated self-tests cover exact GUID comparison and physical-only fallback topology. Release build and self-test must pass. Forced-close recovery remains a manual Windows display-topology test.