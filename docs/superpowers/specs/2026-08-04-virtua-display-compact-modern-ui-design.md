# Virtua Display Compact Modern UI Design

## Goal

Replace the utilitarian dark WPF form with a deliberate, modern, compact interface matching Wallppr's polish while preserving Virtua Display's focused handful of inputs and every existing behavior.

## Visual Direction

- Native .NET 10 WPF only; no UI framework or compatibility layer.
- One cohesive dark surface, not a dashboard and not a card grid.
- Near-black window background with one subtle elevated header surface.
- Purple accent, `Segoe UI Variable Text`, and `Segoe Fluent Icons`.
- Rounded custom control templates with complete hover, pressed, focused, checked, and disabled states.
- Clear hierarchy through typography, spacing, and thin section dividers instead of `GroupBox` chrome.
- No drop shadows, decorative panels, gradients, oversized window, or visual effects without a functional purpose.

## Window Layout

The window remains fixed-width, height-to-content, centered, and minimizable.

1. Header: `Virtua Display`, a compact `POC` badge, and the subtitle `One focused virtual display.`
2. Display section: aspect-ratio filter, resolution preset, width, height, aspect-lock icon button, and refresh rate.
3. Display behavior section: `Make primary` and `Route new windows` checkboxes.
4. Application behavior section: `Start with Windows`, `Minimize to notification area`, and `Close to notification area` checkboxes.
5. Footer: compact status pill and visually dominant Start/Stop button.

Sections use uppercase micro-headings and spacing. Thin separators establish grouping. There are no cards or group boxes.

## Control System

- Text boxes and combo boxes use an 8px corner radius, 38px minimum height, quiet border, dark fill, and purple focus border.
- The primary Start/Stop button uses a 9px radius, semibold text, purple fill, and clear hover/pressed/disabled states.
- Checkboxes use a custom rounded-square indicator with a Fluent check glyph while retaining native keyboard and automation behavior.
- The aspect-ratio lock remains one toggle button between dimensions and refresh rate. It uses Fluent lock/unlock glyphs, tooltip text, and automation names instead of emoji.
- Labels remain associated with inputs through WPF `Target` bindings.
- Existing tab order and accessible automation IDs remain intact.

## Behavior Boundaries

This is a presentation rewrite only. It does not change:

- resolution filtering, ordering, validation, or aspect locking;
- display creation, positioning, primary-display selection, or cleanup;
- new-window routing or stop-time window relocation;
- settings persistence and defaults;
- notification-area behavior, startup registration, single-instance behavior, or driver probing;
- installer behavior or dependencies.

## Verification

- Update the existing WPF self-test to assert the new section structure and named control templates.
- Retain all functional settings, resolution, state-color, tray, and aspect-lock checks.
- Run a Release build and `--self-test`.
- Capture the actual rendered window, inspect it, and replace the README screenshot only when the new render is correct.
- Rebuild the standalone installer after the UI passes.
- Live display creation and installer interaction remain manual tests by the maintainer.
