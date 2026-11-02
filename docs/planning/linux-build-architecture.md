# Linux build: architecture and decisions

Status: agreed with the product owner on 2026-10-28/29. Input for `docs/planning/epics-linux-build.md` and every story under `docs/implementation/`.

This document is self-contained: an implementer needs nothing from the planning conversation. When the code or a tool behaves differently from what is written here, **stop and ask the user**; do not pick an alternative silently.

## 1. Read first (in this order)

1. `CLAUDE.md` (repo root): build commands, conventions (`ImplicitUsings` disabled, `Nullable` enabled), current Windows architecture.
2. This document, sections 2–5 (decisions, boundaries, seams).
3. `docs/planning/epics-linux-build.md`: requirements (FR/NFR), working rules, Definition of Done, smoke checklists, story list.
4. The story file you implement (`docs/implementation/<key>.md`).
5. Reference code: `src/MyScreenRecorder/Recording/SegmentWriter.cs`, `src/MyScreenRecorder/Recording/RecordingSession.cs`, `src/MyScreenRecorder/Audio/AudioEngine.cs`, `src/MyScreenRecorder/Audio/AudioSource.cs`, `src/MyScreenRecorder/Recording/FfmpegLocator.cs`, `src/MyScreenRecorder/MainWindow.xaml.cs`.

## 2. Decision record

Each decision lists the reason and the rejected alternatives. Do not reopen them inside a story; if one turns out to be impossible, stop and ask.

| ID | Decision | Reason | Rejected |
|---|---|---|---|
| D1 | **X11 only.** On a Wayland session the Linux app shows `ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.` and disables REC. Without `DISPLAY` it writes the same kind of message to stderr and exits with code 1 before the UI starts. | ffmpeg `x11grab` covers X11. Mainline FFmpeg (up to 9.1) has no PipeWire/portal grab (the `pipewiregrab` patch exists only on ffmpeg-devel). The dev machine runs X11 (Mint 22.3 Cinnamon). | X11 + Wayland in one epic (portal + PipeWire client, no absolute window placement for overlays). Wayland is a future epic. |
| D2 | **Packaging:** self-contained single-file `linux-x64` binary, released as `MyScreenRecorder-linux-x64.tar.gz` on the same GitHub Release as the Windows exe. | Same model as the Windows exe; no installer. | AppImage, .deb. |
| D3 | **UI:** the WPF app stays the Windows UI, unchanged in behaviour. A new Avalonia 12.1.4 app is the Linux UI. Both sit on a shared `MyScreenRecorder.Core` library. UI-agnostic main-window logic moves into `RecorderController` (Core), used by the Linux UI; the Windows UI adopts it later (backlog). | No regression risk to the shipped Windows UI; Windows can only be compile-checked from Linux. | Avalonia for both OS (rewrites the Windows UI and its Win32 overlay tricks; high regression risk). |
| D4 | **Windows verification:** after every Epic 1 story and after story 4.2 the user runs the Windows smoke checklist (epics doc §4) on real Windows. The story is not `done` until the user reports a pass. | The Core extraction changes the Windows build. | CI compile only; duplicating code without a shared core. |
| D5 | **First Linux release includes:** REGION selection, live preview, idle level meters, overlays (idle badges + recording border). | Parity with Windows. | Shipping these as later polish. |
| D6 | **ffmpeg on Linux:** BtbN FFmpeg-Builds `linux64-gpl` static build, pinned to a month-end autobuild tag and verified by sha256, shipped **beside** the binary inside the tar.gz (not embedded). Lookup order on Linux: `ffmpeg` beside the executable, then `PATH`. Windows keeps its current embedded `ffmpeg.exe`. | The BtbN binary is 173.5 MB; embedding it made the single file 116 MB and added 170–230 MB peak RSS (bundle decompression). Beside-the-exe costs nothing and `FfmpegLocator` already checks that location first. | Embedding (size, memory); system ffmpeg only (distro builds may lack features); johnvansickle static build (no libxcb, no libpulse). |
| D7 | **Comments/docstrings:** short docstrings (contract, failure modes, side effects) only on the new platform interfaces in Core and on native wrappers (libpulse, Xlib, process launcher). Everything else follows the user's global rule: comments only for non-obvious reasons. | Resolves the conflict between `CLAUDE.md` (docstrings on all non-trivial functions) and the user's global comment rule. | Full docstrings everywhere; no docstrings at all. |
| D8 | **x264 latency fix on Linux only:** the X11 capture profile adds `-x264-params sliced-threads=1`. `Quality.VideoArgList` (shared) is unchanged. The Windows smoke checklist gains a "clap in the last second of a segment is audible" check; if Windows fails it, a separate backlog story fixes Windows. | Measured on Linux: without it each segment loses its last 1.1–1.4 s of audio; with it 0.15–0.27 s, files 3–7 % smaller. Windows behaviour is unverified, and changing it needs the user's Windows check. | Changing the shared args for both OS now; `-tune zerolatency` (files 49 % larger); `-threads 4` (delay still scales with frame threads). |
| D9 | **Boundary moves** (§3.4) are approved: Settings + new `AppPaths` move to Core; `CaptureTarget` takes the monitor list as input; Recording reaches processes only through `IChildProcessLauncher`; pure ffmpeg-argument builders for gdigrab/x11grab live in Core; Linux platform services live in a separate library `MyScreenRecorder.Platform.Linux`, Windows platform services stay in the WPF project. | Testability on Linux; no OS API calls in Core. | Linux services inside the Avalonia project (tests would reference a UI exe). |
| D10 | **Tests:** xUnit v3 (`xunit.v3` 4.0.2) on Microsoft Testing Platform, enabled by a repo-root `global.json`. Core tests run on Linux and in CI; Linux integration tests carry `[Trait("Category", "Integration")]` and run only on a machine with X11 + PipeWire/PulseAudio + ffmpeg. | The repo has no tests; the refactor needs a safety net that runs on Linux. VSTest mode is refused by MTP on the .NET 10 SDK (verified). | xUnit v2 / VSTest. |
| D11 | **Release gating:** one `v*` tag builds Windows and Linux in separate jobs; a third job publishes the release only if both succeed. | A broken Linux build must not ship a half release. | Independent releases per OS. |

