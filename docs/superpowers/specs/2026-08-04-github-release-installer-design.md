# GitHub Release Installer Design

## Goal

Build and attach a Windows installer whenever a GitHub Release is published.

## Release flow

- Maintainer creates a GitHub Release from a semantic version tag such as `v1.2.3` and uses GitHub's generated release notes.
- A `release: published` workflow checks out that exact tag on `windows-2025`.
- The workflow accepts `v1.2.3` or `1.2.3`, passes `1.2.3` to the existing `packaging/build-installer.ps1`, and rejects other tag formats.
- The existing script verifies bundled payload hashes, publishes the self-contained x64 app, and compiles the Inno Setup installer.
- The workflow attaches `VirtuaDisplay-Setup-1.2.3.exe` and its SHA-256 checksum to the same release.

## Security and failure behavior

- Workflow permissions are limited to `contents: write`, required to upload release assets.
- The tag is passed through environment variables and validated before use.
- Build or payload verification failure stops asset upload and leaves the workflow visibly failed.
- No secrets, signing keys, deployment environment, or third-party release action are required.

## Scope

Installer releases do not run on every `main` merge. Pull requests feed GitHub generated release notes; publishing a release is the explicit distribution boundary.

## Verification

- Parse the workflow YAML locally.
- Run the existing installer build locally with a valid semantic version.
- Confirm the expected installer and checksum naming logic.
- After push, confirm GitHub recognizes the workflow on `main`.
