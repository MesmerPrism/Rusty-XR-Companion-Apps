# Polar H10 Windows Capture

Rusty XR Companion includes a Windows-only Polar H10 capture service for
operator-side biosignal experiments. The service lives in the Windows project
because it uses WinRT Bluetooth LE APIs and should not become a Rusty XR core
dependency.

## Scope

`RustyXr.Companion.Windows.PolarH10WindowsCaptureService` can:

- connect to a Polar H10 by Bluetooth address;
- read battery metadata when available;
- subscribe to standard heart-rate and RR notifications;
- start Polar PMD accelerometer streaming at 200 Hz, 16 bit, 8 g;
- write newline-delimited JSON records for sessions, battery, HR/RR, PMD
  control responses, ACC frames, and malformed frames;
- stop the PMD stream during cleanup where the device still accepts the command.

The JSONL output is intended as a local witness format for bridge and plotting
tools. It is not a stable public data interchange contract yet.

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

## Public Boundary

Keep raw capture artifacts, local run paths, study identifiers, and private
stream names out of this repository. Store run artifacts in the private planning
workspace or another user-selected output directory.

The Companion repository should own reusable Windows acquisition and operator
tooling. Project-specific bridge orchestration, private plotting manifests, and
experiment notes should stay outside this public repo until they are sanitized
into generic contracts.
