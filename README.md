# MyScreenRecorder

Small Windows screen recorder: screen + microphone + speaker (WASAPI loopback) in one file.

## Download

Grab `MyScreenRecorder.exe` from the latest [GitHub Release](../../releases/latest) and double-click it. Requires 64-bit Windows 10 version 2004 or newer. The exe is unsigned, so SmartScreen may ask you to click *More info* then *Run anyway*.

## Releasing

Push a version tag; the `release` workflow builds the exe and attaches it to a new GitHub Release:

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## Build

Requires the .NET 10 SDK. Run:

```powershell
.\build.ps1
```

This downloads ffmpeg and the pixel font into `tools/` and `src/MyScreenRecorder/Assets/`, then publishes a single self-contained `dist\MyScreenRecorder.exe` (ffmpeg is embedded; no installer, no prerequisites).

## Run

Double-click `dist\MyScreenRecorder.exe`.

- SOURCE: one monitor, ALL monitors, or a drawn REGION
- MIC / SPEAKER: pick a device or NONE; both are mixed into the recording
- QUALITY: video 480P / 720P / 1080P (never upscaled), audio AAC 128K / AAC 256K / WAV
- REC / PAUSE / STOP / CANCEL: pause and resume any number of times into one output file; CANCEL discards the recording after a confirmation

## Where files go

- Recordings: `%USERPROFILE%\Videos\MyScreenRecorder\REC_yyyy-MM-dd_HH-mm-ss.mp4` (`.mkv` for WAV audio)
- In-progress segments: `%USERPROFILE%\Videos\MyScreenRecorder\.parts\<timestamp>\` (crash-tolerant MKV chunks, merged and deleted on STOP)
- Settings: `%APPDATA%\MyScreenRecorder\settings.json`
- Extracted ffmpeg: `%LOCALAPPDATA%\MyScreenRecorder\`
