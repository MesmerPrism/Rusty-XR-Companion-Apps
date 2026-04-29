# Contributing

Contributions are welcome when they keep the project useful as a general Quest
and Windows utility.

Good first contributions:

- clearer setup docs
- ADB error explanations
- additional no-hardware tests
- public sample catalog improvements
- tooling detection fixes
- diagnostics bundle improvements
- accessibility and keyboard-flow improvements in the WPF app

Before opening a PR:

```powershell
dotnet build RustyXr.Companion.slnx
dotnet test RustyXr.Companion.slnx
npm run pages:build
powershell -ExecutionPolicy Bypass -File .\tools\app\Invoke-PublicBoundaryScan.ps1
```

## Public Asset Policy

The first version does not commit APKs. Use local APK paths or public catalog
metadata.

If the project later approves a bundled public APK:

- confirm the APK can legally be redistributed
- prefer GitHub Releases for large binary delivery
- use Git LFS only when the project accepts the storage and bandwidth cost
- document the source, hash, license, and update path
- keep the app useful without that payload when possible

Do not attach private builds, private certificates, generated capture data, or
machine-specific diagnostics to PRs.
