# oru

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

`oru` is a lightweight Windows tray utility designed for **real-time audio mirroring and multi-device output merging**. It allows you to seamlessly route system audio to multiple playback devices simultaneously, alongside fast Bluetooth management and one-click output switching directly from your system tray.

## Core Features

- **Audio Mirroring & Merging (Multi-Audio)**: Route system audio to multiple playback devices (e.g., headphones, speakers, and HDMI outputs) simultaneously in real-time.
- **Fluent System Tray Flyout**: A modern, native Windows 11 flyout design integrating Acrylic and Mica backdrops.
- **One-Click Quick Switching**: Fast enumeration and immediate toggling of active playback devices.
- **Integrated Bluetooth Management**: View, connect, and toggle wireless audio devices directly from the tray.

## Installation

### Download (Recommended)

1. Go to [**Releases**](../../releases/latest) and download `oru-<version>-win-x64.zip` (or `win-arm64` for ARM devices)
2. Extract the ZIP anywhere (e.g. `C:\Tools\oru\`)
3. Run `oru.exe`

> **Note:** Because oru is unsigned, Windows SmartScreen may show a warning on first run.
> Click **"More info" → "Run anyway"** to proceed. You can verify the download using the
> `checksums-sha256.txt` file included in each release.

### WinGet (Coming Soon)

```powershell
winget install oru
```

## Requirements

- Windows 10 version 2004 (build 19041) or later
- Windows 11 recommended for Mica / Acrylic backdrops and best Fluent design fidelity
- No .NET install required — the release ZIP is fully self-contained

## Stack

- .NET 8 (self-contained, no runtime install needed)
- WinUI 3 / Windows App SDK 1.6
- NAudio — Windows Core Audio COM interop
- CommunityToolkit.Mvvm

## Building from Source

**Requirements:** .NET 8 SDK — `dotnet --list-sdks`

```powershell
# Debug run
dotnet run -p:Platform=x64

# Release build (x64)
dotnet build -c Release -p:Platform=x64
```

## Creating a Release

Use the included publish script to build and package both architectures:

```powershell
.\publish.ps1 -Version 1.0.0
```

Output ZIPs and a `checksums-sha256.txt` file will be placed in `Publish\release\`.
Upload these as assets when creating a new GitHub Release.

## Privacy

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it.

## License

Copyright (c) 2026 [ducksblock](https://github.com/ducksblock)

Released under the [MIT License](LICENSE).

---

> Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).
