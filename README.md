# oru 🎧

**A lightweight Windows tray utility for **real-time audio mirroring**, multi-device output merging, and quick Bluetooth connectivity.**

<img width="2048" height="1024" alt="oru-promotional-header" src="https://github.com/user-attachments/assets/a9b67995-582d-4d27-905f-07cfd8e27e4d" />

## Features

- **Audio Mirroring** - Route system audio to multiple playback devices simultaneously
- **Quick Switching** - One-click output device toggling from the system tray
- **Bluetooth Connect** - Quickly connect to paired wireless audio devices from the tray
- **Fluent Design** - Native Windows 11 flyout with Acrylic and Mica backdrops

## Install

Download `oru-x64.zip` (or `oru-arm64.zip` for ARM) from [**Releases**](../../releases/latest), extract, and run `oru.exe`.

> Windows SmartScreen may warn on first run since oru is unsigned — click **"More info" → "Run anyway"**.

**Requirements:** Windows 10 2004+ (Windows 11 recommended) · No .NET install needed

## Build from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
dotnet run -p:Platform=x64                    # debug
dotnet build -c Release -p:Platform=x64       # release build
.\publish.ps1                                 # package x64 + ARM64 ZIPs
```

## Privacy

This program does not transfer any information to other networked systems unless specifically requested by the user.

## License

MIT © 2026 [ducksblock](https://github.com/ducksblock)
