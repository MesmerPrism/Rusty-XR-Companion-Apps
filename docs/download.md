---
title: Download
nav_order: 8
---

# Download

Public releases are published through GitHub Releases and linked from GitHub
Pages.

## Guided Setup

Use the guided setup helper when a release is available:

[Download RustyXrCompanion-Setup.exe](https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases/latest/download/RustyXrCompanion-Setup.exe)

The helper downloads the latest portable app zip, installs it under the user's
LocalAppData programs folder, refreshes the managed Quest tooling cache, creates
a Start Menu shortcut, and launches the app. Installed release-channel apps
check GitHub Releases on startup and replace the local release install when a
newer portable app zip is published.

If the helper cannot reach an upstream tooling source, the app install still
completes and the **Install / Update Managed Tooling** button can retry later.

## Direct Assets

- [Portable app zip](https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases/latest/download/RustyXrCompanion-win-x64.zip)
- [CLI zip](https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases/latest/download/rusty-xr-companion-cli-win-x64.zip)
- [Checksums](https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases/latest/download/SHA256SUMS.txt)
- [Release page](https://github.com/MesmerPrism/Rusty-XR-Companion-Apps/releases)

## Signing

The release workflow can sign the setup helper when certificate secrets are
configured. A self-signed preview helper can still be blocked by Windows
reputation policy on some machines. If that happens, use the portable zip or
build from source.

MSIX and `.appinstaller` support are planned for a later release once the app
needs Windows package identity.

Development builds use a separate dev install path and never auto-update from
public releases. See [Dev And Release Channels](dev-release-channels.md).
