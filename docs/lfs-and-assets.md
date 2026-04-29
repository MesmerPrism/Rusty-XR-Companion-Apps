---
title: Git LFS And Assets
nav_order: 10
---

# Git LFS And Assets

This repo includes Git LFS patterns for APK and AAB files, but the first
release deliberately does not commit APK bytes.

## Default Policy

- Use local APK paths during development.
- Use public catalog JSON for metadata.
- Use GitHub Releases for large public binaries when possible.
- Keep source checkout size small.
- Keep the app useful without a bundled APK.

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

For this first version:

- keep `samples/quest-session-kit/APKs/*.apk` ignored
- use `samples/quest-session-kit/apk-catalog.example.json` for format examples
- let users choose APK files from disk
- publish optional large binaries as GitHub Release assets instead of source
  files

That gives contributors a normal clone without needing LFS bandwidth.
