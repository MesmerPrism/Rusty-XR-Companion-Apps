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

The helper downloads the latest portable app zip, installs it under
`%LOCALAPPDATA%\Programs\RustyXrCompanion`, refreshes the managed Quest tooling
cache, creates the Start Menu launcher at
`%APPDATA%\Microsoft\Windows\Start Menu\Programs\Rusty XR Companion\Rusty XR Companion.lnk`,
registers a per-user Windows uninstall entry, and launches the app. The setup
window shows both paths before installation starts.

The portable app zip includes a bundled public Rusty XR Quest camera
composite-layer APK and catalog. On first launch, the app auto-loads that
catalog so the APK target is already present for install and launch on a
connected Quest.

Both the portable app zip and CLI zip include an `agent-onboarding\` folder.
Use `agent-onboarding\AGENTS.md` as the installed-app instruction file for a
local agent. It is available even when the source repos have not been checked
out yet.

The setup window also points source builders toward the recommended sibling
workspace shape:

```text
<workspace>\Rusty-XR
<workspace>\Rusty-XR-Companion-Apps
```

That source layout is optional for published-app users, but it is the supported
way for local agents to build Rusty XR APKs from source and verify them through
the companion catalog workflow. See [Source Workspace](source-workspace.md).

Installed release-channel apps check GitHub Releases on startup and replace
the local release install when a newer portable app zip is published.

If the helper cannot reach an upstream tooling source, the app install still
completes and the **Install / Update Managed Tooling** button can retry later.

## Uninstall

Use **Windows Settings > Apps > Installed apps > Rusty XR Companion >
Uninstall** for the normal uninstall path. This removes the published release
install under `%LOCALAPPDATA%\Programs\RustyXrCompanion`, the Start Menu
shortcut, and the Windows uninstall entry.

The setup helper can also uninstall the app. Download
`RustyXrCompanion-Setup.exe`, open it, and choose **Uninstall**. By default,
uninstall keeps managed Quest tooling, APK caches, diagnostics, screenshots,
and media captures under `%LOCALAPPDATA%\RustyXrCompanion`. Select the purge
checkbox only when you also want to remove that local cache.

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
