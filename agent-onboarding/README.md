# Rusty XR Companion Agent Onboarding

This folder is included in the released Windows app zip and CLI zip. It gives a
local agent enough context to operate the released Companion tools and, when
source repos are present, to use the full Rusty XR source APK workflow.

Start with:

- `AGENTS.md` for agent rules and public-boundary guidance.
- `source-workspace.md` for the sibling Rusty XR + Companion source workflow.

The installed app and CLI can manage Quest operator tooling, install APKs,
launch catalog runtime profiles, capture diagnostics, and print a workspace
guide. Building new APK bytes from source still requires a Rusty XR checkout
plus local Rust and Android build tooling.

From the CLI folder:

```powershell
.\RustyXr.Companion.Cli.exe workspace guide --root <workspace>
```

From a source checkout:

```powershell
dotnet run --project .\src\RustyXr.Companion.Cli -- workspace guide --root <workspace>
```
