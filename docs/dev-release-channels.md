---
title: Dev And Release Channels
nav_order: 10
---

# Dev And Release Channels

Rusty XR Companion has two local Windows channels.

## Published Release

The public setup helper installs the published app under:

```text
%LOCALAPPDATA%\Programs\RustyXrCompanion
```

That install is the customer-facing app. It is updated only from GitHub
Releases. The app checks the latest public release on startup and, when a newer
portable app zip is available, launches a small updater that replaces this
install root and restarts the app.

The release launcher with the Companion icon is created at:

```text
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Rusty XR Companion\Rusty XR Companion.lnk
```

The release app zip also carries the bundled public Rusty XR Quest camera
composite-layer catalog and APK under the install root's `catalogs\` folder.
Because auto-update replaces the install root from the latest app zip, updated
catalog/APK payloads arrive through the same release update path.

The release install registers a per-user Windows uninstall entry. Uninstalling
the release channel removes `%LOCALAPPDATA%\Programs\RustyXrCompanion` and the
release Start Menu shortcut. Managed tooling, diagnostics, and APK caches under
`%LOCALAPPDATA%\RustyXrCompanion` are kept unless the setup helper's purge
option is selected.

The setup helper and docs always use `releases/latest/download/...` URLs, so a
new GitHub release becomes the source of truth for both new installs and
existing release installs.

## Dev Install

The dev app is for source work and should not be confused with the published
release. Install it with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Install-DevDesktopApp.ps1 -Launch
```

The dev install lives under:

```text
%LOCALAPPDATA%\Programs\RustyXrCompanionDev
```

It creates a separate `Rusty XR Companion Dev` Start Menu shortcut and does not
auto-update from GitHub Releases. Re-run the script whenever you want the local
dev install to reflect the current source tree.

Release uninstall does not remove this dev install or the `Rusty XR Companion
Dev` shortcut.

## Source Run

For quick development loops, use:

```powershell
dotnet run --project src\RustyXr.Companion.App
```

or the stable source-built launcher:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

Source and dev runs display their channel in the app header and keep release
auto-update disabled.

## Release Rule

Committing and pushing to the repository does not update customer machines.
Only a tagged GitHub release updates the public download assets. Once a release
is published, release-channel installs pick it up on the next app launch.