## 3. Boundaries

### 3.1 Current boundaries (derived, not documented)

There is no architecture document. These boundaries were derived from the code: folders map 1:1 to namespaces, and the import direction was taken from the `using` directives and call sites (commit `b6259ee`).

| Module (namespace = folder) | Holds | Depends on |
|---|---|---|
| `Audio/` | `AudioEngine` (mixer + wall-clock pump into the ffmpeg pipe), `AudioSource` (WASAPI capture, buffer, downmix/resample, peak), `AudioDevices`, `StereoDownmixSampleProvider` | NAudio |
| `Recording/` | `RecordingSession` (state machine, concat finalize), `SegmentWriter` (one ffmpeg per segment), `Quality`, `FfmpegLocator` | Audio, Capture, Native (`KillOnCloseJob` only) |
| `Capture/` | `CaptureTarget`, `ScreenInfo`/`MonitorInfo`, `PreviewGrabber` | WinForms (`Screen`, `SystemInformation`); `PreviewGrabber` also WPF + Native |
| `Overlay/` | `OverlayManager`, `MonitorOverlay`, `OverlayPiece`, `RegionSelector`/`RegionSelectWindow` | Capture, Native, WPF |
| `Native/` | Win32 window P/Invokes, `KillOnCloseJob` | — |
| root | `App`, `MainWindow`, `Cycler`, `Meter`, `Tips`, `Theme.xaml` (UI); `Settings` | everything |

Direction: UI → {Overlay, Recording, Capture, Audio}; Recording → {Audio, Capture, Native}; Overlay → {Capture, Native}. No cycles. Recording and Audio do not reference WPF.

### 3.2 Target projects

