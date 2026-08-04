# Virtual Display Session Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reclaim Virtua Display's still-active monitor by stable container GUID without persistent session metadata.

**Architecture:** Enumerate active DisplayConfig targets, resolve monitor container IDs through SetupAPI, and adopt only Virtua Display's fixed GUID. Keep exact topology only in memory during a normal session; synthesize a safe physical-only topology after process loss.

**Tech Stack:** C# 14, .NET 10 WPF, DisplayConfig, SetupAPI, existing self-test harness

## Global Constraints

- Adopt only container GUID `8d6a8a70-67e9-4af0-9e57-0fcb401ca31b`.
- Recovery never calls `ADD` or creates a monitor.
- Apollo monitors remain untouched.
- No persistent recovery metadata, migration, service, helper, installer change, or dependency.

---

### Task 1: Discover the GUID-owned active monitor

**Files:**
- Modify: `src/Virtua.Display/DisplayController.cs`
- Modify: `src/Virtua.Display/SelfTest.cs`

- [x] Add a failing exact-owner GUID check.
- [x] Enumerate active DisplayConfig paths and resolve monitor container IDs.
- [x] Return the Windows display name for the exact GUID.

### Task 2: Build a safe recovered topology

**Files:**
- Modify: `src/Virtua.Display/DisplayController.cs`
- Modify: `src/Virtua.Display/SelfTest.cs`

- [x] Add a failing physical-only fallback topology check.
- [x] Remove the virtual target from the captured topology.
- [x] Preserve a physical primary, or choose the closest physical display below the virtual display and normalize it to `(0, 0)`.

### Task 3: Recover at startup

**Files:**
- Modify: `src/Virtua.Display/MainWindow.xaml.cs`
- Delete: `src/Virtua.Display/SessionRecoveryStore.cs`
- Modify: `src/Virtua.Display/Models.cs`
- Modify: `docs/superpowers/specs/2026-08-04-virtual-display-session-recovery-design.md`

- [x] Recover by GUID whether or not any prior process state exists.
- [x] Restart watchdog and optional routing without calling `ADD`.
- [x] Remove registry recovery state, model, tests, and lifecycle calls.
- [x] Run Release build and self-test.
- [x] Manually verify forced-close recovery on Windows.