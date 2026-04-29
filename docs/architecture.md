---
title: Architecture
nav_order: 7
---

# Architecture

Rusty XR Companion uses a thin-shell architecture.

```text
WPF app / CLI
  -> Companion Core
  -> Diagnostics layer
  -> ADB, optional Quest tooling, optional casting tools
```

## Projects

- `RustyXr.Companion.Core`
  - public models
  - tooling discovery
  - ADB command services
  - catalog loading
  - scrcpy launch wrapper
- `RustyXr.Companion.Diagnostics`
  - Windows environment analysis
  - diagnostics bundle writer
- `RustyXr.Companion.Cli`
  - scriptable command surface
- `RustyXr.Companion.App`
  - WPF operator app
- `RustyXr.Companion.PreviewInstaller`
  - guided portable-release setup helper

## Design Rules

- no-hardware diagnostics must stay useful
- external processes are launched through services
- live-device commands require an explicit serial when more than one device is
  present
- APK package IDs are user or catalog data, not private hard-coded defaults
- Rusty XR core contracts should be consumed when stable, but app-shell release
  logic stays here
