# Epics: Linux (X11) build

Architecture, decisions (D1–D11) and verified facts: `docs/planning/linux-build-architecture.md`. Read it before any story. Tracker: `docs/implementation/sprint-status.yaml`.

## 1. Requirements

| ID | Requirement |
|---|---|
| FR1 | On an X11 session a Linux user records one monitor, all monitors, or a drawn region into `REC_yyyy-MM-dd_HH-mm-ss.mp4` (`.mkv` when audio is WAV) in `~/Videos/MyScreenRecorder`. |
| FR2 | Microphone and speaker (the monitor of an output sink) are each selectable or `NONE` and are mixed into the recording. |
| FR3 | PAUSE/RESUME any number of times into one output file; STOP finalizes; CANCEL discards after a Yes/No confirmation (default No). |
| FR4 | Quality options are the same as on Windows: video 480P/720P/1080P (never upscaled), audio AAC 128K / AAC 256K / WAV. |
| FR5 | Live preview of the selected source in the main window. |
| FR6 | Mic and speaker level meters move while idle and while recording. |
| FR7 | Overlays: monitor-number badges while idle; while recording/paused a REGION target, a red/yellow border outside the captured rectangle. Neither appears in the recorded video. |
| FR8 | Settings persist in `~/.config/MyScreenRecorder/settings.json`; OPEN opens the output folder. |
| FR9 | On a Wayland session the app shows the D1 message and disables REC; without `DISPLAY` it prints the message to stderr and exits with code 1. |
| FR10 | One `v*` tag publishes `MyScreenRecorder.exe` and `MyScreenRecorder-linux-x64.tar.gz` on one GitHub Release. |
| NFR1 | Windows behaviour is unchanged by Epics 1 and 4 (Windows smoke checklist, §4). |
| NFR2 | No ffmpeg process outlives the app: normal close, crash, or `kill -9` of the app. |
| NFR3 | Per segment, the audio track ends no more than 0.4 s before the video track (`ffprobe` stream durations). |
| NFR4 | Core and Linux platform code have unit tests that run on Linux; CI runs them. |
| NFR5 | The Linux ffmpeg is pinned to a tag and verified by sha256. |
| NFR6 | Preview costs ≤ 10 % of one core for a 2560×1440 monitor on the dev machine. |

## 2. Working rules (every story)

- **Prerequisites on the dev machine:** .NET 10 SDK installed for the user (it is not installed system-wide today; ask the user how they want it installed before installing anything), X11 session, PipeWire/PulseAudio. For integration tests and running from source: `tools/linux/ffmpeg` and `tools/linux/ffprobe` (created by `build.sh` from story 4.1; before that, ask the user for an ffmpeg to use).
- **Branch:** one branch per story, created from the current `origin/main` after the previous story is merged: `git fetch origin && git switch -c feat/linux-<story-key> --no-track origin/main`, then check that `git config branch.feat/linux-<story-key>.merge` prints nothing. If the previous story is not merged yet, stop and ask.
- **Nothing is committed, pushed, or opened as a PR without the user's explicit permission.** The user's global agent rules (commit delegation, error log, comment check, test rules) apply in full.
- **Language and conventions:** C# with `ImplicitUsings` disabled (explicit `using`s) and `Nullable` enabled; keep namespaces of moved files; move files with `git mv`. Comments/docstrings per D7.
- **When reality differs from the plan** (a file, symbol, version, or behaviour is not as written): stop and ask the user. Do not choose an alternative silently.
- **Docs:** update `CLAUDE.md` (and `README.md` where user-facing) in the same story when a command, path, project, or architecture fact changes.

## 3. Definition of Done (every story)

1. Every AC passes, with the evidence the story names (test name, command output, or smoke item).
2. `dotnet build MyScreenRecorder.sln -c Debug`: 0 errors, no new warnings.
3. `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"`: all pass. Linux platform/UI stories also run `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` (integration included) on the dev machine.
4. Each new negative test was run once against a copy of the code with its guard removed and failed for the guard's reason.
5. Comments: `git diff -U0 | grep -E '^\+\s*(//|///)'` reviewed against D7; restating comments removed.
6. `CLAUDE.md`/`README.md` updated where facts changed.
7. Stories marked *Windows smoke*: the user ran §4 on real Windows and reported a pass; the result is recorded in the story's Dev Agent Record.
8. Stories marked *Linux smoke*: the listed §5 items pass on the dev machine.
9. Nothing committed/pushed without the user's permission.

## 4. Windows smoke checklist (run by the user)

Build with `.\build.ps1` and run `dist\MyScreenRecorder.exe` on a machine where `ffmpeg.exe` is **not** on `PATH` and not beside the exe.

