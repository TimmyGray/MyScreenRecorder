# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Windows-only WPF screen recorder (.NET 10, `net10.0-windows`, win-x64). Captures screen with ffmpeg `gdigrab` and mixes microphone + speaker (WASAPI loopback, via NAudio) into one MP4/MKV. Published as a single self-contained `MyScreenRecorder.exe` with ffmpeg embedded.

## Commands

```powershell
.\build.ps1                                   # downloads ffmpeg -> tools/ and font -> Assets/ (if missing), then publishes to dist\MyScreenRecorder.exe
dotnet build src\MyScreenRecorder -c Debug    # quick compile check
dotnet build MyScreenRecorder.sln -c Debug    # all projects; also works on Linux (EnableWindowsTargeting)
dotnet test --solution MyScreenRecorder.sln   # unit tests
dotnet run --project src\MyScreenRecorder     # run from source
```

- Tests live in `tests/MyScreenRecorder.Core.Tests` (xUnit v3 on Microsoft Testing Platform, enabled by the repo-root `global.json`; do not add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`). There is no lint/format config.
- `tools/`, `dist/`, `bin/`, `obj/` are gitignored. `tools/ffmpeg.exe` is embedded as a resource only if it exists (`Condition="Exists(...)"` in the csproj), so a plain `dotnet build` without running `build.ps1` first yields an exe that falls back to `ffmpeg.exe` beside the exe or on `PATH` (see `FfmpegLocator`).
- Release: pushing a `v*` tag triggers `.github/workflows/release.yml`, which runs `build.ps1` and creates a GitHub Release with the exe.

## Architecture

`Quality`, `Settings`/`AppPaths`, `CaptureTarget`/`MonitorInfo`, `StereoDownmixSampleProvider`, and `AudioDeviceInfo` live in the platform-neutral `src/MyScreenRecorder.Core` library (`net10.0`, BCL + `NAudio.Core` only); the WPF app references it. `CaptureTarget.ForAll`/`ForRegion` take the monitor list as input.

The recording pipeline spans several files and is the part worth understanding before editing:

- **Segmented recording.** `RecordingSession` (state machine: Idle/Recording/Paused/Finalizing; all operations serialized by the `_ops` semaphore) does not keep one ffmpeg process alive across a pause. Each Start/Resume spawns a new `SegmentWriter`, which runs one ffmpeg process writing `seg_NNN.mkv` into `Videos\MyScreenRecorder\.parts\<timestamp>\`. Pause = stop the current segment; STOP = stop it, then `FinalizeAsync` runs an ffmpeg `concat` (`-c copy`, `+faststart` for mp4) into `REC_<timestamp>.mp4` (or `.mkv` for WAV audio) and deletes the parts folder. CANCEL stops and deletes the parts. If finalizing fails, parts and `ffmpeg.log` are kept and the error goes in `LastError`.
- **Audio path.** ffmpeg does not capture audio itself. `SegmentWriter` creates a named pipe (`\\.\pipe\srec_<session>_<index>`) and passes it to ffmpeg as an `f32le` 48 kHz stereo input; `AudioEngine.BeginSegment` then starts a pump thread that mixes the armed `AudioSource`s (mic/loopback, NAudio `MixingSampleProvider`) on a wall-clock schedule and writes float PCM into the pipe, padding with silence so audio length tracks real time. Audio devices cannot be changed while a segment is active (`SetMic`/`SetSpeaker` throw). `StereoDownmixSampleProvider` normalizes device channel layouts to stereo.
- **Quality.** `Quality` is the single source of ffmpeg video/audio arguments (`VideoArgList`/`AudioArgList`), labels, and output extension. Scaling is `min(target height, source height)` so footage is never upscaled.
- **Process hygiene.** Every ffmpeg child is assigned to `KillOnCloseJob` (Win32 job object in `Native/Win32.cs`) so ffmpeg dies with the app. Segments are stopped by writing `q` to ffmpeg's stdin, with a kill fallback after a timeout.
- **Capture target and overlays.** `CaptureTarget` (monitor / all monitors / drawn region) carries screen-coordinate bounds forced to even dimensions (x264 requirement). `OverlayManager` creates one click-through `MonitorOverlay` per monitor (a numbered badge when idle, a colored border when recording/paused) from `OverlayPiece` windows. Overlay windows use `WS_EX_TRANSPARENT` and `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` so they are not captured in the recording. `RegionSelectWindow` is the region picker; `PreviewGrabber` feeds the live preview image in the main window.
- **UI.** `MainWindow.xaml(.cs)` is the only main window; it wires `RecordingSession`, `AudioEngine`, `OverlayManager`, and `Settings` (persisted to `%APPDATA%\MyScreenRecorder\settings.json`). Styling lives in `Theme.xaml` with the embedded PressStart2P pixel font (falls back to Consolas if the font download failed).
- **DPI.** `app.manifest` declares PerMonitorV2 and ffmpeg is launched with `__COMPAT_LAYER=HighDpiAware`, so coordinates passed to `gdigrab` are physical pixels. Keep this consistent when touching bounds math.

## Conventions

- `ImplicitUsings` is disabled; add `using` directives explicitly. `Nullable` is enabled.
- The parent workspace `E:\apps\AGENTS.md` asks for concise docstring annotations (purpose, input assumptions, output contract, side effects, failure modes, security notes — at least 3) on non-trivial functions you add or change, and for existing annotations to be updated when behavior changes. The current code has few of these; follow the rule for new/edited code.