| Project | TFM / RID | Holds | May reference |
|---|---|---|---|
| `src/MyScreenRecorder.Core` (new) | `net10.0`, no RID | Recording pipeline, mixing/pump, quality, capture targets, settings, paths, `RecorderController`, platform interfaces (`MyScreenRecorder.Platform` namespace), pure ffmpeg-argument builders | BCL, `NAudio.Core` 2.2.1 |
| `src/MyScreenRecorder` (existing WPF app, same path, same `AssemblyName`) | `net10.0-windows`, `win-x64`, `EnableWindowsTargeting=true` | Windows UI, overlays, preview, Windows platform services under `Platform/Windows/` | Core, `NAudio` 2.2.1 |
| `src/MyScreenRecorder.Platform.Linux` (new) | `net10.0` | Process launcher, PulseAudio capture, `pactl` device catalog, Unix-socket pipe factory, session check, Xlib interop (overlay windows, cursor) | Core |
| `src/MyScreenRecorder.Linux` (new, `AssemblyName` `MyScreenRecorder`) | `net10.0`, `linux-x64` | Avalonia UI, screen catalog (Avalonia `Screens`), preview, region selector, overlay manager, composition root | Core, Platform.Linux, Avalonia 12.1.4 |
| `tests/MyScreenRecorder.Core.Tests` (new) | `net10.0` | Unit tests for Core | Core |
| `tests/MyScreenRecorder.Platform.Linux.Tests` (new) | `net10.0` | Unit tests (parsers, path builders, session check) + `Integration` tests | Core, Platform.Linux |

Namespaces inside Core keep the existing ones (`MyScreenRecorder.Recording`, `.Audio`, `.Capture`, root `MyScreenRecorder`) so moved files keep their diffs small. Moves use `git mv`.

### 3.3 Dependency rules

- Core references no UI framework (WPF, WinForms, Avalonia) and calls no OS-specific API (no P/Invoke, no `OperatingSystem.Is*` branches). It may contain pure data/argument builders that describe a platform (e.g. `GdiGrab`, `X11Grab`).
- Each app project is its own composition root: it builds a `PlatformServices` record and passes it into Core.
- Platform.Linux references Core only. The Linux app references Core and Platform.Linux. Neither Linux project references the WPF project, and the WPF project references no Linux project.
- Platform classes are marked `[SupportedOSPlatform("linux")]` / `[SupportedOSPlatform("windows")]`.
- Tests reference only the project they test (plus Core).

### 3.4 Boundary moves (approved, D9)

1. `Settings` moves from the UI root to Core; the settings path moves into a new `AppPaths` (removes the duplicate path at `MainWindow.xaml.cs:50`).
2. `CaptureTarget.ForAll`/`ForRegion` take the monitor list instead of calling `ScreenInfo`/WinForms. `ForAll` uses the union of monitor bounds (equal to `SystemInformation.VirtualScreen`, which is documented as the bounding rectangle of all displays).
3. Recording stops referencing `Native`: process start goes through `IChildProcessLauncher` (Windows implementation wraps `KillOnCloseJob`).
4. ffmpeg capture arguments (gdigrab, x11grab) are pure builders in Core so they are unit-testable on Linux.
5. Linux platform services live in `MyScreenRecorder.Platform.Linux`; Windows platform services stay in the WPF project.
6. `PreviewGrabber` (UI code living in `Capture/`) stays in the WPF project untouched.

## 4. Seams (Core, namespace `MyScreenRecorder.Platform`)

Docstrings on these interfaces are required (D7). Members are the contract; a story that needs a different member stops and asks.