| # | Step | Expected |
|---|---|---|
| W1 | Start the app | Status `READY` (not `ERROR: ffmpeg not found`); preview shows monitor 1; badges on every monitor |
| W2 | Pick a mic and a speaker; speak; play a sound | Both meters move |
| W3 | REC 5 s, clap in the last second → PAUSE 3 s → RESUME 5 s, clap in the last second → STOP | `SAVED: REC_*.mp4`; the file plays ~10 s; voice and system sound audible; **both claps audible** (D8 tail check) |
| W4 | REGION: draw a region → REC 3 s → STOP | Video has the region's size; neither border nor badge is in the video |
| W5 | Source ALL → REC 3 s → STOP | File plays |
| W6 | REC → CANCEL → Yes | `CANCELED: recording discarded`; no new file; `.parts\<stamp>` removed |
| W7 | Audio WAV → REC 3 s → STOP | Output is `.mkv` |
| W8 | REC → close the window | File saved; no `ffmpeg.exe` left in Task Manager |
| W9 | Restart the app | Source, mic, speaker, qualities restored |

If W3's claps are missing, report it: it opens backlog story 5-8, it does not block the story.

## 5. Linux smoke checklist (run on the dev machine)

Run `dotnet run --project src/MyScreenRecorder.Linux` (or `dist/linux/MyScreenRecorder` after 4.1) on X11.

| # | Step | Expected |
|---|---|---|
| L1 | Start | Status `READY`; sources `MON 1 1920x1080`, `MON 2 2560x1440`, `MON 3 1920x1080`, `ALL 6400x1440` (dev machine layout) |
| L2 | Speak; play a sound | Mic and speaker meters move |
| L3 | REC 5 s, clap in the last second → PAUSE 3 s → RESUME 5 s, clap in the last second → STOP | `SAVED: REC_*.mp4` in `~/Videos/MyScreenRecorder`; plays; both claps audible; `ffprobe` audio duration ≥ video duration − 0.8 s |
| L4 | ALL → REC 3 s → STOP | File plays |
| L5 | REC → CANCEL → Yes | No file; `.parts/<stamp>` removed |
| L6 | Audio WAV → REC 3 s → STOP | `.mkv` |
| L7 | REC → close the window | File saved; `pgrep -a ffmpeg` prints nothing |
| L8 | REC → `kill -9 <app pid>` | Within 1 s `pgrep -a ffmpeg` prints nothing; the current `seg_NNN.mkv` in `.parts` plays |
| L9 | OPEN | File manager shows `~/Videos/MyScreenRecorder` |
| L10 | Restart | Settings restored |
| L11 | Preview (from 3.2) | Preview follows the selected source; `top` shows the preview ffmpeg ≤ 10 % CPU on MON 2 |
| L12 | REGION (from 3.3) | Drawn region is recorded at its size |
| L13 | Overlays (from 3.4) | Badges while idle, gone before the first frame; region border outside the rectangle; neither in the video; clicks pass through badges and border |

## 6. Epics and stories

Story keys are `<epic>-<story>-<slug>`; each story is one PR.

### Epic 1: Shared Core (Windows behaviour unchanged)

**1-1 Core and test projects; move platform-neutral types** — *Windows smoke* — FR4, NFR1, NFR4
- Adds `MyScreenRecorder.sln`, `global.json`, `src/MyScreenRecorder.Core`, `tests/MyScreenRecorder.Core.Tests`; `EnableWindowsTargeting` in the WPF project; moves `Quality` (dead members removed), `Settings` (+ `AppPaths`), `CaptureTarget`, `MonitorInfo`, `StereoDownmixSampleProvider`, `AudioDeviceInfo` to Core.
- AC1 **Solution builds on Linux.** Given the .NET 10 SDK on Linux, when `dotnet build MyScreenRecorder.sln -c Debug` runs, then all projects build with 0 errors, the WPF project included.
- AC2 **Tests run.** When `dotnet test --solution MyScreenRecorder.sln` runs, then the Core tests run and pass.
- AC3 **Union replaces VirtualScreen.** Given monitors (0,116,1920×1080), (1920,0,2560×1440), (4480,143,1920×1080), when `CaptureTarget.ForAll(monitors)` is called, then bounds are (0,0,6400×1440) and the label is `ALL 6400x1440`.
- AC4 **Old settings still load.** Given a settings.json in the current format, when it is loaded, then every field is read; given corrupt JSON, then defaults are returned.
- AC5 **Windows unchanged.** The user's Windows smoke checklist passes.

