# Polar H10 Windows Capture

Rusty XR Companion includes a Windows-only Polar H10 capture service for
operator-side biosignal experiments. The service lives in the Windows project
because it uses WinRT Bluetooth LE APIs and should not become a Rusty XR core
dependency.

## Scope

`RustyXr.Companion.Windows.PolarH10WindowsCaptureService` can:

- connect to a Polar H10 by Bluetooth address;
- read battery metadata when available;
- optionally subscribe to standard heart-rate and RR notifications;
- optionally start Polar PMD accelerometer streaming at 200 Hz, 16 bit, 8 g;
- write newline-delimited JSON records for sessions, battery, HR/RR, PMD
  control responses, ACC frames, and malformed frames;
- stop the PMD stream during cleanup where the device still accepts the command.

The JSONL output is intended as a local witness format for bridge and plotting
tools. It is not a stable public data interchange contract yet.

`PolarH10WindowsCaptureOptions` exposes `IncludeHeartRate` and `IncludePmdAcc`
so capture tools can run HR/RR-only, PMD-only, or combined Windows sessions. PMD
is still explicit at the call site even when a command defaults to the legacy
combined capture behavior.

## PMD Ownership

Treat Polar H10 raw PMD streams such as ECG and accelerometer as single-owner
streams. The H10 can be configured for two Bluetooth receivers for live heart
rate, but that should not be interpreted as a guarantee that raw PMD streams
can be consumed by two devices at the same time.

For direct-vs-mediated Quest comparisons, prefer one of these designs:

- PC owns PMD and forwards selected data to the Quest broker over LSL, OSC, or
  ZeroMQ-style bridge transports.
- Quest owns PMD and relays timestamped broker events back to the PC for
  plotting.
- HR/RR is tested separately as the dual-receiver path.
- Two sensors are used when a true same-time raw ACC or ECG comparison is
  required.

## Bridge Planning Command

Use the portable CLI planner before a live capture to choose the PMD owner,
transport options, and timing evidence to collect:

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- polar plan --json
dotnet run --project src/RustyXr.Companion.Cli -- polar plan --profile pc-owned-pmd --out .\artifacts\polar-bridge --json
```

The planner is device-free. It does not open Bluetooth, ADB, LSL, OSC, or
ZeroMQ sockets; it only writes JSON and Markdown planning artifacts. Current
profiles are:

- `pc-owned-pmd`: Windows owns Polar PMD and forwards to the Quest broker over
  LSL, OSC, or ZeroMQ-style bridge transports.
- `quest-owned-pmd`: the Quest broker owns Polar PMD and relays timestamped
  records back to the PC for plotting.
- `hr-rr-dual-receiver`: both PC and Quest receive standard HR/RR while PMD is
  disabled.
- `two-sensor-raw-compare`: two sensors provide independent raw PMD streams for
  direct and mediated comparison.

For a single H10, do not label PC and Quest as simultaneous PMD owners. Run the
LSL broker round-trip probe alongside live captures when a headset is available
so plots can annotate clock offset and round-trip stability.

## Throughput Diagnostic Command

Use `polar throughput` for the live timing run. It writes a bundle containing
the raw Windows JSONL capture where applicable, `polar-throughput-report.json`,
CSV timing extracts, and `polar-throughput-summary.md`.

```powershell
dotnet run --project src/RustyXr.Companion.Cli -- polar throughput `
  --mode windows-owned-pmd `
  --serial <quest-serial> `
  --device-address <polar-mac> `
  --duration-seconds 30 `
  --lsl-dll <path-to-lsl.dll> `
  --out .\artifacts\polar-throughput `
  --json

dotnet run --project src/RustyXr.Companion.Cli -- polar throughput `
  --mode quest-owned-pmd `
  --serial <quest-serial> `
  --quest-device-address <polar-mac> `
  --duration-seconds 30 `
  --lsl-dll <path-to-lsl.dll> `
  --out .\artifacts\polar-throughput `
  --json

dotnet run --project src/RustyXr.Companion.Cli -- polar throughput `
  --mode hr-rr-dual-receiver `
  --serial <quest-serial> `
  --windows-device-address <polar-windows-mac> `
  --quest-device-address <polar-quest-mac> `
  --duration-seconds 30 `
  --lsl-dll <path-to-lsl.dll> `
  --out .\artifacts\polar-throughput `
  --json
```

`windows-owned-pmd` opens PMD on Windows, records HR/RR and ACC frames, pushes
those records to a Windows LSL string outlet, and, when `--serial` is supplied,
asks the Quest broker to run a bounded LSL string inlet capture for the
forwarded `bio:polar_acc` records. `quest-owned-pmd` starts the broker-owned
Polar source on the Quest and listens on Windows for the broker's LSL-mirrored
`bio:polar_acc` events. `hr-rr-dual-receiver` disables PMD on Windows and
starts HR/RR on both sides, which is the supported single-sensor overlap test.

Use `--device-address` when both hosts see the same BLE address. Use
`--windows-device-address` and `--quest-device-address` when Windows and
Android expose different BLE identities for the same strap.

The command also runs the existing broker LSL round-trip probe unless
`--skip-roundtrip` is set. That gives the report an LSL clock-correction and
transport-stability witness independent of the Polar samples.

For PMD handoff reliability, run `quest-owned-pmd`, verify the broker reports
PMD stopped, run `windows-owned-pmd`, then run `quest-owned-pmd` again. Raw PMD
should have exactly one owner in each leg.

## Public Boundary

Keep raw capture artifacts, local run paths, study identifiers, and private
stream names out of this repository. Store run artifacts in the private planning
workspace or another user-selected output directory.

The Companion repository should own reusable Windows acquisition and operator
tooling. Project-specific bridge orchestration, private plotting manifests, and
experiment notes should stay outside this public repo until they are sanitized
into generic contracts.