```csharp
public interface IChildProcessLauncher
{
    // Starts the pre-configured process (StartInfo, events already set) and registers it for cleanup.
    // Throws whatever Process.Start throws. Never returns before the process has started.
    void Start(Process process);
}

public interface IAudioPipe : IDisposable
{
    string FfmpegInput { get; }          // value for ffmpeg -i
    Stream Stream { get; }               // write side, f32le 48 kHz stereo
    Task WaitForConnectionAsync(CancellationToken cancellationToken);
}

public interface IAudioPipeFactory
{
    IAudioPipe Create(string sessionId, int index);
}

public interface IAudioCapture : IDisposable
{
    WaveFormat Format { get; }
    event Action<byte[], int>? DataAvailable;   // buffer, valid byte count; raised on a capture thread
    event Action? Stopped;
    void Start();
    void Stop();
}

public interface IAudioCaptureFactory
{
    // Throws if deviceId is not a currently present device (never falls back to another device).
    IAudioCapture Open(string deviceId, bool loopback);
}

public interface IAudioDeviceCatalog
{
    IReadOnlyList<AudioDeviceInfo> Inputs();
    IReadOnlyList<AudioDeviceInfo> Outputs();
    string? DefaultInputId();
    string? DefaultOutputId();
}

public interface IScreenCatalog
{
    IReadOnlyList<MonitorInfo> GetMonitors();   // ordered by Left, then Top; numbered from 1
}
```

Pure builders in Core (`MyScreenRecorder.Recording`):

- `ScreenGrab` (abstract): `InputArgs(Rectangle bounds, int framerate)`, `EncoderArgs()` (appended after `Quality.VideoArgList`), `ApplyEnvironment(IDictionary<string, string?> environment)`.
  - `GdiGrab`: exactly today's args (`-f gdigrab -framerate 15 -draw_mouse 1 -offset_x X -offset_y Y -video_size WxH -thread_queue_size 1024 -i desktop`), `EncoderArgs` empty, environment `__COMPAT_LAYER=HighDpiAware`.
  - `X11Grab(string display)`: `-f x11grab -framerate 15 -draw_mouse 1 -video_size WxH -thread_queue_size 1024 -i <display>+X,Y`, `EncoderArgs` = `-x264-params sliced-threads=1` (D8), no environment change.
- `FfmpegLocatorOptions(string ExecutableName, Assembly? ResourceAssembly, string? ResourceName, string ExtractDirectory)`. The locator splits `PATH` with `Path.PathSeparator`. Windows: `("ffmpeg.exe", <WPF assembly>, "ffmpeg.exe", %LOCALAPPDATA%\MyScreenRecorder)`. Linux: `("ffmpeg", null, null, <unused>)`.
- `PlatformServices` record: grows as stories add seams (1.2: launcher, pipes, grab, ffmpeg options; 1.3: capture factory, device catalog; 1.4: screen catalog).

## 5. Linux design per area

### 5.1 Process hygiene (story 2.2)

- **Trap:** `PR_SET_PDEATHSIG` fires when the *thread* that forked the child exits, and .NET 10 forks on the thread that calls `Process.Start` (`pal_process.c`, `vfork()` under glibc). Measured: a child started from a short-lived thread was killed after 0.27 s; from `Task.Run` after 20 s (pool thread retired). Code after `ConfigureAwait(false)` runs on pool threads, so `SegmentWriter`/`RecordingSession` would hit it.
- `LinuxChildProcessLauncher` owns **one dedicated background thread**, created at app start and alive until process exit, that executes every `Process.Start` (work queue + `TaskCompletionSource`; exceptions are delivered to the caller, never end the thread).
- It rewrites the start info to `setpriv --pdeathsig TERM -- <original file> <original args>`. `setpriv` execs, so `Process.Id` is ffmpeg's PID and `Kill(entireProcessTree: true)` still works.
- `TERM`, not `KILL`: measured, `TERM` lets ffmpeg write a valid MKV segment; `KILL` left a 0-byte file.
- At startup the launcher checks `setpriv --help` output contains `--pdeathsig`. If not, it starts processes unwrapped, keeps their PIDs, and kills them with `Kill(true)` on `AppDomain.ProcessExit`; the UI shows `WARNING: setpriv not found; ffmpeg may outlive a crash`.
- Every ffmpeg the Linux app starts (segments, concat, preview, capability check) goes through the launcher.
- Per-process limit: exactly one spawn thread per app process. Several app instances each have their own thread; nothing is shared between instances except the output folder (file names carry a timestamp) and socket names (carry a 32-hex session id).

### 5.2 Audio capture (story 2.4)

