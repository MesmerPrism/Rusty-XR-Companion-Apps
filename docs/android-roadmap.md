---
title: Android Roadmap
nav_order: 12
---

# Android Roadmap

The first implemented app is Windows WPF. A later Android companion app should
cover phone-side Quest workflows:

- foreground service host
- USB-host ADB bootstrap
- Wi-Fi ADB handoff from USB
- staged APK install
- launch and stop actions
- diagnostics bundle export
- catalog/profile reuse where public schemas align

The Android lane should keep the same public boundary: no private package IDs,
no private APK payloads, and no unpublished workflow assumptions.
