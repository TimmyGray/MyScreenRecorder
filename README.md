# MyScreenRecorder

Small Windows screen recorder: screen + microphone + speaker (WASAPI loopback) in one file.

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
- REC / PAUSE / STOP: pause and resume any number of times; one output file

## Where files go

- Recordings: `%USERPROFILE%\Videos\MyScreenRecorder\REC_yyyy-MM-dd_HH-mm-ss.mp4` (`.mkv` for WAV audio)
- In-progress segments: `%USERPROFILE%\Videos\MyScreenRecorder\.parts\<timestamp>\` (crash-tolerant MKV chunks, merged and deleted on STOP)
- Settings: `%APPDATA%\MyScreenRecorder\settings.json`
- Extracted ffmpeg: `%LOCALAPPDATA%\MyScreenRecorder\`