- `PulseSimpleCapture : IAudioCapture` P/Invokes `libpulse-simple.so.0` (`pa_simple_new`, `pa_simple_read`, `pa_simple_free`, `pa_strerror`; `PA_STREAM_RECORD = 2`, `PA_SAMPLE_FLOAT32LE = 5`). Use the versioned library names; bare names fail because only `.so.0` exists.
- Sample spec float32le / 48000 / 2 channels; the server converts (a 4-channel s32le sink monitor arrives as 2-channel float). `pa_buffer_attr.fragsize = 3840` bytes (20 ms). Without it the first read takes ~2 s.
- One blocking read thread per source; it raises `DataAvailable`. `pa_simple_*` calls for one stream happen only on that thread (including `pa_simple_free` at shutdown).
- `loopback` is ignored by the Linux factory: the "speaker" device id is already a monitor source name.
- **Trap:** pipewire-pulse silently connects to the default device when the requested name does not exist. The factory checks the id against the catalog before opening and throws otherwise.
- The shared chain stays: `BufferedWaveProvider` → `ToSampleProvider` → `StereoDownmixSampleProvider` → (no resampler at 48 kHz) → `MixingSampleProvider` (NAudio.Core works on Linux, verified).
- Idle meters (D5): sources capture continuously like on Windows, so the desktop shows the microphone as in use.

### 5.3 Device catalog (story 2.4)

- `PactlDeviceCatalog` runs `pactl --format=json list sources` / `list sinks` and `pactl get-default-source` / `get-default-sink` (pactl ≥ 16 needed for JSON; the dev machine has 16.1).
- Inputs = sources whose `properties."device.class"` is not `monitor` **and** whose `monitor_source` is empty (on a source entry `monitor_source` names the monitored sink). Display name = `description`. Id = `name`.
- Outputs = sinks. Display name = sink `description`. Id = the sink's `monitor_source` (`<sink>.monitor`). Default output id = monitor of the default sink.
- If `pactl` is missing or fails, both lists are empty and the UI shows `ERROR: pactl not found (install pulseaudio-utils)`.

### 5.4 Audio transport (story 2.3)

- `UnixSocketPipeFactory` uses the same `NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 1 << 20)` as Windows, with a **rooted** name `$XDG_RUNTIME_DIR/srec_<sessionId>_<index>`; `FfmpegInput` = `unix:<that path>`.
- On Unix the server binds and listens in the constructor; a rooted name is used verbatim; Dispose unlinks the file (dotnet/runtime `NamedPipeServerStream.Unix.cs`). Verified end to end with ffmpeg `-i unix:<path>`; connection in ~10 ms.
- If `XDG_RUNTIME_DIR` is empty, not an existing directory, or the UTF-8 path exceeds 107 bytes (`sun_path` is 108 bytes), use the non-rooted name `srec_<sessionId>_<index>` (→ `/tmp/CoreFxPipe_…`, still protected by the `CurrentUserOnly` peer-credential check).
- Linux caps the socket send buffer at `2 × net.core.wmem_max` (425 984 bytes here), about 1.1 s of audio; the D8 encoder fix keeps the writer from blocking.

### 5.5 Video capture (story 2.3)

- `X11Grab` with `display` = the `DISPLAY` environment value at segment start; coordinates are X root-window pixels (= Avalonia `Screen.Bounds`, verified against `xrandr --listmonitors`). Width/height stay even (`CaptureTarget`); odd offsets are allowed.
- Stop stays `q\n` on stdin (exit code 0). `SIGINT`/`SIGTERM` also produce valid files but exit with 255; no code may treat 255 from a signalled stop as a recording failure.
- Never add `-shortest` (stalls the pipeline, measured).
- Known residuals, accepted: per segment the audio ends 0.15–0.27 s before the video and starts ~65 ms ahead of it.
- Capability check at startup (Linux app): `ffmpeg -hide_banner -devices` contains `x11grab`; `ffmpeg -hide_banner -protocols` lists `unix` under `Input:`. Otherwise status `ERROR: ffmpeg at <path> lacks x11grab/unix support` and REC disabled.

