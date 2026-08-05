# Application Settings Dialog Design

## Goal

Remove application-lifecycle options from the compact main window and place them in a dedicated Settings window that follows Wallppr's wording and interaction pattern.

## Chosen approach

Use one modeless Settings window owned by `App`. The main header gets a compact gear-and-label **Settings** button. Repeated requests restore and activate the existing window instead of opening duplicates. A modal dialog would unnecessarily block display controls; a flyout would not provide the requested separate dialog.

## Main window

- Keep display resolution, display behavior, status, and Start/Stop controls.
- Remove the **APPLICATION** section and its three checkboxes.
- Place **Settings**, with the Segoe Fluent Icons gear glyph, at the right side of the header.
- Raise one `SettingsRequested` event; `App` owns window creation and lifetime.

## Settings window

Use the same section and wording pattern as Wallppr, adapted only for the product name:

- **Start Virtua Display with Windows** — “Launch for your account when you sign in.”
- **Minimize to notification area** — “Hide the window when minimized.”
- **Keep running when closed** — “The close button hides Virtua Display in the notification area.”

Each row uses a keyboard-focusable switch. The footer says **Settings save immediately** and shows either **Saved** or an inline error. The dialog uses the existing dark resources, icon, typography, and title-bar treatment.

## State and persistence

`MainWindow` remains the single owner of the loaded `UserSettings`, current startup-registration state, tray behavior, and existing injected test seams. It exposes narrow application-setting properties and mutation methods to `SettingsWindow`.

Tray-setting mutations update `_lastValidSettings` and call the existing settings writer immediately. A failed write restores the previous in-memory value and leaves the switch at the persisted state. Startup registration changes use the existing registration callback; failure also restores the visible state. Resolution and display-behavior persistence remain unchanged.

## Window lifecycle

`App` creates the Settings window on demand. When the main window is visible, Settings is owned and centered over it; otherwise it centers on screen. Closing Settings only closes that dialog. Closing or exiting the application closes Settings before shutdown.

The window keeps a fixed width and uses WPF `SizeToContent="Height"`. No fixed or minimum height is applied, so every option fits when opened without requiring resizing or a scrollbar.

## Verification

- Extend self-tests for exact visible wording, gear accessibility name, singleton request seam, immediate persistence, failure rollback, and retained defaults.
- Run Release build and `--self-test`; do not start a virtual display.
- Launch the real main window, inspect its rendered layout, and replace `docs/images/virtua-display.png` with the accepted main-window-only capture.
- Rebuild the installer after the UI and screenshot pass.
