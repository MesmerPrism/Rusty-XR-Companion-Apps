# Third-Party Notices

Rusty XR Companion Apps is MIT licensed, but it can discover, launch, download,
or work beside tools that keep their own licenses.

## Android Platform Tools

`adb.exe` comes from Android SDK Platform Tools. Users can install it through
Android Studio, the Android SDK command-line tools, or another official Android
SDK distribution. Rusty XR Companion can also download the Windows
platform-tools archive from Google's official Android SDK repository metadata
into the user's LocalAppData tool cache.

## Meta Quest Tooling

Meta Quest command-line tools are provided by Meta under Meta's own terms.
This repository can locate such tools when they are already installed, and it
can download Meta's published `@meta-quest/hzdb-win32-x64` package into the
user's LocalAppData tool cache. Rusty XR Companion does not relicense Meta
tools.

## scrcpy

`scrcpy` is an open-source Android display and control tool. Rusty XR
Companion can launch it when it is installed, present on `PATH`, or installed
into the managed LocalAppData tool cache from the upstream GitHub release.
Rusty XR Companion does not relicense scrcpy.

## Bundled Rusty XR Example APKs

Published companion app zips can include public Rusty XR example APKs generated
from the Rusty XR source repository. Each bundled APK is accompanied by a
metadata file that records its source URL, SHA-256 hash, signing mode, native
library list, permission list, and debug status.

The Rusty XR broker APK may include native Lab Streaming Layer (`liblsl`) files
when that release asset was built with an Android `liblsl.so`. Those native
libraries keep their upstream license terms; Rusty XR Companion records their
presence in the bundled APK metadata and does not relicense them.

## User-Supplied APKs

APKs installed through this utility remain the responsibility of the user or
the app publisher. This repository does not claim ownership over target apps
installed with the companion.