### 5.6 Screens and session (stories 2.1, 3.1)

- `SessionCheck` (Platform.Linux): Wayland if `XDG_SESSION_TYPE` equals `wayland` (case-insensitive) or `WAYLAND_DISPLAY` is non-empty; no X if `DISPLAY` is empty. Reads an injected environment dictionary (testable).
- `AvaloniaScreenCatalog` (Linux app): `Screens.All` → `MonitorInfo(number, Bounds as Rectangle, IsPrimary)`, ordered by Left then Top. `Scaling` is ignored: bounds are already physical root pixels.

### 5.7 Preview (story 3.2)

- A persistent ffmpeg per selected target, started through the launcher: `-f x11grab -framerate 4 -draw_mouse 1 -video_size WxH -i <display>+X,Y -vf scale=TW:TH:flags=fast_bilinear -f rawvideo -pix_fmt bgra pipe:1`, where `TW×TH` is computed like `PreviewGrabber` (max 476×266, never upscaled). The app reads `TW*TH*4`-byte frames from stdout into an Avalonia `WriteableBitmap`.
- Restarted on target change; stopped when the window is minimised or closed.
- Measured cost on a 2560×1440 monitor: 4 % of one core (`fast_bilinear`).
- Rejected: `XGetImage` per tick (41 ms and 36 MB per grab at 6400×1440); one-shot ffmpeg per frame (28–56 % of a core at 4 fps).

### 5.8 Overlays (story 3.4)

- X11 has no `WDA_EXCLUDEFROMCAPTURE`: any window over the capture area is recorded (verified pixel-exact).
- **Badges** (monitor number) are shown only while Idle. **Trap:** today the overlay state changes after `StartAsync` returns, i.e. after ffmpeg has started. The Linux UI must hide the badges **before** calling `StartAsync`/`ResumeAsync`.
- **Border** is drawn only for REGION targets, and only the edges that lie fully outside the captured rectangle after clamping to the monitor (an edge that would overlap the rectangle is not drawn). Full-monitor and ALL targets have no border on Linux; the status line shows the state.
- Mechanism: raw Xlib override-redirect windows on the app's own `XOpenDisplay` connection, input made click-through with `XShapeCombineRectangles(…, ShapeInput, 0 rectangles, ShapeSet)` (`libXext.so.6`), verified by probe. Avalonia windows are **not** used for overlays: they are WM-managed (Muffin moves windows away from panels and monitor edges, measured), steal focus unless `ShowActivated=false`, and have no window-level click-through API (only popups; the GLX child render window keeps receiving input).
- Badge content (number in the pixel font) is rendered by Avalonia `RenderTargetBitmap` and copied into the X window with `XPutImage`.

### 5.9 Region selector (story 3.3)

- Per-monitor transparent Avalonia windows like `RegionSelectWindow`; global cursor position via `XQueryPointer` (`libX11.so.6`).
- Muffin may constrain WM-managed windows near panels (measured). The story starts with a probe of a monitor-sized borderless window on each monitor (`xwininfo`) and stops to ask if placement differs from the monitor bounds.

### 5.10 ffmpeg delivery and locator (stories 1.2, 2.3, 4.1)

- **Trap:** after `FfmpegLocator` moves to Core, `Assembly.GetExecutingAssembly()` (`FfmpegLocator.cs:44`) would be the Core assembly, so the embedded Windows ffmpeg would no longer be found and Windows users would see `ERROR: ffmpeg not found`. The locator takes the resource assembly from `FfmpegLocatorOptions`.
- Linux: `build.sh` places the BtbN `ffmpeg` (and `ffprobe` for tests) in `tools/linux/`; the Linux app copies `tools/linux/ffmpeg` to its output/publish directory (`ExcludeFromSingleFile`), so `dotnet run` and the tarball both find it beside the executable.
- BtbN builds link glibc dynamically (needs glibc ≥ 2.28) and dlopen system `libX11`/`libxcb*` lazily: a missing library fails only at first capture. Document the runtime dependencies in README.
- BtbN `latest` assets change daily; pin a month-end `autobuild-YYYY-MM-*` tag (kept two years) and its sha256 from the release's `checksums.sha256`.
- The build is GPLv3 (`--enable-gpl --enable-version3`); the tarball carries ffmpeg's license file and a pointer to the corresponding source (BtbN release URL + FFmpeg git tag).

