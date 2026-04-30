# Rusty XR Companion Release Agent Notes

This folder ships with the released Rusty XR Companion app and CLI. It is the
installed-app onboarding file for local agents.

## Public Boundary

Keep all committed source files and shared artifacts public-safe. Do not put
private/local repository names or paths, private package IDs, signing material,
headset serials, generated diagnostics, screenshots, media frames, APK bytes,
or local caches into public repos.

Use generic language such as "target app", "selected Quest", "runtime
profile", "device profile", and "downstream app".

## What The Release Provides

The released app and CLI provide:

- Quest discovery through ADB.
- managed Android platform-tools / `adb`.
- managed Meta `hzdb`.
- managed `scrcpy`.
- APK install, launch, stop, catalog runtime-profile launch, and verification.
- diagnostics bundles, logcat capture, screenshots, and media receiver output.
- `workspace guide` and `workspace status` CLI commands.

The release does not ship Rust/Cargo, Android SDK/NDK/JDK, OpenXR loader
binaries, signing identity, or source checkouts. Those are required only when a
machine needs to build new APK bytes from source.

## Source Workspace Shape

When both public repos are installed, use this sibling layout:

```text
<workspace>\Rusty-XR
<workspace>\Rusty-XR-Companion-Apps
```

Run:

```powershell
.\RustyXr.Companion.Cli.exe workspace guide --root <workspace>
```

If running from a Companion source checkout instead:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- workspace guide --root <workspace>
```

The guide reports the expected catalog paths, APK output paths, and companion
verification commands for the public Rusty XR examples.

## Agent Workflow

1. Check whether the user is working from a release install, the CLI zip, or a
   source checkout.
2. Run `workspace guide --root <workspace>` when the user has or wants both
   repos.
3. Use the companion-managed tooling install before asking for manual `adb`,
   `hzdb`, or `scrcpy` setup.
4. Build Rusty XR APKs only from the Rusty XR source repo.
5. Install, launch, and verify APKs only through Companion catalog commands
   unless the user asks for lower-level ADB commands.
6. Keep generated APKs, diagnostics, screenshots, logcat files, media frames,
   and caches in ignored folders.

## Useful Commands

```powershell
.\RustyXr.Companion.Cli.exe tooling install-official
.\RustyXr.Companion.Cli.exe devices
.\RustyXr.Companion.Cli.exe workspace guide --root <workspace>
.\RustyXr.Companion.Cli.exe catalog list --path <catalog.json>
.\RustyXr.Companion.Cli.exe catalog verify --path <catalog.json> --app <id> --serial <serial> --install --launch --out <output-folder>
```
