---
title: Git LFS And Assets
nav_order: 10
---

# Git LFS And Assets

This repo includes Git LFS patterns for APK and AAB files, but the source
tree deliberately does not commit APK bytes.

## Default Policy

- Use local APK paths during development.
- Use public catalog JSON for metadata.
- Use GitHub Releases for large public binaries when possible.
- Bundle generated public APKs in release artifacts, not in source, when the
  release workflow needs an offline install path.
- Keep source checkout size small.
- Keep the app useful without requiring contributors to download LFS payloads.

## When To Use Git LFS

Use Git LFS only when the project explicitly decides to keep a curated public
APK in source:

```powershell
git lfs install
git lfs track "*.apk"
git lfs track "*.aab"
```

Before doing that, confirm:

- the APK can legally be redistributed
- the storage and bandwidth budget is acceptable
- the docs explain why the payload is bundled
- the release also publishes hashes and update notes

## How To Avoid LFS Costs

For this release lane:

- keep `samples/quest-session-kit/APKs/*.apk` ignored
- use `samples/quest-session-kit/apk-catalog.example.json` for format examples
- let users choose APK files from disk
- publish large binaries as GitHub Release assets instead of source files
- let release packaging copy approved generated APK payloads into the portable
  app zip when an offline first-run experience is needed

That gives contributors a normal clone without needing LFS bandwidth.
