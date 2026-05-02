# Agent Notes

This is a public open-source repository. Keep every committed file public-safe.

## Public Boundary

Do not commit:

- private/local repository names or paths
- private package IDs, launch activities, stream names, study names, or session
  IDs
- private visual-effect behavior, unpublished research logic, or private tuning
  constants
- APK/AAB payloads unless a future release explicitly approves them and Git LFS
  is configured for the cost
- signing private keys, PFX files, keystores, passwords, or generated release
  secrets
- raw captures, screenshots, generated diagnostics, or other local artifacts

Use generic language such as "target app", "user-supplied APK", "selected
Quest", "device profile", and "runtime profile".

## Release And Third-Party Dependencies

Before changing release packaging, managed tool installs, self-update behavior,
bundled catalog APKs, scrcpy/hzdb/platform-tools handling, or video/codec
bridges:

- Make third-party tool installs explicit and user-controlled.
- Pin public-release tool/APK versions and hashes; reserve `latest` for
  explicit update checks or dev-channel convenience.
- Preserve upstream licenses/notices and write source/version/hash/license
  metadata for managed tools.
- Include release-level notices for self-contained .NET runtime files and any
  bundled native/runtime components.
- For bundled generated APKs, record source repo/tag/commit, APK SHA-256,
  signing mode, included native libraries, permissions, and debug/release
  status.
- Keep hazardous runtime profiles opt-in in GUI and CLI.
- Do not bundle WebRTC/NDI/FFmpeg/GStreamer/libx264/libx265 or SDK payloads
  without a dedicated redistribution and notices audit.

## Build And Validation

```powershell
dotnet build RustyXr.Companion.slnx
dotnet test RustyXr.Companion.slnx
npm run pages:build
powershell -ExecutionPolicy Bypass -File .\tools\app\Invoke-PublicBoundaryScan.ps1
```

Run the WPF app:

```powershell
dotnet run --project src/RustyXr.Companion.App
```

Run the CLI:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- doctor
dotnet run --project src/RustyXr.Companion.Cli -- workspace guide
```

Use the single-file desktop launcher when Windows policy blocks a raw multi-file
dev output:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Start-Desktop-App.ps1
```

Use the separate dev install when testing user-facing WPF flows without
touching the published release install:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\Install-DevDesktopApp.ps1 -Launch
```

## Architecture Rules

- Keep external device/tool integrations behind services.
- Keep no-hardware diagnostics working.
- Prefer public Rusty XR contracts or schemas when they become stable.
- Keep Windows app shell, CLI, installers, and Quest device operations in this
  repo rather than in the Rusty XR core repo.
- Keep docs clear enough for someone who has never seen the author's local
  workspace.
- Keep the public release install and dev install separate:
  `%LOCALAPPDATA%\Programs\RustyXrCompanion` is release-only and may
  auto-update from GitHub Releases; `%LOCALAPPDATA%\Programs\RustyXrCompanionDev`
  is for local feature work and must not auto-update from public releases.

## Source Workspace Rule

When the public Rusty XR core repo is also installed, prefer this sibling
layout:

```text
<workspace>\Rusty-XR
<workspace>\Rusty-XR-Companion-Apps
```

Use the CLI guide before changing catalog or build-flow docs:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- workspace guide --root <workspace>
```

The companion owns managed `adb`, `hzdb`, `scrcpy`, install, launch, cast,
diagnostics, and catalog verification. Rusty XR owns the public Rust contracts,
schemas, examples, and APK source. Do not copy APK bytes, signing material,
diagnostics bundles, screenshots, media frames, or local caches into either
repo.
