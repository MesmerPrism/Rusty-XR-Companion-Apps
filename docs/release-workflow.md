---
title: Release Workflow
nav_order: 9
---

# Release Workflow

The release lane is portable and does not commit APK payloads. The app source
tree carries catalog metadata only. During release packaging, the workflow
downloads the public Rusty XR Quest camera composite-layer APK from a release
asset and copies it into the portable app payload beside the default catalog.
Published release installs and local dev installs are intentionally separate.

The GitHub workflow:

1. builds and tests the .NET solution
2. builds the GitHub Pages site
3. publishes the WPF app as a self-contained win-x64 app
4. publishes the CLI as a self-contained win-x64 app
5. downloads and validates the public Rusty XR composite-layer APK
6. copies that APK into `artifacts/app-win-x64/catalogs/apks`
7. publishes the guided setup helper
8. signs the setup helper when signing secrets are configured
9. copies the setup helper into the app payload as
   `RustyXrCompanion-Uninstall.exe`
10. validates the copied uninstall helper with the same signing check when a
   preview certificate is configured
11. creates app and CLI zips
12. writes `SHA256SUMS.txt`
13. uploads release assets

The published WPF app and CLI receive the release version during `dotnet
publish`. Release-channel installs compare that version to the latest GitHub
Release tag on startup and update from `RustyXrCompanion-win-x64.zip` only
after a release is published.

The portable app zip includes `RustyXrCompanion-Uninstall.exe`, copied from the
guided setup helper after signing. That keeps Windows Settings uninstall on the
same validated preview certificate path as the guided installer without adding
a separate binary asset.

The portable app zip also includes:

- `catalogs/rusty-xr-quest-composite-layer.catalog.json`
- `catalogs/apks/rusty-xr-quest-composite-layer-debug.apk`

The WPF app auto-loads this catalog on startup and defaults to the accepted
`camera-stereo-gpu-composite` runtime profile. The APK URL comes from the
workflow dispatch `composite_apk_url` input, the
`RUSTY_XR_COMPOSITE_APK_URL` repository variable, or the default latest Rusty
XR release asset URL, in that order.

## Signing Secrets

Optional secrets:

```text
WINDOWS_PREVIEW_SETUP_CERTIFICATE_BASE64
WINDOWS_PREVIEW_SETUP_CERTIFICATE_PASSWORD
```

Optional variable:

```text
WINDOWS_PREVIEW_SETUP_TIMESTAMP_URL
RUSTY_XR_COMPOSITE_APK_URL
```

Generate or export a certificate with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\New-Preview-SigningCertificate.ps1
```

Do not commit PFX files or passwords.

## Why Portable First

This version does not need Windows package identity. Portable release assets
keep the public setup simple while still allowing a generated public example
APK payload to ship with the installed app.

Future MSIX support can reuse the same public docs structure and signing
posture when package identity becomes useful.

## Dev Channel

Use a separate local dev install for feature work:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Install-DevDesktopApp.ps1 -Launch
```

This writes to `%LOCALAPPDATA%\Programs\RustyXrCompanionDev`, creates a
`Rusty XR Companion Dev` shortcut, and disables public release auto-updates.
