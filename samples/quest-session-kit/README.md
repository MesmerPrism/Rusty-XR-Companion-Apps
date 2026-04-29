# Quest Session Kit Samples

This folder contains public placeholder metadata for adapting the companion to
your own Quest apps.

The first release does not commit APK bytes. Keep APKs local, choose them from
the WPF app, or publish large public payloads as GitHub Release assets.

Files:

- `apk-catalog.example.json`

The sample uses `schemaVersion: "rusty.xr.quest-app-catalog.v1"`, matching the
public Rusty XR core schema exporter. Keep APK bytes out of the sample folder
unless a future release explicitly enables a public large-asset lane.