**1-2 Recording pipeline into Core behind process/grab/pipe seams** — *Windows smoke* — NFR1, NFR2 — needs 1-1
- Adds `IChildProcessLauncher`, `IAudioPipe(Factory)`, `ScreenGrab`/`GdiGrab`/`X11Grab`, `FfmpegLocatorOptions`, `PlatformServices`; moves `SegmentWriter`, `RecordingSession`, `FfmpegLocator` to Core; `AppPaths.OutputFolder`; Windows implementations (`JobObjectProcessLauncher`, `WindowsNamedPipeFactory`) in `src/MyScreenRecorder/Platform/Windows/`.
- AC1 **Byte-identical Windows args.** Given the Windows seams, when a segment's start info is built (with audio and without), then the argument list and environment equal today's (`SegmentWriter.BuildStartInfo` at commit `b6259ee`).
- AC2 **X11 args.** Given `X11Grab(":0")`, bounds (1920,0,2560×1440), P720, AAC 256K, then the arguments are the x11grab input from the architecture §4, the `unix:` audio input, the `Quality` args, and `-x264-params sliced-threads=1`.
- AC3 **Embedded ffmpeg found.** Given `FfmpegLocatorOptions` with the WPF assembly, when `PATH` has no ffmpeg and none is beside the exe, then the embedded resource is extracted and returned (Windows smoke W1).
- AC4 **PATH split.** Given `PATH` built with `Path.PathSeparator` containing a directory with the executable, then the locator returns it.
- AC5 **Windows unchanged.** Windows smoke passes.

**1-3 Audio capture seam** — *Windows smoke* — FR2, FR6, NFR1 — needs 1-1 (parallel with 1-2 only if both branch from the same base; default: after 1-2)
- Adds `IAudioCapture`, `IAudioCaptureFactory`, `IAudioDeviceCatalog`; `AudioSource` becomes platform-neutral over `IAudioCapture`; `AudioEngine` moves to Core; `WasapiAudioCapture`, `MmDeviceCatalog` in the WPF project.
- AC1 **Peaks.** Given a fake capture delivering float32, 16-, 24-, and 32-bit PCM buffers with a known maximum, then `Peak` equals that maximum (±1e-4) and drops to 0 300 ms after the last data.
- AC2 **Armed buffering.** Given a source that is not armed, when data arrives, then nothing is buffered; when armed, then it is.
- AC3 **Wall-clock pump.** Given no sources delivering data, when a segment runs ~1 s, then the pipe receives silence for the elapsed time (±10 %).
- AC4 **Windows unchanged.** Windows smoke passes.

**1-4 RecorderController** — NFR4 — needs 1-2, 1-3
- Adds `IScreenCatalog` and `RecorderController` (Core): targets (monitors, ALL, region), device lists with `NONE`, initial selection rules from settings/first run/defaults, settings save mapping, start/pause/resume/stop/cancel, button enablement, status text with a severity enum, overlay state. Logic is taken from `MainWindow.xaml.cs`; the WPF `MainWindow` is not changed.
- AC1 **Initial selection.** First run → default devices selected; later runs → saved ids; unknown saved id → `NONE`; saved target `All` → ALL; unknown monitor number → primary.
- AC2 **Status texts** match today's strings: `READY`, `SAVING...`, `* REC  hh:mm:ss  N.N MB  <quality>`, `|| PAUSED  …`, `SAVED: <file>`, `CANCELED: recording discarded`, `ERROR: …`.
- AC3 **Enablement** per state equals `MainWindow.UpdateUi` today.
- AC4 **Badges hidden before capture.** When REC is pressed, then the overlay state becomes non-idle before `RecordingSession.StartAsync` is called (architecture §5.8).

### Epic 2: Linux platform services

**2-1 Linux projects skeleton and session check** — FR9 — needs 1-1
- Adds `src/MyScreenRecorder.Platform.Linux`, `src/MyScreenRecorder.Linux` (Avalonia 12.1.4 window with theme colours and pixel font), `tests/MyScreenRecorder.Platform.Linux.Tests`, `SessionCheck`.
- AC1 Given `XDG_SESSION_TYPE=wayland` (or `WAYLAND_DISPLAY` set), then the window shows the D1 message.
- AC2 Given `DISPLAY` unset, when the app starts, then stderr has the message and the exit code is 1.
- AC3 Given an X11 session, then the window opens with title `MY SCREEN RECORDER`.

**2-2 Linux process launcher** — NFR2 — needs 1-2, 2-1
- `LinuxChildProcessLauncher` (architecture §5.1).
- AC1 Given a child started through the launcher from a `Task.Run` continuation, when 30 s pass, then the child is alive (Integration).
- AC2 Given a child ffmpeg recording through the launcher, when the test host is killed with `SIGKILL`, then the child is gone within 1 s and its MKV is valid (Integration).
- AC3 Given no `setpriv` with `--pdeathsig` (injected probe result), then processes start unwrapped and are killed on `ProcessExit`.

