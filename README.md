<p align="center">
  <img src="assets/logo.png" alt="Virtua Display logo" width="480">
</p>

# Virtua Display

A small Windows app for creating and managing one temporary virtual display.

![Virtua Display](docs/images/virtua-display.png)

## Features

- Choose an aspect ratio, resolution, and refresh rate.
- Match the primary display or enter a custom resolution.
- Lock the aspect ratio while editing width or height.
- Place the virtual display centered above the primary display.
- Optionally make it primary and route new windows to it.
- Move windows back and restore the previous layout when stopped.
- Start with Windows, minimize to the notification area, or close to the notification area.

## Install

Virtua Display supports Windows 10 and Windows 11 on x64 PCs.

Download and run `VirtuaDisplay-Setup-<version>.exe`. The installer includes the app and installs SudoVDA when needed. Apollo is not required.

The installer is unsigned, so Windows SmartScreen may show a warning. A compatible SudoVDA installation from Apollo is reused. Setup stops if it finds an incompatible version. Uninstalling Virtua Display leaves the shared SudoVDA driver and certificate installed.

## Usage

1. Open **Virtua Display**.
2. Choose a resolution preset, or enter a custom width and height.
3. Select the refresh rate and any wanted display behavior.
4. Select **Start**.
5. Select **Stop** when finished.

Closing the app normally removes its virtual display and restores the previous display layout.

**Start with Windows** launches the app, not the virtual display. The notification-area icon can open the app, start or stop the display, and exit. The Minimize and Close buttons hide the app only when their matching notification-area options are enabled.

## Limitations

- Only one virtual display is supported.
- Window routing applies only to newly created top-level windows.
- Elevated, protected, and system windows may not move.
- Force-closing can leave the display active briefly. If it still exists when Virtua Display reopens, the app reconnects automatically; otherwise SudoVDA removes it through its watchdog.

## Building from source

Install the .NET 10 SDK, then run:

```powershell
dotnet build src\Virtua.Display\Virtua.Display.csproj -c Release
```

To build the installer, install Inno Setup 6 and run:

```powershell
.\packaging\build-installer.ps1 -Version 0.1.0
```

Generated files are written under `artifacts`.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled dependency notices.
