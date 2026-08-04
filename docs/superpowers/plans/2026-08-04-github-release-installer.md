# GitHub Release Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and attach the existing Inno Setup installer and a SHA-256 checksum whenever a GitHub Release is published.

**Architecture:** One GitHub Actions workflow handles the `release: published` event. It validates the release tag, invokes the existing packaging script on a GitHub-hosted Windows runner, creates a checksum, and uploads both files with GitHub CLI.

**Tech Stack:** GitHub Actions, PowerShell 7, .NET 10, Inno Setup 6, GitHub CLI

## Global Constraints

- Release tags must be `vMAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH`.
- Build the exact release tag on `windows-2025`.
- Reuse `packaging/build-installer.ps1`; do not duplicate packaging logic.
- Grant only `contents: write`.
- Add no secret, signing, deployment, or third-party release dependency.

---

### Task 1: Publish installer assets

**Files:**
- Create: `.github/workflows/release-installer.yml`

**Interfaces:**
- Consumes: `github.event.release.tag_name` and `packaging/build-installer.ps1 -Version <MAJOR.MINOR.PATCH>`
- Produces: release assets `VirtuaDisplay-Setup-<version>.exe` and `VirtuaDisplay-Setup-<version>.exe.sha256`

- [ ] **Step 1: Add the release workflow**

```yaml
name: Release installer

on:
  release:
    types: [published]

permissions:
  contents: write

jobs:
  build-and-upload:
    runs-on: windows-2025
    timeout-minutes: 20
    steps:
      - name: Check out release tag
        uses: actions/checkout@v7
        with:
          ref: ${{ github.event.release.tag_name }}

      - name: Set up .NET
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: 10.0.x

      - name: Validate release version
        id: version
        shell: pwsh
        env:
          RELEASE_TAG: ${{ github.event.release.tag_name }}
        run: |
          if ($env:RELEASE_TAG -notmatch '^v?(\d+\.\d+\.\d+)$') {
            throw "Release tag must be vMAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH: $env:RELEASE_TAG"
          }
          "value=$($Matches[1])" >> $env:GITHUB_OUTPUT

      - name: Build installer
        shell: pwsh
        run: .\packaging\build-installer.ps1 -Version '${{ steps.version.outputs.value }}'

      - name: Create checksum
        shell: pwsh
        env:
          VERSION: ${{ steps.version.outputs.value }}
        run: |
          $installer = "artifacts\VirtuaDisplay-Setup-$env:VERSION.exe"
          $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
          "$hash  $(Split-Path -Leaf $installer)" | Set-Content -NoNewline -Encoding ascii "$installer.sha256"

      - name: Upload release assets
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
          RELEASE_TAG: ${{ github.event.release.tag_name }}
          VERSION: ${{ steps.version.outputs.value }}
        run: |
          $installer = "artifacts\VirtuaDisplay-Setup-$env:VERSION.exe"
          gh release upload $env:RELEASE_TAG $installer "$installer.sha256" --clobber
```

- [ ] **Step 2: Parse and inspect the workflow**

Run:

```powershell
python -c "import pathlib,yaml; yaml.safe_load(pathlib.Path('.github/workflows/release-installer.yml').read_text())"
git diff --check
```

Expected: both commands exit successfully with no output.

- [ ] **Step 3: Build the installer locally**

Run:

```powershell
.\packaging\build-installer.ps1 -Version 0.0.0
```

Expected: exit code 0, payload verification succeeds, and `artifacts\VirtuaDisplay-Setup-0.0.0.exe` exists.

- [ ] **Step 4: Verify release asset naming**

Run:

```powershell
$installer = 'artifacts\VirtuaDisplay-Setup-0.0.0.exe'
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $installer)" | Set-Content -NoNewline -Encoding ascii "$installer.sha256"
Get-Item $installer, "$installer.sha256" | Select-Object Name, Length
```

Expected: installer and non-empty checksum file have the workflow's release asset names.

- [ ] **Step 5: Commit and push**

```powershell
git add .github/workflows/release-installer.yml docs/superpowers/plans/2026-08-04-github-release-installer.md
git commit -m "ci: publish installer with GitHub releases"
git push origin main
```

- [ ] **Step 6: Verify GitHub recognizes the workflow**

Run:

```powershell
gh workflow view release-installer.yml --repo fjdiazt/virtua-display --yaml
git status --short --branch
```

Expected: GitHub returns the workflow YAML and local `main` matches `origin/main` with a clean tree.
