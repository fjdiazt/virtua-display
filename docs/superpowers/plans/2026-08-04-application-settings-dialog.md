# Application Settings Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move application-lifecycle options into a Wallppr-style modeless Settings window and refresh the README's main-window screenshot.

**Architecture:** `MainWindow` remains the owner of loaded settings and tray behavior while exposing narrow application-setting properties and mutations. `App` owns one modeless `SettingsWindow`; the main window requests it through an event. The existing registry schema and immediate-save behavior remain unchanged.

**Tech Stack:** .NET 10, WPF XAML, C#, Windows registry, existing self-test runner

## Global Constraints

- Use Wallppr's wording adapted only for `Virtua Display`.
- Keep one modeless Settings window; repeated gear clicks activate it.
- Remove all application-lifecycle switches from the main window.
- Save each setting immediately and roll visible state back after failure.
- Keep existing registry value names and defaults.
- Do not start or mutate a virtual display during automated verification.
- README shows only the updated main window.

---

### Task 1: Define failing settings-window behavior

**Files:**
- Modify: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Consumes: `MainWindow` test constructor and existing `Find<T>` helper
- Produces: checks for `_settingsButton`, `SettingsRequested`, `SettingsWindow`, `_startWithWindowsSwitch`, `_minimizeToNotificationAreaSwitch`, and `_keepRunningWhenClosedSwitch`

- [ ] **Step 1: Replace main-window application-checkbox assertions**

Assert that `applicationSection`, `_startWithWindowsCheck`, `_minimizeToNotificationAreaCheck`, and `_closeToNotificationAreaCheck` are absent. Assert `_settingsButton` contains the Fluent gear glyph `\uE713`, has automation name `Open settings`, and raises `SettingsRequested` once when clicked.

- [ ] **Step 2: Add Settings-window assertions**

Construct `new SettingsWindow(window)` and assert exact labels:

```text
Start Virtua Display with Windows
Minimize to notification area
Keep running when closed
```

Assert the descriptive text, `Settings save immediately`, default-off states, and switch template chrome.

- [ ] **Step 3: Exercise immediate save**

Click all three switches. Require the startup callback to receive `true` and the saved `UserSettings` to contain both notification-area values as `true`. Change resolution afterward and require those settings to remain true.

- [ ] **Step 4: Run RED verification**

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
```

Expected: compilation fails because `SettingsWindow` and the new main-window settings seam do not exist.

---

### Task 2: Implement modeless Settings UI

**Files:**
- Modify: `src/Virtua.Display/App.xaml`
- Modify: `src/Virtua.Display/App.xaml.cs`
- Modify: `src/Virtua.Display/MainWindow.xaml`
- Modify: `src/Virtua.Display/MainWindow.xaml.cs`
- Create: `src/Virtua.Display/SettingsWindow.xaml`
- Create: `src/Virtua.Display/SettingsWindow.xaml.cs`

**Interfaces:**
- `MainWindow.SettingsRequested: event Action?`
- `MainWindow.StartWithWindowsEnabled: bool`
- `MainWindow.MinimizeToNotificationAreaEnabled: bool`
- `MainWindow.CloseToNotificationAreaEnabled: bool`
- `MainWindow.SetStartWithWindows(bool enabled): void`
- `MainWindow.SetMinimizeToNotificationArea(bool enabled): void`
- `MainWindow.SetCloseToNotificationArea(bool enabled): void`
- `SettingsWindow(MainWindow settingsOwner)`

- [ ] **Step 1: Move state out of main-window controls**

Store startup state in a bool field. Read notification-area state from `_lastValidSettings`. Each mutation saves before replacing `_lastValidSettings`; exceptions propagate so Settings can restore the prior switch state. Keep display-resolution persistence unchanged.

- [ ] **Step 2: Replace APPLICATION section with Settings button**

Make the header a two-column grid. Add `_settingsButton` on the right with glyph `&#xE713;`, label `Settings`, and automation name `Open settings`. Remove the application section and move the footer up one row.

- [ ] **Step 3: Add autosizing Settings window**

Create a compact dark window with fixed width, WPF `SizeToContent="Height"`, no fixed/minimum height or scrollbar, one `STARTUP &amp; BACKGROUND` section, three switch rows, exact approved wording, descriptive text, and immediate-save footer. Switch handlers call the narrow MainWindow mutations; success displays `Saved`, failure restores all switches and displays the exception.

- [ ] **Step 4: Own one dialog in App**

Subscribe to `MainWindow.SettingsRequested`. `ShowSettings` activates an existing dialog or creates one owned by the visible main window and centered appropriately. Clear the cached reference on close and close Settings during application shutdown.

- [ ] **Step 5: Run GREEN verification**

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
.\src\Virtua.Display\bin\Release\net10.0-windows\VirtuaDisplay.exe --self-test
git diff --check
```

Expected: build has zero warnings/errors, self-test passes, and diff check is clean.

---

### Task 3: Render, document, package, and publish

**Files:**
- Modify: `docs/images/virtua-display.png`

**Interfaces:**
- Consumes: built `VirtuaDisplay.exe` and existing README image path
- Produces: inspected main-window-only screenshot and rebuilt ignored installer artifact

- [ ] **Step 1: Capture the real main window**

Launch the Release app without starting a display. Capture only the main window at native scale, with Settings closed. Inspect spacing, clipping, typography, gear button, status, and control contrast.

- [ ] **Step 2: Replace README screenshot**

Write the accepted capture to `docs/images/virtua-display.png`. Do not add a Settings-window screenshot; README already references the main image.

- [ ] **Step 3: Rebuild installer**

```powershell
.\packaging\build-installer.ps1 -Version 0.1.0
```

Expected: installer build exits zero and writes `artifacts\VirtuaDisplay-Setup-0.1.0.exe`.

- [ ] **Step 4: Final verification and publication**

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release --no-restore
.\src\Virtua.Display\bin\Release\net10.0-windows\VirtuaDisplay.exe --self-test
git diff --check
git status --short --branch
```

Review the exact diff, commit scoped files, push `main`, verify local and remote hashes match, and require a clean tree.
