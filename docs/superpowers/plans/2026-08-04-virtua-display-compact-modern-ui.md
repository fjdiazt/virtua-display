# Virtua Display Compact Modern UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Virtua Display's utilitarian form chrome with a compact, modern native-WPF interface while preserving every behavior contract.

**Architecture:** Keep the existing code-behind and named controls. Replace application resources with one coherent design system, replace `GroupBox` layout with semantic section grids and dividers, and update only the aspect-lock glyph strings in code. Existing self-tests remain the behavior safety net and gain presentation assertions.

**Tech Stack:** .NET 10, WPF XAML, C#, existing self-test runner

## Global Constraints

- Native WPF only; no new dependencies, compatibility layer, or migration path.
- One compact surface; no cards, dashboard layout, shadows, or oversized window.
- Preserve control names, automation IDs, tab order, persistence, display lifecycle, tray behavior, and installer behavior.
- Use `Segoe UI Variable Text` and `Segoe Fluent Icons` with a single dark theme.
- Live display and installer interaction remain maintainer-run manual tests.

---

### Task 1: Lock the new visual contract with failing self-tests

**Files:**
- Modify: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Consumes: `MainWindow`, named WPF controls, application resources.
- Produces: assertions for section structure, templates, typography, and Fluent lock glyphs.

- [ ] **Step 1: Replace GroupBox assertions with modern section assertions**

Assert named `Border` sections `displaySection`, `behaviorSection`, and `applicationSection`; header title `Virtua Display`; subtitle `One focused virtual display.`; POC badge; window font `Segoe UI Variable Text`; and absence of the old GroupBox names.

- [ ] **Step 2: Add template and icon assertions**

Apply templates and assert `InputBorder` for text boxes, `ButtonBorder` for the primary button, `CheckBorder` for checkboxes, and `LockButtonBorder` for the aspect toggle. Expect unlocked `\uE785` and locked `\uE72E` glyphs.

- [ ] **Step 3: Run the RED test**

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
```

Expected: failures because the new named sections/templates and Fluent glyphs do not exist.

### Task 2: Replace the visual system and window layout

**Files:**
- Modify: `src/Virtua.Display/App.xaml`
- Modify: `src/Virtua.Display/MainWindow.xaml`
- Modify: `src/Virtua.Display/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: every existing named control and event wired by `MainWindow.xaml.cs`.
- Produces: the compact modern window without behavior changes.

- [ ] **Step 1: Replace application resources**

Define the approved dark palette and reusable styles for `Window`, `TextBlock`, `Label`, `TextBox`, `ComboBox`, `ComboBoxItem`, `Button`, `ToggleButton`, `CheckBox`, `ContextMenu`, `MenuItem`, `Separator`, and `ToolTip`. Custom templates must expose the exact names asserted in Task 1.

- [ ] **Step 2: Replace the main layout**

Build the header, three divider-separated sections, and footer. Preserve `_aspectCombo`, `_presetCombo`, `_widthText`, `_heightText`, `_aspectLockButton`, `_refreshCombo`, all behavior checkboxes, `_statusIndicator`, `_statusLabel`, `_startStopButton`, `resolutionLayout`, and existing automation IDs/tab indices.

- [ ] **Step 3: Replace emoji lock content**

In `UpdateAspectLockButton`, set content to `"\uE72E"` when locked and `"\uE785"` when unlocked. Preserve tooltip and automation-name behavior.

- [ ] **Step 4: Run GREEN verification**

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release --no-build -- --self-test
```

Expected: build has zero warnings/errors and self-test passes.

### Task 3: Render, inspect, document, and package

**Files:**
- Modify: `docs/images/virtua-display.png`
- Test: `artifacts/VirtuaDisplay-Setup-0.1.0.exe`

**Interfaces:**
- Consumes: completed WPF UI and existing screenshot/installer workflow.
- Produces: inspected README screenshot and rebuilt installer.

- [ ] **Step 1: Capture the actual window**

Launch only the WPF form without starting a virtual display, capture its HWND using the existing Windows screenshot method, then close it. Inspect the image for clipping, alignment, control states, contrast, and compact proportions.

- [ ] **Step 2: Correct visual defects and repeat verification**

For any rendered defect, adjust only XAML/style values, rerun Release build/self-test, and recapture until correct.

- [ ] **Step 3: Replace the README screenshot**

Write the accepted capture to `docs/images/virtua-display.png`; README already references this path.

- [ ] **Step 4: Rebuild the installer**

```powershell
.\packaging\build-installer.ps1 -Version 0.1.0
```

Expected: payload verification, self-contained publish, and Inno Setup compile succeed.

- [ ] **Step 5: Final verification**

Run the published `--driver-status` and `--self-test` commands with redirected output; confirm exit code 0, inspect installer size/version/hash, run `git diff --check`, and report that live display/installer interaction remains manual.
