# Virtua Display Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the application and repository from SudoVDA GUI to Virtua Display while retaining SudoVDA only as the underlying driver name.

**Architecture:** Rename the existing WPF project in place; do not add compatibility layers. Product-facing identifiers become `Virtua Display`, code uses `Virtua.Display`, and driver-facing diagnostics retain `SudoVDA`.

**Tech Stack:** .NET 10, WPF, C#, Windows Registry, GitHub CLI

## Global Constraints

- Product and window title: `Virtua Display`.
- Repository: `virtua-display`.
- Executable and assembly: `VirtuaDisplay.exe`.
- Root namespace: `Virtua.Display`.
- Per-user settings key: `HKCU\Software\Virtua\Display`.
- Keep `SudoVDA` only in driver status, diagnostics, logs, bundled licenses, and credits.
- Remove the obsolete `SudoVDA GUI` startup value; do not migrate old proof-of-concept settings.
- Add no dependencies.

---

### Task 1: Rename the project and code identity

**Files:**
- Rename: `src/SudoVDA.GUI/` to `src/Virtua.Display/`
- Rename: `src/Virtua.Display/SudoVDA.GUI.csproj` to `src/Virtua.Display/Virtua.Display.csproj`
- Modify: every `.cs` and `.xaml` file under `src/Virtua.Display/`
- Test: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Consumes: existing WPF application without behavior changes.
- Produces: project `src/Virtua.Display/Virtua.Display.csproj`, assembly `VirtuaDisplay`, namespace `Virtua.Display`.

- [ ] **Step 1: Change identity assertions first**

Change the leading self-test assertions to:

```csharp
Check(Assembly.GetExecutingAssembly().GetName().Name == "VirtuaDisplay", "assembly name");
Check(StartupRegistration.BuildCommand(@"C:\Apps\VirtuaDisplay.exe") ==
      "\"C:\\Apps\\VirtuaDisplay.exe\" --startup",
    "startup command");
```

Change UI assertions to `Virtua Display`, `Open Virtua Display`, and `Virtua Display Smoke Window`.

- [ ] **Step 2: Run the old project and verify the identity checks fail**

Run:

```powershell
dotnet run --project src\SudoVDA.GUI\SudoVDA.GUI.csproj -c Release -- --self-test
```

Expected: nonzero exit with identity failures.

- [ ] **Step 3: Rename the project and namespaces**

Use `git mv` for the directory and project file. Set the project identity:

```xml
<AssemblyName>VirtuaDisplay</AssemblyName>
<RootNamespace>Virtua.Display</RootNamespace>
<Product>Virtua Display</Product>
<Description>Simple Windows virtual display controller.</Description>
```

Replace `namespace SudoVDA.GUI;` with `namespace Virtua.Display;` and update both XAML class names:

```xml
x:Class="Virtua.Display.App"
x:Class="Virtua.Display.MainWindow"
```

Change `MainWindow.xaml` title to `Virtua Display`. Change the smoke-window title to `Virtua Display Smoke Window`.

- [ ] **Step 4: Rename product-owned runtime identifiers**

Use these exact values:

```csharp
private const string SingleInstanceName = @"Local\Virtua.Display";
```

```csharp
WriteAscii(input.DeviceName, 14, "VirtuaDisplay");
WriteAscii(input.SerialNumber, 14, "VD0001");
```

Do not rename `SudoVdaClient`, its IOCTL messages, or driver protocol output.

- [ ] **Step 5: Build and run self-test**

Run:

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
```

Expected: build succeeds with zero warnings/errors; self-test prints `Self-test passed.`

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "refactor: rename app to Virtua Display"
```

### Task 2: Rename persisted and notification-area identity

**Files:**
- Modify: `src/Virtua.Display/ResolutionSettings.cs`
- Modify: `src/Virtua.Display/StartupRegistration.cs`
- Modify: `src/Virtua.Display/App.xaml.cs`
- Modify: `src/Virtua.Display/NotificationAreaIcon.cs`
- Test: `src/Virtua.Display/SelfTest.cs`

**Interfaces:**
- Consumes: `UserSettingsStore.Load/Save`, `StartupRegistration.IsEnabled/SetEnabled`.
- Produces: settings path `Software\Virtua\Display`, startup value `Virtua Display`, `StartupRegistration.RemoveLegacy()`.

- [ ] **Step 1: Add failing persistence and startup-cleanup checks**

Assert the default path and add a temporary-key cleanup check:

```csharp
Check(UserSettingsStore.DefaultPath == @"Software\Virtua\Display", "settings registry path");

using (var key = Registry.CurrentUser.CreateSubKey(path))
    key.SetValue("SudoVDA GUI", "obsolete", RegistryValueKind.String);
StartupRegistration.RemoveLegacy(path);
using (var key = Registry.CurrentUser.OpenSubKey(path))
    Check(key?.GetValue("SudoVDA GUI") is null, "legacy startup registration removed");
```

- [ ] **Step 2: Run self-test and verify failure**

Run:

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
```

Expected: failure for the old registry path or missing `RemoveLegacy`.

- [ ] **Step 3: Implement the clean registry identity**

Set:

```csharp
internal const string DefaultPath = @"Software\Virtua\Display";
private const string ValueName = "Virtua Display";
private const string LegacyValueName = "SudoVDA GUI";
```

Add:

```csharp
internal static void RemoveLegacy(
    string registryPath = RunPath,
    string valueName = LegacyValueName)
{
    using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true);
    key?.DeleteValue(valueName, throwOnMissingValue: false);
}
```

Call `StartupRegistration.RemoveLegacy();` once in `App.OnStartup` after test/smoke argument handling and before normal window construction.

- [ ] **Step 4: Rename notification-area copy**

Set the icon tip to `Virtua Display`, menu header to `Open Virtua Display`, and product-owned exceptions to `Could not add Virtua Display...` / `Could not initialize the Virtua Display...`.

- [ ] **Step 5: Run self-test**

Run:

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
```

Expected: `Self-test passed.`

- [ ] **Step 6: Commit**

```powershell
git add src\Virtua.Display
git commit -m "feat: apply Virtua Display runtime identity"
```

### Task 3: Update end-user documentation and screenshot

**Files:**
- Modify: `README.md`
- Rename: `docs/images/sudovda-gui.png` to `docs/images/virtua-display.png`

**Interfaces:**
- Consumes: completed product rename.
- Produces: end-user README and screenshot using only Virtua Display branding; transitional Apollo requirement remains until the installer plan lands.

- [ ] **Step 1: Update README identity**

Use heading `# Virtua Display`, executable `VirtuaDisplay.exe`, project path `src\Virtua.Display\Virtua.Display.csproj`, and screenshot path `docs/images/virtua-display.png`. Describe SudoVDA as the driver. Keep the current Apollo requirement but label it temporary until standalone packaging is merged.

- [ ] **Step 2: Capture the renamed WPF window**

Run:

```powershell
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --smoke-window
```

Capture the full dark-themed window at 100% display scaling, replace `docs/images/virtua-display.png`, and confirm the title reads `Virtua Display`.

- [ ] **Step 3: Scan product-facing text**

Run:

```powershell
rg -n "SudoVDA GUI|SudoVDA-GUI|VRPrivacy|Open SudoVDA|Title=\"SudoVDA\"" README.md src docs\images
```

Expected: no matches. Driver-facing `SudoVDA` references remain allowed.

- [ ] **Step 4: Build and run self-test again**

Run:

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
dotnet run --project src\Virtua.Display\Virtua.Display.csproj -c Release -- --self-test
```

Expected: both succeed.

- [ ] **Step 5: Commit**

```powershell
git add README.md docs\images src\Virtua.Display
git commit -m "docs: publish Virtua Display identity"
```

### Task 4: Rename the GitHub repository

**Files:**
- Modify external repository name: `fjdiazt/sudovda-gui` to `fjdiazt/virtua-display`
- Modify local remote: `origin`

**Interfaces:**
- Consumes: tested and pushed rename commits.
- Produces: canonical repository URL `https://github.com/fjdiazt/virtua-display.git`.

- [ ] **Step 1: Verify clean state and push code first**

Run:

```powershell
git status --short
git push origin main
```

Expected: clean status; push succeeds.

- [ ] **Step 2: Rename the GitHub repository and remote**

Run:

```powershell
gh repo rename virtua-display --repo fjdiazt/sudovda-gui --yes
git remote set-url origin https://github.com/fjdiazt/virtua-display.git
```

- [ ] **Step 3: Verify canonical remote and build**

Run:

```powershell
git remote -v
gh repo view fjdiazt/virtua-display --json name,url
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
```

Expected: both remote directions use `fjdiazt/virtua-display.git`; GitHub reports `virtua-display`; build succeeds.

- [ ] **Step 4: Commit check**

No new commit is expected from the remote rename. Run `git status --short` and require empty output.