### 5.11 Paths (verified on the dev machine)

| Purpose | API | Linux value |
|---|---|---|
| Recordings | `SpecialFolder.MyVideos` + `MyScreenRecorder` (fallback `UserProfile/Videos` when it returns `""`, existing code) | `~/Videos/MyScreenRecorder` (from `XDG_VIDEOS_DIR`) |
| Settings | `SpecialFolder.ApplicationData` with `SpecialFolderOption.DoNotVerify` + `MyScreenRecorder/settings.json` (without `DoNotVerify` a missing config folder yields `""` and a relative path; decided in story 1.1) | `~/.config/MyScreenRecorder/settings.json` |
| Sockets | `$XDG_RUNTIME_DIR` | `/run/user/1000` |

`MyVideos` returns `""` when the folder does not exist (dotnet/runtime `Environment.GetFolderPathCore.Unix.cs`); the existing fallback in `RecordingSession.OutputFolder` covers it.

### 5.12 Linux UI (stories 2.1, 3.x)

- Avalonia 12.1.4 (`Avalonia.Desktop`, `Avalonia.Themes.Simple`), `net10.0`. Avalonia 12 renamed `SystemDecorations` to `WindowDecorations` and removed `AvaloniaLocator.Current`.
- Pixel font PressStart2P as `AvaloniaResource`, `avares://MyScreenRecorder/Assets/Fonts#Press Start 2P`, fallback `monospace` (Consolas does not exist on Linux).
- Cancel confirmation: a small own Avalonia dialog (Yes/No, default No), not a third-party MessageBox package.
- OPEN: `xdg-open <output folder>` through `Process.Start` with `UseShellExecute = false`.
- Crash handling: `Dispatcher.UIThread.UnhandledException` and `AppDomain.CurrentDomain.UnhandledException` show the error text (like `App.xaml.cs`).

### 5.13 Build and release (stories 4.1, 4.2)

- `build.sh` mirrors `build.ps1`: pinned BtbN download + sha256 check, font download (warning on failure), `dotnet test`, `dotnet publish src/MyScreenRecorder.Linux -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/linux`, then `dist/MyScreenRecorder-linux-x64.tar.gz` with `MyScreenRecorder`, `ffmpeg` (both mode 0755), `README.md`, ffmpeg license.
- `release.yml`: `build-windows` (windows-latest, `build.ps1`) and `build-linux` (ubuntu-latest, `build.sh`) upload artifacts; `release` (needs both) downloads them, creates a **draft** release with both assets, then publishes it (`gh release edit --draft=false`). A failure before publishing leaves at most a draft, never a public half release.

## 6. Verified facts

Probed on the dev machine (Mint 22.3, X11 Cinnamon/Muffin, PipeWire 1.0.5 + pipewire-pulse, kernel 6.8, glibc 2.39, 12 threads; monitors DP-1 1920×1080+0+116, HDMI-0 2560×1440+1920+0 primary, HDMI-1 1920×1080+4480+143, root 6400×1440) with .NET SDK 10.0.403, BtbN `n9.0.2-37-gae19868753-20261028`, Avalonia 12.1.4, NAudio 2.2.1.

