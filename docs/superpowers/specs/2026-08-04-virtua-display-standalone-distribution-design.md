# Virtua Display Standalone Distribution Design

## Goal

Rename SudoVDA GUI to **Virtua Display** and distribute it as a simple Windows application that installs and uses SudoVDA without requiring Apollo.

## Product Identity

- Product and window title: `Virtua Display`
- Repository: `virtua-display`
- Executable and assembly: `VirtuaDisplay.exe`
- Root namespace: `Virtua.Display`
- Per-user settings key: `HKCU\Software\Virtua\Display`
- Startup entry, notification icon, shortcuts, installer entries, and single-instance identity use `Virtua Display`.
- Keep `SudoVDA` only where the underlying driver must be identified: driver status, diagnostics, logs, bundled licenses, and credits.

Perform a clean product rename before the first standalone installer. Remove stale `SudoVDA GUI` startup registration during first launch or installation, but do not maintain general backwards compatibility for the proof of concept.

## Distribution

Use **Inno Setup** to produce one conventional x64 installer. Publish the WPF application as self-contained so users do not install a separate .NET runtime.

Bundle pinned copies of:

- Virtua Display application files
- SudoVDA driver package
- SudoVDA signing certificate required by that package
- Nefcon driver-management utility
- applicable licenses and notices
- a manifest containing pinned versions and SHA-256 hashes

The installer itself may remain unsigned for the proof of concept. Windows SmartScreen can therefore warn users. Do not weaken Windows security settings or automate bypassing that warning.

## Driver Ownership and Apollo Coexistence

Before installation, detect the installed SudoVDA driver through the same driver-status capability used by the application.

- No SudoVDA driver: request elevation and install the bundled driver package.
- Compatible SudoVDA driver already present, including one installed by Apollo: reuse it unchanged.
- Incompatible SudoVDA driver present: stop with a clear error showing installed and required versions. Do not silently replace, downgrade, or remove it.

Virtua Display does not claim exclusive ownership of the shared driver. Uninstalling Virtua Display removes only the application, shortcuts, startup entry, and application settings. It leaves the SudoVDA driver and certificate installed so Apollo or another client is not broken.

## Application Behavior

Move driver discovery and compatibility reporting behind the existing SudoVDA control path. Application startup reports one of three actionable states: ready, driver missing, or incompatible driver. Normal display creation and removal remain unchanged after a compatible driver is available.

Do not add an updater, background service, package manager, telemetry, or a separate dependency installer. One elevated installer and the existing lightweight per-user application are sufficient.

## Failure Handling

- Verify bundled hashes before driver installation.
- Abort if elevation is denied, files fail verification, or driver installation fails.
- Show a concise recovery message and preserve any existing driver installation.
- Log installer diagnostics without recording personal data.
- Do not continue with a partially installed or unknown driver state.

## Verification

- Build and run application self-tests under the new identity.
- Build a clean self-contained x64 publish and installer.
- Verify install, launch, virtual-display start/stop, repair, upgrade, and uninstall on supported Windows 10 and Windows 11 systems.
- Verify a clean system with no Apollo installation.
- Verify Apollo-first installation reuses its compatible SudoVDA driver and Apollo still works after Virtua Display uninstall.
- Verify Virtua-Display-first installation does not collide with a later compatible Apollo installation.
- Verify incompatible-driver, denied-UAC, hash-failure, and driver-install-failure paths abort safely.
- Confirm the repository, README, screenshots, executable metadata, shortcuts, notification icon, settings, and visible UI consistently say `Virtua Display`.

## Explicitly Deferred

- Removing SudoVDA itself or writing a new virtual-display driver
- Driver upgrades or removal from the Virtua Display uninstaller
- Code-signing certificates or paid distribution services
- Automatic updates and Microsoft Store/MSIX packaging
- Compatibility migration beyond removing the obsolete startup entry