**2-3 ffmpeg on Linux: locator, x11grab, Unix-socket audio** — FR1, NFR3 — needs 2-2
- `UnixSocketPipeFactory`, Linux `FfmpegLocatorOptions`, capability check (architecture §5.4, §5.5).
- AC1 Socket path rules: rooted under `XDG_RUNTIME_DIR`; fallback when unset/missing/longer than 107 bytes.
- AC2 Given a 3 s sine pumped through `AudioEngine` into a real segment, then the MKV has video and audio and audio ≥ video − 0.4 s (Integration).
- AC3 Given an ffmpeg without x11grab (fake `-devices` output), then the check fails with the §5.5 message.

**2-4 PulseAudio capture and device catalog** — FR2, FR6 — needs 1-3, 2-1
- `PulseSimpleCapture`, `PactlDeviceCatalog` (architecture §5.2, §5.3).
- AC1 Given captured `pactl --format=json` output of the dev machine (fixture), then inputs exclude monitors and outputs carry `<sink>.monitor` ids.
- AC2 Given an id not in the catalog, when opening, then it throws (no silent fallback).
- AC3 Given the default source and the default sink monitor, when capturing 1 s, then data arrives within 100 ms and the format is float32/48 kHz/2 ch (Integration).

### Epic 3: Linux UI

**3-1 Main window: record end to end** — FR1–FR4, FR6, FR8 — *Linux smoke L1–L10* — needs 1-4, 2-2, 2-3, 2-4
- Avalonia main window wired to `RecorderController`, `AvaloniaScreenCatalog`, cyclers, meters, buttons, cancel dialog, OPEN, crash handler, capability/session/setpriv messages. No preview, region, or overlays yet.
- ACs = Linux smoke L1–L10.

**3-2 Live preview** — FR5, NFR6 — *Linux smoke L11* — needs 3-1
**3-3 Region selector** — FR1 — *Linux smoke L12* — needs 3-1
**3-4 X11 overlays** — FR7 — *Linux smoke L13* — needs 3-1 (3-3 for the region border)

### Epic 4: Build and release

**4-1 build.sh and tarball** — D2, D6, NFR5 — needs 3-1
- Pinned BtbN tag + sha256; `tools/linux/{ffmpeg,ffprobe}`; publish; tarball layout (architecture §5.13); README Linux section with runtime dependencies (glibc ≥ 2.28, libX11/libxcb, libpulse-simple, pactl, setpriv, xdg-open, fontconfig).
- AC1 Given a clean checkout, when `./build.sh` runs, then `dist/MyScreenRecorder-linux-x64.tar.gz` exists and extracting it gives `MyScreenRecorder` and `ffmpeg` with mode 0755.
- AC2 Given a tampered download (wrong sha256), then `build.sh` fails before publishing.
- AC3 The extracted binary passes Linux smoke L1, L3.

**4-2 Release workflow for both OS** — FR10, D11 — *Windows smoke on the released exe* — needs 4-1
- AC1 On a test tag in a fork or with a `-rc` tag agreed with the user: the release contains both assets; deleting the Linux job's artifact (simulated failure) produces no public release.

### Epic 5: Backlog (after the first Linux release)

- 5-1 Windows `MainWindow` adopts `RecorderController`.
- 5-2 Startup sweep of orphan ffmpeg processes whose command line contains our `.parts` folder.
- 5-3 OPEN selects the file via `org.freedesktop.FileManager1.ShowItems`.
- 5-4 `.desktop` file and icon in the tarball.
- 5-5 libpulse introspection replaces `pactl`.
- 5-6 Pin and checksum the Windows ffmpeg download in `build.ps1`.
- 5-7 Move to `.NET 11` `ProcessStartInfo.KillOnParentExit`.
- 5-8 Bound x264 latency on Windows (only if Windows smoke W3 loses the clap).
- Future epic (not tracked here): Wayland via xdg-desktop-portal + PipeWire.

## 7. Sequencing

- **Critical path to the first Linux release:** 1-1 → 1-2 → 1-3 → 1-4 → 3-1 → {3-2, 3-3 → 3-4} → 4-1 → 4-2.
- **Parallel track:** 2-1 after 1-1; 2-2 and 2-3 after 1-2; 2-4 after 1-3. All of Epic 2 must be done before 3-1.
- Stories touching the WPF project (1-1, 1-2, 1-3) need the user's Windows run; plan them so the user can test between them.
