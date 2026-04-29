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
