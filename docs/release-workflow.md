---
title: Release Workflow
nav_order: 9
---

# Release Workflow

The first release lane is portable and does not require an APK payload.

The GitHub workflow:

1. builds and tests the .NET solution
2. builds the GitHub Pages site
3. publishes the WPF app as a self-contained win-x64 app
4. publishes the CLI as a self-contained win-x64 app
5. publishes the guided setup helper
6. signs the setup helper when signing secrets are configured
7. creates app and CLI zips
8. writes `SHA256SUMS.txt`
9. uploads release assets

## Signing Secrets

Optional secrets:

```text
WINDOWS_PREVIEW_SETUP_CERTIFICATE_BASE64
WINDOWS_PREVIEW_SETUP_CERTIFICATE_PASSWORD
```

Optional variable:

```text
WINDOWS_PREVIEW_SETUP_TIMESTAMP_URL
```

Generate or export a certificate with:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\app\New-Preview-SigningCertificate.ps1
```

Do not commit PFX files or passwords.

## Why Portable First

This version does not need Windows package identity and does not bundle APK
payloads. Portable release assets keep the public setup simple while the app
surface, docs, and diagnostics stabilize.

Future MSIX support can reuse the same public docs structure and signing
posture when package identity becomes useful.