| Fact | Evidence / how to re-check |
|---|---|
| BtbN linux64-gpl has x11grab, pulse, libx264, aac, pcm_s16le, `unix` protocol; no alsa, no pipewire | `ffmpeg -hide_banner -devices`, `-encoders`, `-protocols` |
| x11grab with the project's args yields correct size/duration; concat `-c copy` of segments works | 3 s grab of `:0.0+1920,0` 2560×1440 → 1280×720, 3.067 s |
| Region outside the root fails with exit 234 | "Capture area … outside the screen size 6400x1440" |
| `q` → exit 0; SIGINT/SIGTERM → valid file, exit 255 | ffmpeg `fftools/ffmpeg.c` sigterm handler; probe |
| ffmpeg ignores stdin EOF; an orphan keeps recording | probe: `kill -9` of the parent → ffmpeg reparented to PID 1, still running |
| PDEATHSIG thread trap in .NET 10 | probe (0.27 s / 20 s kills); dotnet/runtime `release/10.0` `pal_process.c` |
| Dedicated long-lived spawn thread + `setpriv --pdeathsig TERM` → child alive while app runs, valid MKV after `kill -9` of the app | probe |
| NamedPipeServerStream ↔ ffmpeg `unix:` works; rooted path used verbatim; unlinked on Dispose | probe; dotnet/runtime `NamedPipeServerStream.Unix.cs` |
| x264 tail loss 1.1–1.4 s/segment; `-x264-params sliced-threads=1` → 0.15–0.27 s | probe with sine pump over the socket and with ffmpeg `-f pulse` |
| NAudio.Core 2.2.1 mixing/resampling/buffering work on Linux; WASAPI types throw `PlatformNotSupportedException` | probe |
| libpulse-simple float32le/48k/2 works on pipewire-pulse; `fragsize` 3840 → 18 ms first read; NULL attr → ~2 s | probe (Python ctypes, same C ABI) |
| Unknown pulse device name silently falls back to the default device | `pactl list source-outputs` during probe |
| Avalonia `Screens.All` == `xrandr --listmonitors`; `TryGetPlatformHandle()` → XID | probe |
| Topmost windows are captured by x11grab | probe (pixel-exact red window) |
| Override-redirect window + empty ShapeInput is click-through | probe (`XTranslateCoordinates` child changes) |
| Preview via ffmpeg 4 fps bgra stream, `fast_bilinear`: 4 % CPU (2560×1440) | probe |
| WPF project builds on Linux with `EnableWindowsTargeting=true` (0 errors, BAML compiled); without it `NETSDK1100` | probe on a copy of the project |
| xunit.v3 4.0.2 needs `global.json` `{"test":{"runner":"Microsoft.Testing.Platform"}}` on SDK 10; then `dotnet test --project …`, `--solution …`, `--filter-not-trait "Category=Integration"` work | probe; without global.json: "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK" |
| Single-file publish of an Avalonia app: 48 MB without ffmpeg | probe |

## 7. Risks

| Risk | Mitigation | Windows regression |
|---|---|---|
| Core extraction breaks Windows (no Windows machine here) | Pure moves + wrappers; byte-identical ffmpeg argument tests; compile check on Linux; user's Windows smoke checklist per Epic 1 story | 1.1 low, 1.2 high, 1.3 medium |
| Embedded Windows ffmpeg silently lost | Locator takes the resource assembly; smoke item with empty PATH | 1.2 |
| ffmpeg killed mid-recording by the PDEATHSIG trap | Dedicated spawn thread; integration test that starts from a pool thread and survives 30 s | none |
| Missing system libs on other distros (libxcb, libpulse-simple, fontconfig, pactl, setpriv) | Capability checks with specific messages; README dependency list | none |
| Overlay placement on other window managers | Override-redirect windows are not WM-managed | none |
| Release workflow change breaks the Windows asset | Draft-then-publish; dry run on a test tag | 4.2 medium |
| Two UIs drift | Shared `RecorderController`; backlog story for Windows adoption | — |

## 8. Out of scope

- Wayland capture (future epic: xdg-desktop-portal ScreenCast + PipeWire).
- Changing the Windows UI or its behaviour (except the moves above); the Windows UI adopting `RecorderController` is a backlog story.
- AppImage/.deb/.desktop integration, tray indicator, selecting the file in the file manager (`org.freedesktop.FileManager1.ShowItems`), startup sweep of orphan ffmpeg processes, libpulse introspection replacing `pactl`, `.NET 11` `KillOnParentExit`: backlog.
- Pinning/checksumming the Windows ffmpeg download in `build.ps1`: backlog.
- ARM64 Linux.
