# Standalone Installer Test Checklist

Artifact: `artifacts\VirtuaDisplay-Setup-0.1.0.exe`

Automated local checks completed on 2026-08-04:

- Release build: pass, 0 warnings and 0 errors
- Self-test: pass
- Installed-driver probe: `Ready`, SudoVDA protocol 0.2.1
- Vendored payload hashes: pass
- Driver DLL, catalog, and nefcon Authenticode signatures: valid
- Self-contained publish and Inno Setup compilation: pass

Manual checks are intentionally left for the maintainer.

| Scenario | OS build | Prior driver | Installer result | Driver status | Display start/stop | Apollo result | Uninstall result | Notes |
|---|---|---|---|---|---|---|---|---|
| Windows 10 x64 clean | Pending | None | Pending | Pending | Pending | N/A | Pending | |
| Windows 11 x64 clean | Pending | None | Pending | Pending | Pending | N/A | Pending | |
| Apollo-first compatible | Pending | Apollo SudoVDA | Pending | Pending | Pending | Pending | Pending | Confirm no duplicate device node. |
| Virtua Display first, then Apollo | Pending | None | Pending | Pending | Pending | Pending | Pending | Confirm both apps reuse the compatible driver. |
| Repair | Pending | Compatible SudoVDA | Pending | Pending | Pending | N/A | N/A | |
| Upgrade over same AppId | Pending | Compatible SudoVDA | Pending | Pending | Pending | N/A | N/A | |
| Uninstall | Pending | Compatible SudoVDA | Pending | N/A | N/A | Pending | Pending | Confirm driver and certificate remain. |
| UAC denied | Pending | None | Pending | N/A | N/A | N/A | N/A | Confirm no changes. |
| Incompatible protocol | Pending | Incompatible SudoVDA | Pending | Pending | N/A | N/A | N/A | Confirm setup stops before driver commands. |
| Modified payload hash | Pending | None | Pending | N/A | N/A | N/A | N/A | Confirm setup reports bundled-driver verification failure. |
