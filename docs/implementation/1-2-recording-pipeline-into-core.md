# Story 1.2: Recording pipeline into Core behind process/grab/pipe seams

Status: review

## Story

As the maintainer of MyScreenRecorder,
I want the segmented recording pipeline (`SegmentWriter`, `RecordingSession`, `FfmpegLocator`) in `MyScreenRecorder.Core`, reaching processes, the audio pipe and the screen grab only through seams,
so that the Linux build can reuse the pipeline with a Linux process launcher, a Unix-socket pipe and `x11grab`, while the Windows ffmpeg arguments and behaviour stay byte-identical.

<!-- Track: Core — Covers: NFR1, NFR2, NFR4 — Decisions: D3, D4, D6, D7, D8, D9 — Windows smoke: required -->

## Decision Record

Decisions of the architecture document (D1–D11) are not reopened here. The entries below are the decisions this story adds; the reviewer may challenge any of them before development starts.

- 2026-11-03: 1-1 and 1-3 are done and merged (`75765fa`), so 1-2 is unblocked. The code this story moves (`Recording/SegmentWriter.cs`, `RecordingSession.cs`, `FfmpegLocator.cs`, `Native/Win32.cs`) is unchanged between `b6259ee` and `75765fa` (`git diff b6259ee HEAD -- <path>` is empty for all four), so "today" and "`b6259ee`" are the same code.
- 2026-11-03: `PlatformServices` is **extended**, not created (1-3 created it). New shape, in this member order: `public sealed record PlatformServices(IAudioCaptureFactory AudioCaptures, IAudioDeviceCatalog AudioDevices, IChildProcessLauncher ProcessLauncher, IAudioPipeFactory AudioPipes, ScreenGrab Grab, FfmpegLocatorOptions Ffmpeg);`. Reason: architecture §4 lists exactly these additions for 1.2; the order keeps the 1-3 members first so the diff is an append.
- 2026-11-03: `SegmentWriter` takes the narrow dependencies `IChildProcessLauncher`, `IAudioPipeFactory`, `ScreenGrab` (not `PlatformServices`); `RecordingSession` takes `PlatformServices` plus an `FfmpegLocator`: `public RecordingSession(AudioEngine audio, PlatformServices platform, FfmpegLocator ffmpeg)`. Reason: same rule as 1-3 (`AudioEngine` takes only the factory): the narrowest dependency, tests pass fakes. `RecordingSession` is the object that picks the four members it needs out of the record and hands them to each `SegmentWriter`. Rejected: `RecordingSession(AudioEngine, PlatformServices)` creating the locator itself (the WPF window also asks the locator at `MainWindow.xaml.cs:102` and `:360`; two instances would mean two caches).
- 2026-11-03: `FfmpegLocator` changes from a `static` class to an instance class `FfmpegLocator(FfmpegLocatorOptions options)` with `string? Resolve()`; one instance per app, created by the composition root and passed to `RecordingSession` and used by `MainWindow`. An `internal` constructor `(FfmpegLocatorOptions options, string baseDirectory, Func<string, string?> getEnvironmentVariable)` is the test seam (`InternalsVisibleTo("MyScreenRecorder.Core.Tests")` already exists); the public constructor passes `AppContext.BaseDirectory` and `Environment.GetEnvironmentVariable`. Reason: the options differ per OS, so a static cache keyed by nothing would be wrong; the injected base directory and environment let the tests run without changing the process `PATH`. Rejected: static class with a settable `Options` property (hidden global state); mutating the process environment in tests (needs the non-parallel collection and still cannot control `AppContext.BaseDirectory`).
- 2026-11-03: **Lookup order is unchanged**: file `<options.ExecutableName>` beside the executable (`AppContext.BaseDirectory`), then the embedded resource (skipped when `ResourceAssembly` is `null`), then `PATH`. Reason: byte-identical Windows behaviour (`FfmpegLocator.cs:25-37`). On Linux (`ResourceAssembly = null`) this yields D6's "beside the executable, then `PATH`" with no Linux branch. The epic's AC3 wording ("when `PATH` has no ffmpeg and none is beside the exe, then the embedded resource is extracted") is satisfied by the order but does not say embedded comes before `PATH`; the code order is the contract.
- 2026-11-03: The embedded extraction keeps today's naming: target file `<name without extension>_<resource length><extension of ResourceName>` (for `ResourceName = "ffmpeg.exe"` this is `ffmpeg_<length>.exe`), temp file `<same stem>_<length>_<32-hex guid>.tmp`, in `options.ExtractDirectory`. Resource-name match is `string.Equals(n, ResourceName, OrdinalIgnoreCase) || n.EndsWith("." + ResourceName, OrdinalIgnoreCase)` (today: `"ffmpeg.exe"` and `".ffmpeg.exe"`). `ExtractDirectory` is computed eagerly by the composition root (`Path.Combine(Environment.GetFolderPath(LocalApplicationData), "MyScreenRecorder")`); `Directory.CreateDirectory` stays inside the locator's try/catch.
- 2026-11-03: `ScreenGrab` is an abstract class in `MyScreenRecorder.Recording` with three **abstract** members: `IReadOnlyList<string> InputArgs(Rectangle bounds, int framerate)`, `IReadOnlyList<string> EncoderArgs()`, `void ApplyEnvironment(IDictionary<string, string?> environment)`. `ProcessStartInfo.Environment` is `IDictionary<string, string?>`, so it is passed as is. Reason: architecture §4 names the three members; abstract (not virtual) makes each grab state them explicitly. Rejected: virtual defaults (a new grab could silently inherit "no encoder args").
- 2026-11-03: `ScreenGrab.EncoderArgs()` output goes **right after `Quality.VideoArgList(...)` and before `Quality.AudioArgList(...)`** in the segment command line. Reason: architecture §4 says "appended after `Quality.VideoArgList`" without saying where relative to the audio args; it is a video-encoder option, so it sits with the video options. For `GdiGrab` it is empty, so the Windows list is unchanged. ffmpeg treats output options in any order before the output file.
- 2026-11-03: The even-size rule stays in `SegmentWriter`: it builds `new Rectangle(b.X, b.Y, Math.Max(2, b.Width & ~1), Math.Max(2, b.Height & ~1))` (today `SegmentWriter.cs:155-156`) and passes that to `ScreenGrab.InputArgs`, which prints `-video_size` verbatim. Reason: byte-identical for gdigrab, and the same rule then applies to x11grab. Rejected: moving the rule into each grab (two copies).
- 2026-11-03: `SegmentWriter.BuildStartInfo` becomes `internal ProcessStartInfo BuildStartInfo(string? audioInput)`; `null` means "no audio", otherwise it is `IAudioPipe.FfmpegInput`. Reason: the `hasAudio`/`pipeName` pair becomes one value; `internal` so Core.Tests can assert the exact argument list (AC1, AC2).
- 2026-11-03: `X11Grab(string display)` receives the display at construction, so `PlatformServices.Grab` carries one display value for the app's lifetime. Architecture §5.5 originally said "at segment start"; it was amended on 2026-11-03 (with this story) to: the Linux composition root reads `DISPLAY` once at startup and passes it to `X11Grab`, since `DISPLAY` does not change for a running X11 client. Story 3-1 implements that; this story only fixes the constructor shape.
- 2026-11-03: `RecordingSession.OutputFolder` (static) is **removed**; the same body moves to `AppPaths.OutputFolder` (Core, `MyScreenRecorder` namespace, next to `SettingsFile`), and `MainWindow` (`:68`, `:337`) and `RecordingSession` call `AppPaths.OutputFolder`. The body is moved as is, including `Directory.CreateDirectory(folder)` inside a swallowing try/catch (a getter with a side effect, kept for byte-identical behaviour). Reason: the epic lists `AppPaths.OutputFolder` for this story and 1-1 already introduced `AppPaths` to avoid duplicate paths. Rejected: leaving a forwarding `RecordingSession.OutputFolder` (two names for one path).
- 2026-11-03: The concat (finalize) process also goes through `IChildProcessLauncher` but gets **no** `ScreenGrab.ApplyEnvironment` and no `StandardInputEncoding` (today's concat start info has neither: `RecordingSession.cs:361-382`). Reason: byte-identical; also architecture §5.1 requires every ffmpeg to go through the launcher.
- 2026-11-03: Windows implementations are `internal sealed` classes in `src/MyScreenRecorder/Platform/Windows/`, namespace `MyScreenRecorder.Platform.Windows`, marked `[SupportedOSPlatform("windows")]`: `JobObjectProcessLauncher : IChildProcessLauncher`, `WindowsNamedPipeFactory : IAudioPipeFactory`, `WindowsNamedPipe : IAudioPipe`. `KillOnCloseJob` stays `internal` in `Native/Win32.cs`, unchanged. Core types carry no `SupportedOSPlatform` (architecture §3.3: Core has no OS calls).
- 2026-11-03: The pipeline gets behaviour tests in Core.Tests, not only argument tests: a fake `ffmpeg` shell script (text in Dev Notes) plus fake launcher/pipe lets `SegmentWriter` and `RecordingSession` run end to end on Linux. These tests are skipped on Windows (`Assert.SkipUnless(!OperatingSystem.IsWindows(), ...)`). Reason: epic risk table rates this story "high" for Windows regression and the 1-1/1-3 reviews found untested moved branches every time. Rejected: argument tests only (leaves the state machine, pipe ordering, fault, cancel and finalize-failure branches unguarded).
- 2026-11-03: Two slow tests are accepted: the 10 s audio-pipe connect timeout and the 15 s `q`-ignored kill fallback (about 25 s together). Reason: both constants are moved behaviour with an observable effect (NFR2 hygiene); injecting a clock or constants would change the moved code. Rejected: making the timeouts injectable.
- 2026-11-03: Acceptance-criteria numbering differs from the epic: story AC1–AC4 = epic AC1–AC4; story AC5 (pipeline behaviour), AC6 (`AppPaths.OutputFolder`) are added; the epic's AC5 "Windows unchanged" is story AC7.
- 2026-11-03: Out-of-scope code that looks related is listed under Contract Guardrail.
- 2026-11-03 (full review, decided with the user): `IChildProcessLauncher.Start` throws only when the process did not start; once started, wrapping/registration failures are handled inside the launcher and never thrown. Written into the interface docstring and architecture §4. Reason: `SegmentWriter.StartAsync` and `RecordingSession.FinalizeAsync` dispose on catch without killing (probe: a launcher that starts a child and then throws leaves it running). Rejected: a defensive kill in every caller (spreads to each future caller and changes moved code). Story 2-2 tests the Linux launcher against it.

## Acceptance Criteria

1. **Byte-identical Windows args**
   **Given** a `SegmentWriter` with `GdiGrab`, ffmpeg path `C:\ff\ffmpeg.exe`, parts dir `/tmp/parts/2026-11-03_10-00-00`, index 0, target bounds `(X=-1920, Y=116, W=1921, H=1081)`, `VideoQuality.P720`, `AudioQuality.Aac256`
   **When** `BuildStartInfo` is called with audio input `\\.\pipe\srec_0123456789abcdef0123456789abcdef_0`
   **Then** `ArgumentList` equals the "Windows, with audio" list in Testing Requirements element by element (this list was produced by running the released `BuildStartInfo` from `b6259ee` in a probe, see Dev Notes)
   **When** `BuildStartInfo` is called with `null`
   **Then** `ArgumentList` equals the "Windows, without audio" list
   **And** in both cases `FileName == "C:\ff\ffmpeg.exe"`, `WorkingDirectory` is the parts dir, `UseShellExecute == false`, `CreateNoWindow == true`, `RedirectStandardInput/Output/Error == true`, `StandardInputEncoding` is a `UTF8Encoding` with an empty preamble, and `Environment` equals `new ProcessStartInfo().Environment` plus exactly one entry `__COMPAT_LAYER=HighDpiAware`
   **And** `GdiGrab.InputArgs(new Rectangle(0, 0, 1920, 1080), 15)` is `-f gdigrab -framerate 15 -draw_mouse 1 -offset_x 0 -offset_y 0 -video_size 1920x1080 -thread_queue_size 1024 -i desktop`, `EncoderArgs()` is empty.

2. **X11 args**
   **Given** `X11Grab(":0")`, bounds `(1920, 0, 2560, 1440)`, `P720`, `Aac256`, audio input `unix:/run/user/1000/srec_0123456789abcdef0123456789abcdef_0`, parts dir `/tmp/parts/2026-11-03_10-00-00`, ffmpeg path `/opt/ffmpeg`
   **When** `BuildStartInfo` runs
   **Then** `ArgumentList` equals the "X11, with audio" list in Testing Requirements (x11grab input `-i :0+1920,0`, the `unix:` audio input, the `Quality` args, then `-x264-params sliced-threads=1` before the audio args)
   **And** `Environment` equals `new ProcessStartInfo().Environment` (no entry added or changed)
   **And** `StandardInputEncoding`, redirects and flags equal AC1's.

3. **Embedded ffmpeg found**
   **Given** `FfmpegLocatorOptions("ffmpeg.exe", <assembly with an embedded resource named ffmpeg.exe>, "ffmpeg.exe", <empty temp dir>)`, an empty base directory, and an environment whose `PATH` lists no ffmpeg
   **When** `Resolve()` runs
   **Then** it returns `<temp dir>/ffmpeg_<resource length>.exe`, the file exists and has the resource's exact bytes
   **And** a second locator over the same options returns the same path without rewriting the file, a pre-existing file of another length at that path is replaced by the resource bytes, and no `*.tmp` file is left behind.

4. **PATH split and lookup order**
   **Given** a `PATH` value built with `Path.PathSeparator` that lists a directory without the executable first and a directory with `<ExecutableName>` second
   **When** `Resolve()` runs (options without a resource assembly)
   **Then** the second directory's executable path is returned
   **And** an executable beside the base directory wins over the embedded resource, and the embedded resource wins over `PATH`
   **And** `PATH` entries are trimmed of whitespace and surrounding double quotes, empty entries are skipped, an unset or empty `PATH` yields `null`, and `Resolve()` returns `null` when nothing is found.

5. **Pipeline behaves as before through the seams**
   **Given** the fake launcher/pipe factory and the fake `ffmpeg` script
   **When** `SegmentWriter` and `RecordingSession` run the scenarios listed in Testing Requirements ("Behaviour tests")
   **Then** every listed observable result holds (pipe created before the process starts; audio begins only after the pipe connects; error texts; resource release on failure; one `Faulted` per fault; stop by `q`; kill fallback; segment files and `list.txt`; concat command line; final file name and extension; parts removed on success and cancel, kept on failure; `LastError` texts).

6. **Output folder**
   **Given** `HOME` is a temp dir and `XDG_CONFIG_HOME` points at an empty temp dir
   **When** `AppPaths.OutputFolder` is read
   **Then** it is `<HOME>/Videos/MyScreenRecorder` and the folder exists
   **Given** `user-dirs.dirs` sets `XDG_VIDEOS_DIR="$HOME/Movies"` and `<HOME>/Movies` exists
   **Then** it is `<HOME>/Movies/MyScreenRecorder`; when `<HOME>/Movies` does not exist it is `<HOME>/Videos/MyScreenRecorder` (the `MyVideos` result is `""`, so the fallback applies)
   **And** when the folder cannot be created (a regular file stands where `<HOME>/Videos` should be) the property still returns the path and does not throw.

7. **Windows behaviour unchanged**
   **Given** `.\build.ps1` on Windows, on a machine where `ffmpeg.exe` is not on `PATH` and not beside the exe
   **When** the user runs the Windows smoke checklist W1–W9 (`docs/planning/epics-linux-build.md` §4)
   **Then** every item passes; W1 exercises the embedded ffmpeg (AC3), W3 pause/resume/finalize, W4/W5 `GdiGrab` bounds, W6 cancel, W7 `.mkv`, W8 the job object (no `ffmpeg.exe` left), W9 settings. W3's clap check is informational (D8).

## Tasks / Subtasks

- [x] Task 1: Branch and prerequisites (AC: all)
  - [x] `~/.dotnet/dotnet --version` (or `dotnet --version`) → `10.0.x`. If missing, stop and ask.
  - [x] The branch `feat/linux-1-2-recording-pipeline-into-core` is already checked out at `75765fa` (= `origin/main`, PR #2 merged). Check `git branch --show-current`, `git log --oneline -1` (`75765fa`), `git config branch.feat/linux-1-2-recording-pipeline-into-core.merge` prints nothing. If the branch differs, stop and ask.
  - [x] `git status` shows four modified docs (`docs/implementation/1-3-audio-capture-seam.md`, `docs/implementation/sprint-status.yaml`, `docs/planning/epics-linux-build.md`, `docs/planning/linux-build-architecture.md`: bookkeeping for 1-3 done, this story, and the 1.2 clarifications) and the new untracked story file `docs/implementation/1-2-recording-pipeline-into-core.md`. Leave them as they are; do not stash or revert them; they go into the File List as "pre-existing".
  - [x] Confirm the premise "code unchanged since `b6259ee`": `git diff b6259ee HEAD -- src/MyScreenRecorder/Recording src/MyScreenRecorder/Native/Win32.cs` prints nothing. If it prints a diff, stop and ask.
- [x] Task 2: Seams in Core (AC: 1–5)
  - [x] `src/MyScreenRecorder.Core/Platform/IChildProcessLauncher.cs` (CREATE), namespace `MyScreenRecorder.Platform`: `void Start(Process process);` — architecture §4 verbatim. `///` (D7): starts the pre-configured process (start info and event handlers already set) and registers it for cleanup when the app dies; throws whatever `Process.Start` throws; does not return before the process has started; best-effort registration failures are swallowed; called from any thread.
  - [x] `src/MyScreenRecorder.Core/Platform/IAudioPipe.cs` (CREATE): `: IDisposable`, `string FfmpegInput { get; }`, `Stream Stream { get; }`, `Task WaitForConnectionAsync(CancellationToken cancellationToken);`. `///`: `FfmpegInput` is the value for ffmpeg `-i`; `Stream` is the write side carrying `f32le` 48 kHz stereo; the pipe accepts a connection from the moment `IAudioPipeFactory.Create` returns; `Dispose` closes the stream and releases the endpoint and may be called more than once; `WaitForConnectionAsync` completes when ffmpeg connects, throws `OperationCanceledException` when the token is cancelled, and `IOException` family on a broken endpoint.
  - [x] `src/MyScreenRecorder.Core/Platform/IAudioPipeFactory.cs` (CREATE): `IAudioPipe Create(string sessionId, int index);` `///`: `sessionId` is a 32-hex string, `index` the segment index; the pipe exists (listening) before `Create` returns, so a process started afterwards can connect; throws when the endpoint cannot be created.
  - [x] `src/MyScreenRecorder.Core/Recording/ScreenGrab.cs` (CREATE): abstract class with the three abstract members (Decision Record). `src/MyScreenRecorder.Core/Recording/GdiGrab.cs` (CREATE): `public sealed class GdiGrab : ScreenGrab`; `InputArgs` returns, in this order and with `CultureInfo.InvariantCulture` number formatting, `-f gdigrab -framerate <framerate> -draw_mouse 1 -offset_x <X> -offset_y <Y> -video_size <W>x<H> -thread_queue_size 1024 -i desktop`; `EncoderArgs()` returns an empty list; `ApplyEnvironment` sets `environment["__COMPAT_LAYER"] = "HighDpiAware"`. `src/MyScreenRecorder.Core/Recording/X11Grab.cs` (CREATE): `public sealed class X11Grab : ScreenGrab`, ctor `(string display)` (throws `ArgumentException` for null/empty); `InputArgs` returns `-f x11grab -framerate <framerate> -draw_mouse 1 -video_size <W>x<H> -thread_queue_size 1024 -i <display>+<X>,<Y>`; `EncoderArgs()` returns `-x264-params`, `sliced-threads=1` (D8); `ApplyEnvironment` does nothing. No docstrings on these (D7: only on platform interfaces and native wrappers).
  - [x] `src/MyScreenRecorder.Core/Recording/FfmpegLocatorOptions.cs` (CREATE): `public sealed record FfmpegLocatorOptions(string ExecutableName, Assembly? ResourceAssembly, string? ResourceName, string ExtractDirectory);` (architecture §4).
  - [x] `src/MyScreenRecorder.Core/Platform/PlatformServices.cs` (UPDATE): the six-member record from the Decision Record, `using MyScreenRecorder.Recording;`.
- [x] Task 3: Tests first (AC: 1–6) — write them before Tasks 4–8; they fail to compile until the types exist.
  - [x] `tests/MyScreenRecorder.Core.Tests/Resources/fake-ffmpeg.bin` (CREATE, any 20 bytes, e.g. the text `fake ffmpeg resource`) and `Resources/fake-suffixed.bin` (CREATE, a different length); `MyScreenRecorder.Core.Tests.csproj` (UPDATE): `<EmbeddedResource Include="Resources\fake-ffmpeg.bin" LogicalName="ffmpeg.exe" />` and `<EmbeddedResource Include="Resources\fake-suffixed.bin" LogicalName="Vendor.Tools.fake.exe" />`.
  - [x] Test support (CREATE in `tests/MyScreenRecorder.Core.Tests/`): `FakeChildProcessLauncher.cs` (records every `Process.StartInfo` and the `Process`; by default calls `process.Start()`; can be configured to throw a given exception on call N; has a callback invoked just before `Start`); `FakeAudioPipe.cs` + `FakeAudioPipeFactory.cs` (factory records `(sessionId, index)` and the creation order, returns a `FakeAudioPipe` whose `FfmpegInput` is set by the factory, `Stream` is a `MemoryStream` (`AudioEngine.EndSegment` disposes it, after which `Length` and `Position` throw `ObjectDisposedException`, so the fake exposes `WrittenBytes => ms.ToArray()`, which still works after `Dispose`, and `StreamClosed => !ms.CanWrite`), `Disposed` counts `Dispose()` calls; connection behaviour configurable: completes at once / never completes until the token is cancelled / faults with `IOException`); `FakeFfmpeg.cs` (creates a temp dir, writes the script from Dev Notes as `ffmpeg` with `File.SetUnixFileMode(path, UserRead | UserWrite | UserExecute)`, writes the `mode` file, exposes the directory, disposes recursively); `TestPlatform.cs` (builds a `PlatformServices` from the fakes plus a fake empty `IAudioDeviceCatalog`). `FakeAudioCapture`/`FakeAudioCaptureFactory` from 1-3 are reused for `HasAudio`.
  - [x] `ScreenGrabTests.cs` (CREATE): `GdiGrab` and `X11Grab` direct output (AC1 last bullet, AC2 input part), environment effects, `X11Grab` null/empty display throws, invariant-culture formatting (set `CultureInfo.CurrentCulture` to `de-DE` inside the test and restore it; the numbers stay `1920`, no group separators).
  - [x] `SegmentWriterStartInfoTests.cs` (CREATE): AC1 (both lists, flags, environment), AC2, odd bounds `(0,0,1921,1081)` → `-video_size 1920x1080`, width/height 1 → `2x2` (the `Math.Max(2, …)` branch), `FilePath` is `Path.Combine(partsDir, "seg_000.mkv")` and `seg_012.mkv` for index 12, audio-less map is `0:v` and no `-map 0:a`, audio quality `Wav` → `-c:a pcm_s16le` and `Aac128` → `-b:a 128k`, video quality `P480`/`P1080` → `min(480,ih)`/`min(1080,ih)`.
  - [x] `FfmpegLocatorTests.cs` (CREATE): AC3, AC4 and the table in Testing Requirements.
  - [x] `AppPathsTests.cs` (UPDATE, same non-parallel collection): AC6.
  - [x] `SegmentWriterTests.cs` and `RecordingSessionTests.cs` (CREATE): the behaviour tests (AC5) per the table in Testing Requirements. They use `FakeFfmpeg`, an `AudioEngine` over `FakeAudioCaptureFactory`, `FakeChildProcessLauncher`, `FakeAudioPipeFactory`.
- [x] Task 4: `AppPaths.OutputFolder` (AC: 6)
  - [x] `src/MyScreenRecorder.Core/AppPaths.cs` (UPDATE): add `public static string OutputFolder` with the body of `RecordingSession.OutputFolder` (`RecordingSession.cs:60-81`) copied as is; add `using System.IO` exists already.
- [x] Task 5: Move `FfmpegLocator` (AC: 3, 4)
  - [x] `git mv src/MyScreenRecorder/Recording/FfmpegLocator.cs src/MyScreenRecorder.Core/Recording/FfmpegLocator.cs` (namespace stays). Edits, and only these: class becomes `public sealed class FfmpegLocator` with the two constructors and instance fields `_options`, `_baseDirectory`, `_getEnvironmentVariable`, `_cached`, `_gate` (an instance `object`); `ResolveCore` uses `Path.Combine(_baseDirectory, _options.ExecutableName)`; `ExtractEmbedded` returns `null` at once when `_options.ResourceAssembly` or `_options.ResourceName` is `null`, uses `_options.ResourceAssembly` instead of `Assembly.GetExecutingAssembly()`, the name match and file naming from the Decision Record, `_options.ExtractDirectory` instead of the `LocalApplicationData` combine; `FindOnPath` reads `_getEnvironmentVariable("PATH")`, splits with `Path.PathSeparator`, and builds the candidate with `_options.ExecutableName`. The trim/skip/try-catch structure is unchanged.
- [x] Task 6: Move `SegmentWriter` (AC: 1, 2, 5)
  - [x] `git mv src/MyScreenRecorder/Recording/SegmentWriter.cs src/MyScreenRecorder.Core/Recording/SegmentWriter.cs`. Edit list (the review diffs against this list; nothing else changes):
    1. remove `using System.IO.Pipes;` and `using MyScreenRecorder.Native;`; add `using MyScreenRecorder.Platform;`; keep `using System.Text;` only if `UTF8Encoding` still needs it (it does).
    2. constructor gains `IChildProcessLauncher launcher, IAudioPipeFactory pipes, ScreenGrab grab` (stored in readonly fields); the signature becomes `(string ffmpegPath, string partsDir, int index, CaptureTarget target, VideoQuality vq, AudioQuality aq, AudioEngine audio, string sessionId, IChildProcessLauncher launcher, IAudioPipeFactory pipes, ScreenGrab grab)`.
    3. `_pipe` field type `NamedPipeServerStream?` → `IAudioPipe?`; `DisposePipe` unchanged apart from the type.
    4. `StartAsync`: delete `string pipeName = …`; `if (hasAudio) _pipe = _pipes.Create(_sessionId, _index);` (still **before** the start info is built and the process started); `BuildStartInfo(hasAudio ? _pipe!.FfmpegInput : null)`; `process.Start()` → `_launcher.Start(process)` inside the same try/catch; delete the `try { KillOnCloseJob.Assign(process); } catch {}` block (the Windows launcher does it); `_pipe!.WaitForConnectionAsync(cts.Token)` unchanged; `_audio.BeginSegment(_pipe!)` → `_audio.BeginSegment(_pipe!.Stream)`.
    5. `BuildStartInfo`: signature and grab usage per the Decision Record: the gdigrab block (lines 191–206) becomes `foreach (string a in _grab.InputArgs(bounds, 15)) args.Add(a);`; `psi.Environment["__COMPAT_LAYER"] = …` becomes `_grab.ApplyEnvironment(psi.Environment);`; the audio input line `@"\\.\pipe\" + pipeName` becomes `audioInput`; after the `Quality.VideoArgList` loop add `foreach (string a in _grab.EncoderArgs()) args.Add(a);`.
    6. the class stays `public sealed`; `BuildStartInfo` is `internal`.
- [x] Task 7: Move `RecordingSession` (AC: 5, 6)
  - [x] `git mv src/MyScreenRecorder/Recording/RecordingSession.cs src/MyScreenRecorder.Core/Recording/RecordingSession.cs` (`RecordingState` and `SessionStats` move with it). Edit list:
    1. remove `using MyScreenRecorder.Native;`; add `using MyScreenRecorder.Platform;`.
    2. constructor `(AudioEngine audio, PlatformServices platform, FfmpegLocator ffmpeg)`: stores the audio engine, the locator, and `platform.ProcessLauncher`, `platform.AudioPipes`, `platform.Grab` in readonly fields.
    3. delete `OutputFolder`; `OutputFolder` uses (`:120`, `:359`) become `AppPaths.OutputFolder`.
    4. `FfmpegLocator.Resolve()` (`:103`) → `_ffmpeg.Resolve()`.
    5. `StartSegmentAsync` passes `_launcher, _pipes, _grab` to `new SegmentWriter(...)`.
    6. `FinalizeAsync`: `process.Start();` + the `KillOnCloseJob.Assign` try/catch → `_launcher.Start(process);` (the `using Process` and everything after it unchanged); start info unchanged (no grab environment, no stdin encoding).
- [x] Task 8: Windows implementations (AC: 1, 7)
  - [x] `src/MyScreenRecorder/Platform/Windows/JobObjectProcessLauncher.cs` (CREATE): `Start(Process process)` → `process.Start();` then `try { KillOnCloseJob.Assign(process); } catch { }` — the two statements exactly as they stood at `SegmentWriter.cs:83,96-101` and `RecordingSession.cs:388-395`. `///` docstring per D7 (native wrapper): best-effort job assignment, failure ignored, the child dies with the app.
  - [x] `src/MyScreenRecorder/Platform/Windows/WindowsNamedPipeFactory.cs` (CREATE): `Create(sessionId, index)` → `string name = $"srec_{sessionId}_{index}"; return new WindowsNamedPipe(name, new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 1 << 20));` — the constructor arguments exactly as `SegmentWriter.cs:74`.
  - [x] `src/MyScreenRecorder/Platform/Windows/WindowsNamedPipe.cs` (CREATE): `FfmpegInput` = `@"\\.\pipe\" + name`; `Stream` = the `NamedPipeServerStream`; `WaitForConnectionAsync(ct)` → `_pipe.WaitForConnectionAsync(ct)`; `Dispose()` → `_pipe.Dispose()`.
  - [x] All three: namespace `MyScreenRecorder.Platform.Windows`, `internal sealed`, `[SupportedOSPlatform("windows")]`.
- [x] Task 9: Composition root (AC: 7)
  - [x] `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATE): `_platform = new(new WasapiAudioCaptureFactory(), new MmDeviceCatalog(), new JobObjectProcessLauncher(), new WindowsNamedPipeFactory(), new GdiGrab(), new FfmpegLocatorOptions("ffmpeg.exe", typeof(MainWindow).Assembly, "ffmpeg.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyScreenRecorder")))`; field `private readonly FfmpegLocator _ffmpeg;` assigned in the constructor right after `_audio` and before `new RecordingSession(...)`: `_ffmpeg = new FfmpegLocator(_platform.Ffmpeg);`; `_session = new RecordingSession(_audio, _platform, _ffmpeg);`; `FfmpegLocator.Resolve()` (`:102`, `:360`) → `_ffmpeg.Resolve()`; `RecordingSession.OutputFolder` (`:68`, `:337`) → `AppPaths.OutputFolder`. Nothing else in `MainWindow` changes.
- [x] Task 10: Docs (AC: 7)
  - [x] `CLAUDE.md` (UPDATE): Architecture first paragraph: Core also holds `SegmentWriter`, `RecordingSession`, `FfmpegLocator`, `ScreenGrab`/`GdiGrab`/`X11Grab`, `FfmpegLocatorOptions`, the interfaces `IChildProcessLauncher`, `IAudioPipe`, `IAudioPipeFactory`, `AppPaths.OutputFolder`; "Segmented recording" and "Audio path" name the seams (pipe from `IAudioPipeFactory`, `WindowsNamedPipeFactory` in `Platform/Windows/`); "Process hygiene" says children start through `IChildProcessLauncher` and the Windows `JobObjectProcessLauncher` assigns them to `KillOnCloseJob`; the sentence on `FfmpegLocator`/the exe fallback in Commands still holds (order beside → embedded → `PATH`). `README.md`: no change (no user-facing path, command or behaviour changes); say so in the Dev Agent Record.
- [x] Task 11: Verification (AC: all)
  - [x] `dotnet build MyScreenRecorder.sln -c Debug` → 0 errors, 0 warnings (baseline after 1-3: 0 warnings).
  - [x] `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` → all pass (88 existing + new); run it three times (timing and process tests).
  - [x] `grep -rn "KillOnCloseJob\|System.IO.Pipes\|NamedPipe\|gdigrab\|__COMPAT_LAYER\|OperatingSystem\.Is\|DllImport" src/MyScreenRecorder.Core` prints only `GdiGrab.cs` lines for `gdigrab` and `__COMPAT_LAYER`.
  - [x] `git diff -M --stat HEAD` shows `SegmentWriter.cs`, `RecordingSession.cs`, `FfmpegLocator.cs` as renames; `git diff -M HEAD -- src/MyScreenRecorder.Core/Recording/SegmentWriter.cs` contains only the hunks of the Task 6 edit list (same for Tasks 5 and 7).
  - [x] Guard checks on a copy of the tree, each must fail the named test for that reason (the guard removed, the new negative test run once): see "Guard checks" in Testing Requirements.
  - [x] Comments: `git diff -U0 | grep -E '^\+\s*(#|//|--)'` and the new files: only the D7 docstrings on `IChildProcessLauncher`, `IAudioPipe`, `IAudioPipeFactory`, `JobObjectProcessLauncher`; the fake ffmpeg script's comment-free text is in Dev Notes.
  - [x] Walk the Definition of Done (`docs/planning/epics-linux-build.md` §3) item by item in the Dev Agent Record.
  - [ ] Ask the user to run the Windows smoke checklist W1–W9 on a build of this branch (`.\build.ps1`, no ffmpeg on `PATH`, none beside the exe); record the result in the Dev Agent Record.
  - [x] Every error found while implementing (analysis, test, lint, review) goes to the error log: `python3 /home/timmy/.agents/skills/learn-from-review/scripts/error_log.py add '<JSON>'` (global rule).

## Dev Notes

### Technical Requirements
- Core may reference only the BCL and `NAudio.Core` 2.2.1 (architecture §3.3). `Process`, `ProcessStartInfo`, `Rectangle` (`System.Drawing.Primitives`, already used by `CaptureTarget`) and `Assembly` are BCL. Core must not call `KillOnCloseJob`, `NamedPipeServerStream`, or any OS API after this story.
- Keep namespaces of moved files (`MyScreenRecorder.Recording`); new interfaces in `MyScreenRecorder.Platform`; `AppPaths` stays in `MyScreenRecorder`. Use `git mv`. `ImplicitUsings` disabled: explicit `using`s.
- Behaviour-preserving move: no change to timeouts (10 s connect, 15 s stop, 5 s after kill, 2 s stream drain, 25 s `Dispose` wait, 10 min concat, 3 s concat drain, 5 × 300 ms parts delete), to `Faulted` raising (`Interlocked` once per segment), to the `_ops` serialization, to `LastError` texts, to the segment file name `seg_{index:D3}.mkv`, to `ffmpeg.log` line format `[seg_NNN] <line>` and `[concat] <stderr>`, to `list.txt` (UTF-8 without BOM, `file 'seg_NNN.mkv'` per valid segment) or to the concat arguments.
- Architecture §4 member lists are the contract. A member that seems missing → stop and ask.

### ⚠️ Trap 1 — The embedded Windows ffmpeg is silently lost
`FfmpegLocator.cs:44` is `Assembly.GetExecutingAssembly()`. In Core that is the Core assembly, which has no `ffmpeg.exe` resource: the app would build, tests would pass, and every Windows user would see `ERROR: ffmpeg not found` (architecture §5.10, §7). The WPF composition root passes `typeof(MainWindow).Assembly`. Only Windows smoke W1 (empty `PATH`, no ffmpeg beside the exe) proves it; the Core test proves the code path with a test assembly.

### ⚠️ Trap 2 — Pipe before process, connect after
Today the pipe is created before the process starts (`SegmentWriter.cs:72-75`) so ffmpeg can connect at once; `BeginSegment` runs only after the connection (`:117-118`). `IAudioPipeFactory.Create` must therefore return a listening pipe, and `SegmentWriter` must call it before `_launcher.Start`. A test records the order (`FakeChildProcessLauncher` callback checks that the pipe factory was already called).

### ⚠️ Trap 3 — The pipe stream can be disposed twice
`ReleaseResources` and the `process.Start` failure path call `DisposePipe`; `AudioEngine.EndSegment` also disposes the stream. With `IAudioPipe` the stream is disposed through the pipe, so `Dispose()` can run twice on the underlying `NamedPipeServerStream` (as today). `WindowsNamedPipe.Dispose` must tolerate that (it does: `NamedPipeServerStream.Dispose` is idempotent); the fake counts calls but the production code must not depend on a count of exactly one.

### ⚠️ Trap 4 — Environment on Linux is not empty
`ProcessStartInfo.Environment` contains the whole inherited environment. Tests compare against `new ProcessStartInfo().Environment` plus the expected entry, never against an empty dictionary. The concat process must have no `__COMPAT_LAYER`.

### ⚠️ Trap 5 — Path separators in expected values
`FilePath` is built with `Path.Combine`; on Windows the separator differs. Tests build the expected last argument with `Path.Combine(partsDir, "seg_000.mkv")` (the BCL rule, not a copy of the code's output); the audio input and every other value is a literal.

### ⚠️ Trap 6 — Static state moves to instances
The old locator cache and gate were static; now they are per instance. The WPF app must create **one** locator and share it (Task 9). `SegmentWriter.LogGate` stays `static` (it serialises appends to one `ffmpeg.log` across segments; unchanged).

### ⚠️ Trap 7 — Process tests and parallelism
Behaviour tests start real child processes (`/bin/sh` scripts). Each test uses its own temp directory (`FakeFfmpeg`, parts dir, `HOME`). Tests that set `HOME`/`XDG_CONFIG_HOME` (AC6) join `ProcessEnvironmentCollection` (non-parallel). `RecordingSession` tests need `AppPaths.OutputFolder` to point into a temp dir, so they also set `HOME`/`XDG_CONFIG_HOME` and join that collection; restore both variables in `Dispose`. Always dispose segments/sessions so no `sh` child outlives a test.

### ⚠️ Trap 8 — Windows cannot run here
`JobObjectProcessLauncher`, `WindowsNamedPipeFactory`, `WindowsNamedPipe`, the changed `MainWindow` compile on Linux (`EnableWindowsTargeting`) but cannot run. Keep their bodies textually equal to the removed code so the review can diff them; behaviour is held by Windows smoke W1–W9 (AC7).

### Contract Guardrail
- Interfaces and builders: architecture §4, verbatim; `X11Grab`/`GdiGrab` argument lists: architecture §4 plus the lists in Testing Requirements.
- Expected values: AC1 lists come from running the released `BuildStartInfo` (probe, below), the regression contract (NFR1); AC2's lists are the same shape with the architecture §4 x11grab input, D8's `-x264-params sliced-threads=1`, and the `unix:` input form of §5.4 (`FfmpegInput` = `unix:<path>`); the ffmpeg `-i` display syntax `<display>+X,Y` is architecture §5.5/§6 (probed there with `:0.0`; `:0` without a screen number is ffmpeg's documented `[hostname]:display_number[.screen_number]` form and is not probed here). **Hand-off to story 2-3:** it must probe `-i :0+1920,0` with the pinned BtbN ffmpeg before relying on it; if that fails, stop and ask; AC4's separator rule is `Path.PathSeparator` (architecture §4); AC6's expectations come from the .NET `MyVideos` behaviour probed during planning (below), the user-dirs rule being the XDG one.
- Probe run during planning (reproduce it if in doubt): copy `git show b6259ee:src/MyScreenRecorder/Recording/SegmentWriter.cs` into a scratch console project that references `MyScreenRecorder.Core`, stub `MyScreenRecorder.Native.KillOnCloseJob.Assign`, construct `new SegmentWriter("C:\\ff\\ffmpeg.exe", "/tmp/parts/2026-11-03_10-00-00", 0, new CaptureTarget(TargetKind.Region, new Rectangle(-1920,116,1921,1081), "x", new[]{1}), VideoQuality.P720, AudioQuality.Aac256, null!, "0123456789abcdef0123456789abcdef")` and invoke `BuildStartInfo(hasAudio, pipeName)` by reflection; it printed the two lists in Testing Requirements, `UTF8Encoding` with preamble length 0, and exactly one added environment entry `__COMPAT_LAYER=HighDpiAware`. Also probed with `HOME=<tmp> XDG_CONFIG_HOME=<tmp>/cfg`: `MyVideos` is `""` with no `user-dirs.dirs`; with `XDG_VIDEOS_DIR="$HOME/Movies"` it is `<tmp>/Movies` when that directory exists and `""` when it does not; with `<tmp>/Videos` existing and no `user-dirs.dirs` it is `<tmp>/Videos`.
- Out of scope (do not touch): `RecorderController`, `IScreenCatalog` (1-4); Linux launcher, `UnixSocketPipeFactory`, Linux locator options, capability check, reading `DISPLAY` (2-2, 2-3, 3-1); `PreviewGrabber`, overlays, `MainWindow` logic beyond the Task 9 lines; changing `Quality` (D8: `Quality.VideoArgList` is unchanged and no x264 flags are added for Windows); the x264 tail-latency fix on Windows (backlog 5-8); startup sweep of orphan ffmpeg (backlog 5-2). Deferred from 1-1: a region touching a monitor edge can yield a zero-width target (not changed here; `SegmentWriter`'s `Math.Max(2, …)` is the only guard and is moved as is). Deferred from 1-3: unreachable zero-fill loop in `AudioEngine.Pump`; `AudioSource.OnStopped` redundancy (not touched).
- Known characteristics moved as is, without new tests beyond Testing Requirements: the swallowing `catch {}` blocks around `Exited -= OnExited`, `process.Dispose()`, stdout/stderr reader loops, `FileInfo` size reads (swallow-and-continue); they have no observable effect except through the covered paths.

### File Structure Requirements
- `src/MyScreenRecorder.Core/Platform/IChildProcessLauncher.cs`, `IAudioPipe.cs`, `IAudioPipeFactory.cs` (CREATE)
- `src/MyScreenRecorder.Core/Platform/PlatformServices.cs` (UPDATE)
- `src/MyScreenRecorder.Core/Recording/ScreenGrab.cs`, `GdiGrab.cs`, `X11Grab.cs`, `FfmpegLocatorOptions.cs` (CREATE)
- `src/MyScreenRecorder.Core/Recording/FfmpegLocator.cs` (MOVE from `src/MyScreenRecorder/Recording/FfmpegLocator.cs`, UPDATE)
- `src/MyScreenRecorder.Core/Recording/SegmentWriter.cs` (MOVE from `src/MyScreenRecorder/Recording/SegmentWriter.cs`, UPDATE)
- `src/MyScreenRecorder.Core/Recording/RecordingSession.cs` (MOVE from `src/MyScreenRecorder/Recording/RecordingSession.cs`, UPDATE)
- `src/MyScreenRecorder.Core/AppPaths.cs` (UPDATE)
- `src/MyScreenRecorder/Platform/Windows/JobObjectProcessLauncher.cs`, `WindowsNamedPipeFactory.cs`, `WindowsNamedPipe.cs` (CREATE)
- `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATE)
- `tests/MyScreenRecorder.Core.Tests/ScreenGrabTests.cs`, `SegmentWriterStartInfoTests.cs`, `SegmentWriterTests.cs`, `RecordingSessionTests.cs`, `FfmpegLocatorTests.cs`, `FakeChildProcessLauncher.cs`, `FakeAudioPipe.cs`, `FakeAudioPipeFactory.cs`, `FakeFfmpeg.cs`, `TestPlatform.cs`, `Resources/fake-ffmpeg.bin`, `Resources/fake-suffixed.bin` (CREATE)
- `tests/MyScreenRecorder.Core.Tests/AppPathsTests.cs`, `MyScreenRecorder.Core.Tests.csproj` (UPDATE)
- `CLAUDE.md` (UPDATE)
- `src/MyScreenRecorder/Native/Win32.cs` is **not** changed.

### Testing Requirements

Windows, with audio (`SegmentWriter.BuildStartInfo`, AC1) — one list element per line:
```
-hide_banner
-loglevel
error
-y
-f
f32le
-ar
48000
-ac
2
-thread_queue_size
1024
-i
\\.\pipe\srec_0123456789abcdef0123456789abcdef_0
-f
gdigrab
-framerate
15
-draw_mouse
1
-offset_x
-1920
-offset_y
116
-video_size
1920x1080
-thread_queue_size
1024
-i
desktop
-map
1:v
-map
0:a
-vf
scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p
-c:v
libx264
-preset
veryfast
-crf
28
-g
30
-c:a
aac
-b:a
256k
-progress
pipe:1
-nostats
<Path.Combine(partsDir, "seg_000.mkv")>
```

Windows, without audio (`BuildStartInfo(null)`):
```
-hide_banner
-loglevel
error
-y
-f
gdigrab
-framerate
15
-draw_mouse
1
-offset_x
-1920
-offset_y
116
-video_size
1920x1080
-thread_queue_size
1024
-i
desktop
-map
0:v
-vf
scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p
-c:v
libx264
-preset
veryfast
-crf
28
-g
30
-progress
pipe:1
-nostats
<Path.Combine(partsDir, "seg_000.mkv")>
```

X11, with audio (AC2; bounds `(1920,0,2560,1440)`, `X11Grab(":0")`, audio input `unix:/run/user/1000/srec_0123456789abcdef0123456789abcdef_0`):
```
-hide_banner
-loglevel
error
-y
-f
f32le
-ar
48000
-ac
2
-thread_queue_size
1024
-i
unix:/run/user/1000/srec_0123456789abcdef0123456789abcdef_0
-f
x11grab
-framerate
15
-draw_mouse
1
-video_size
2560x1440
-thread_queue_size
1024
-i
:0+1920,0
-map
1:v
-map
0:a
-vf
scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p
-c:v
libx264
-preset
veryfast
-crf
28
-g
30
-x264-params
sliced-threads=1
-c:a
aac
-b:a
256k
-progress
pipe:1
-nostats
<Path.Combine(partsDir, "seg_000.mkv")>
```
Also assert X11 without audio: same as above minus the audio input block (`-f f32le … -i unix:…`), `-map 0:v` only, no `-map 0:a`, no `-c:a`/`-b:a`, `-x264-params sliced-threads=1` still present.

Concat start info (`RecordingSession.FinalizeAsync`, assert from `FakeChildProcessLauncher.StartInfos[last]`): `FileName` = the located ffmpeg path, `WorkingDirectory` = parts dir, `UseShellExecute == false`, `CreateNoWindow == true`, stdin/stdout/stderr redirected, `StandardInputEncoding == null`, `Environment` equals the baseline (no `__COMPAT_LAYER`), `ArgumentList` = `-hide_banner -loglevel error -y -f concat -safe 0 -i list.txt -c copy -movflags +faststart REC_<stamp>.mp4`-path for mp4, and the same without `-movflags +faststart` for `.mkv` (WAV with audio). The last argument is `Path.Combine(AppPaths.OutputFolder, "REC_<stamp>.<ext>")`.

Locator tests (AC3, AC4; `FfmpegLocatorTests`): table of scenario → expected.
| Scenario | Expected |
|---|---|
| base dir has `ffmpeg.exe`, resource and `PATH` entry also exist | returns `<base>/ffmpeg.exe` (beside wins) |
| nothing beside, resource present, `PATH` has `ffmpeg.exe` | returns the extracted `<extract>/ffmpeg_<len>.exe` (embedded wins over `PATH`) |
| nothing beside, `ResourceAssembly = null`, `PATH` = `<emptyDir><sep><dirWithExe>` | returns `<dirWithExe>/<ExecutableName>` (AC4) |
| `PATH` entries `"  <dir>  "`, `"\"<dir>\""` (surrounding quotes), empty entries between separators | the dir with the exe is found |
| `PATH` is `null` / `""` / only separators | `null` |
| `ResourceName = "fake.exe"` (assembly has `Vendor.Tools.fake.exe`) | suffix match extracts it; `ResourceName = "FFMPEG.EXE"` matches `ffmpeg.exe` case-insensitively; `ResourceName = "nothere.exe"` → falls through to `PATH` / `null` |
| `ResourceName = null` with an assembly given | embedded skipped, no exception |
| extraction target exists with the same length | returned without rewriting (compare `LastWriteTimeUtc` before/after); different length → replaced with the resource bytes; no `*.tmp` remains |
| `ExtractDirectory` cannot be created (a file stands in its place) | `Resolve()` returns the `PATH` result or `null`, does not throw |
| second `Resolve()` after `PATH` changed | returns the cached path; after the cached file is deleted it resolves again |
The `PATH` value in each test is built with `string.Join(Path.PathSeparator, …)`; the fake `getEnvironmentVariable` returns the value only for the key `"PATH"` and `null` otherwise (so a wrong variable name fails).

Behaviour tests (AC5). Common setup: `FakeFfmpeg` (script in Dev Notes) located through an `FfmpegLocator` built with the internal constructor (`baseDirectory` = the fake's directory, `ExecutableName = "ffmpeg"`, `ResourceAssembly = null`, empty `PATH`), `GdiGrab` as the grab (the test checks the seam path, not Linux args), `FakeChildProcessLauncher` (real `process.Start()`), `FakeAudioPipeFactory`. `SegmentWriterTests`:
| # | Scenario | Expected |
|---|---|---|
| S1 | Start without audio, then `StopAsync` | `partsDir` created; no pipe created; launcher called once with the start info of AC1 (without audio); `FilePath` exists with 3 bytes; `FinalBytes == 3` and `CurrentBytes == 3`; no `Faulted` |
| S2 | While running, poll `CurrentBytes` | becomes `1234` (the script prints `total_size=1234`) before stop |
| S3 | Start with audio (fake pipe connects at once), wait 300 ms, `StopAsync` | pipe factory called once with `(sessionId, 0)` **before** the launcher's `Start` (callback assertion); `ArgumentList` contains the pipe's `FfmpegInput` after `-i`; after stop, `WrittenBytes` (never `Length` on the disposed stream) is non-empty, its length a multiple of 8, all zero because no source delivers data (`BeginSegment(pipe.Stream)` ran); `StreamClosed` is true (`EndSegment` ran); pipe `Disposed >= 1` |
| S4 | `StopAsync` called twice (concurrently) | both await the same task; launcher called once; no exception |
| S5 | `stderr` line from ffmpeg | `partsDir/ffmpeg.log` contains the line `[seg_000] segment warning` (index 12 → `[seg_012]`) |
| S6 | Launcher throws `InvalidOperationException("boom")` on `Start` (with audio) | `StartAsync` rethrows the same instance; pipe `Disposed >= 1`; no `Faulted`; no process left |
| S7 | Mode `exit-early`, audio, pipe never connects | `StartAsync` throws `InvalidOperationException` with message `ffmpeg exited before the audio pipe was connected. See ffmpeg.log in <partsDir>`; pipe disposed; no `Faulted` |
| S8 | Pipe connection faults with `IOException("pipe broken")` | `InvalidOperationException` with message `Could not connect the audio pipe to ffmpeg. See ffmpeg.log in <partsDir>` and `InnerException` the `IOException`; the ffmpeg process (pid recorded by the launcher) no longer exists within 3 s; pipe disposed |
| S9 | Pipe never connects and ffmpeg keeps running (slow, 10 s) | the same wrapped message after about 10 s; `InnerException` is an `OperationCanceledException`; process gone |
| S10 | Mode `exit-early`, no audio | exactly one `Faulted("ERROR: see ffmpeg.log")` within 5 s, whether the exit is noticed during `StartAsync` or after |
| S11 | Mode `die-soon`, no audio, writer started and not stopped | exactly one `Faulted` (the `OnExited` path); then `StopAsync` raises no second one |
| S12 | Mode `ignore-q`, `StopAsync` (slow, ~15 s) | completes; the process (pid) no longer exists (kill fallback) |
| S13 | `StopAsync` on a writer that was never started | completes without exception; `FinalBytes == 0` |
| S14 | Mode `empty-output` | after stop `FinalBytes == 0` |

`RecordingSessionTests` (the session over `AudioEngine`; a mic is set through `FakeAudioCaptureFactory` for the audio cases; `HOME`/`XDG_CONFIG_HOME` point at a temp dir, so `AppPaths.OutputFolder` is `<HOME>/Videos/MyScreenRecorder`):
| # | Scenario | Expected |
|---|---|---|
| R1 | `StartAsync` when the locator finds nothing | throws `InvalidOperationException("ffmpeg not found")`; state `Idle`; no parts dir |
| R2 | `StartAsync` twice | second throws `InvalidOperationException("A recording is already in progress")` |
| R3 | Start → Stop (no audio) | states `Recording`, `Finalizing`, `Idle` in `StateChanged` order; returns `<OutputFolder>/REC_<stamp>.mp4` where `<stamp>` matches `^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$`; the file exists and contains `seg` (concat copied the segment); `.parts/<stamp>` removed; `Stats.QualityLabel` is `Quality.Label(vq)` (no audio) |
| R4 | With audio: `Stats.QualityLabel` | `720P / AAC 256K` for `P720` + `Aac256` |
| R5 | Start → Pause → Resume → Pause → Resume → Stop | three segments `seg_000.mkv` … `seg_002.mkv`; states `Recording, Paused, Recording, Paused, Recording, Finalizing, Idle`; the final file content is `seg` × 3; the concat start info lists exactly the three files (check `list.txt` in a variant where concat fails, see R9) |
| R6 | `PauseAsync` when `Idle`, `ResumeAsync` when `Recording`, `StopAsync`/`CancelAsync` when `Idle` | no state change, no exception, `StopAsync` returns `null` |
| R7 | Audio + `Wav` | final extension `.mkv`, concat arguments without `-movflags`; no audio + `Wav` → `.mp4` with `+faststart` |
| R8 | Cancel while Recording and while Paused | state `Idle`; `.parts/<stamp>` removed; no `REC_*` file; no concat launched (launcher count = segments only) |
| R9 | Mode `concat-fail` then Stop | returns `null`; `LastError == "Could not assemble the final file. Parts kept in <partsDir>"`; `ffmpeg.log` contains `[concat] concat boom`; `list.txt` text equals `file 'seg_000.mkv'` + newline (and `seg_001` for two segments); parts folder kept; state `Idle` |
| R10 | Mode `empty-output` then Stop | returns `null`; `LastError == "No video data was recorded. Parts folder: <partsDir>"`; no concat launched |
| R11 | Launcher throws on the concat call (call number = segments + 1) | `StopAsync` returns `null`; `LastError == "Finalize failed: boom Parts kept in " + partsDir` (launcher exception message `boom`); state `Idle` |
| R12 | `StartAsync` where the first segment fails to start (launcher throws) | the exception propagates; `.parts/<stamp>` removed; state `Idle` |
| R13 | `ResumeAsync` where the new segment fails to start | exception propagates; `LastError` equals the exception message; state stays `Paused` |
| R14 | ffmpeg dies during Recording (mode `die-soon`) | `Faulted` event with `ERROR: see ffmpeg.log` once; `LastError == "ERROR: see ffmpeg.log"`; state `Idle` afterwards |
| R15 | `Dispose` while Recording | the segment is stopped (process gone); a second `Dispose` is a no-op |
| R16 | `CancelAsync` when the parts folder cannot be deleted (parent made read-only, skipped when running as root) | after about 1.5 s `LastError == "Could not delete <partsDir>"`; state `Idle` |

`AppPathsTests` additions (AC6): the four cases of AC6, Linux-only (`Assert.SkipUnless(OperatingSystem.IsLinux(), …)` as the existing tests), each restoring `HOME`/`XDG_CONFIG_HOME`.

Guard checks (run each once on a copy; each must fail the named test, for that reason; record actual messages in the Dev Agent Record):
(a) `_grab.EncoderArgs()` loop removed → X11 with/without audio tests; (b) `_grab.ApplyEnvironment` removed → AC1 environment test; (c) `& ~1` removed → odd-bounds test; `Math.Max(2, …)` removed → 1×1 test; (d) `Path.PathSeparator` replaced by `';'` → AC4 PATH test; (e) beside-check removed → "beside wins"; cache re-check `File.Exists(_cached)` removed → "cached file deleted"; `Trim('"')` removed → quoted-entry test; `ResourceAssembly` null check removed → `ResourceName = null` test; (f) pipe creation moved after `_launcher.Start` → S3 ordering assertion; (g) `DisposePipe()` removed from the start-failure catch → S6; (h) `ReleaseResources()` removed from the connect-failure catch → S7/S8 (pipe disposed); (i) `process.Kill(true)` removed from the connect-failure catch → S8 (pid still alive); (j) `_stopping = true` removed in `StopCoreAsync` → S1/S12 (a `Faulted` appears on normal stop); (k) `Interlocked` once-only in `RaiseFaulted` removed → S10/S11 (two events); (l) the `q` write removed → S1: `StopAsync` takes the 15 s kill path, so S1 asserts it completes in under 10 s; (m) kill fallback removed → S12; (n) `_launcher.Start(process)` omitted for the concat process → R3 (launcher count) ; (o) `File.WriteAllLines` content changed (BOM) → R9 `list.txt` text; (p) `LastError` texts changed → R9–R13; (q) `Directory.Delete(_partsDir…)` after success removed → R3; in `CancelAsync` removed → R8; (r) `Quality.Extension` call replaced by `"mp4"` → R7; (s) `AppPaths.OutputFolder` fallback removed → AC6 Movies-missing case; the swallow try/catch removed → AC6 file-in-place case; (t) `_exited.Task.IsCompleted && !_stopping` check at the end of `StartAsync` removed → S10 only if the exit is noticed there (if no input reaches it, build one with a script mode that exits before the first read; if still none, stop and ask before deleting the guard).
A guard that no test reaches is either dead or untested: build an input that reaches it before deleting it; stop and ask if none exists.

### Dev Notes: fake `ffmpeg` script (exact text; tested during planning with `dash` and GNU sed)
```sh
#!/bin/sh
dir=$(dirname "$0")
mode=$(cat "$dir/mode" 2>/dev/null)
for last; do :; done
if [ "$mode" = exit-early ]; then exit 1; fi
case " $* " in
  *" concat "*)
    if [ "$mode" = concat-fail ]; then echo "concat boom" >&2; exit 1; fi
    sed -n "s/^file '\(.*\)'\$/\1/p" list.txt | while read -r f; do cat "$f"; done > "$last"
    exit 0 ;;
esac
echo "segment warning" >&2
if [ "$mode" = empty-output ]; then : > "$last"; else printf 'seg' > "$last"; fi
echo total_size=1234
if [ "$mode" = die-soon ]; then sleep 0.3; exit 1; fi
while read -r line; do
  if [ "$line" = q ] && [ "$mode" != ignore-q ]; then break; fi
done
echo progress=end
exit 0
```
Planning probe results: concat of two segments with contents `a`, `b` gives `ab`; segment mode with `q` on stdin prints `segment warning` (stderr), `total_size=1234`, `progress=end`, exit 0, output `seg`; `concat-fail` prints `concat boom`, exit 1; `exit-early` exits 1; `/bin/sh` is `dash` on the dev machine. `mode` is read per invocation from the file beside the script, so a test can change it between segments. The script runs with the working directory set by the start info (the parts dir), which is where `list.txt` is. `die-soon` (R14, S11) makes a segment ffmpeg exit with code 1 about 0.3 s after it started.

### Previous Story Intelligence (1-1, 1-3, done)
- Test-first worked: write the test, see it fail to compile (type not in Core), then move.
- Review found untested branches every time (even size, swallowed save errors, odd channel counts, pump clamp/NaN/backlog, `_peak` reset): cover every moved branch that has an observable effect (the S/R tables and guard list above).
- Guard checks were run on a copy with `--no-incremental` and the full test run per mutation; each had to fail the named test. Do the same; a guard whose removal changes nothing is recorded as deferred, not deleted (1-3 `Pump` zero-fill, `OnStopped`).
- Moved code stays textually equal where possible so the review can diff it (1-3 Trap 6): `git diff -M` must show only the listed hunks.
- Reviewer subagents hit an API rate limit in 1-3; the dev agent then ran the lenses itself. If that happens again, say so in Review Findings.
- `dotnet test` needs the repo-root `global.json` (MTP); `TestResults/` is ignored; tests that change the process environment belong in `ProcessEnvironmentCollection`.
- `Assert.SkipUnless` is used in `AppPathsTests`; `Assert.SkipWhen` exists in `xunit.v3.assert` 4.0.2 (checked in `~/.nuget/packages/xunit.v3.assert/4.0.2/lib/netstandard2.0/xunit.v3.assert.dll`).
- Windows ownership/dispose lessons (1-3 Trap 3): no path may leak or double-dispose a resource; here the pipe (`DisposePipe` idempotent) and the `Process` (`ReleaseResources` once).
- Deferred from 1-1/1-3: see Contract Guardrail (not fixed here).
- Windows smoke is the final gate (user ran W1–W9 for 1-1 and 1-3).

### Git Intelligence Summary
- `75765fa` merge PR #2 (1-3) · `cd0adf6` audio capture seam (created `PlatformServices`, `Platform/Windows/`) · `60a23cc` merge PR #1 · `d88a178` test gaps · `0077a81` Core split (created `AppPaths`, moved `Quality`) · `b6259ee` docs: CLAUDE.md (the reference commit for byte-identical args) · `2114ef4` reduced file size (today's x264 args) · `8763bc0` initial recorder (segment pipeline).

### Latest Stack Information
- .NET SDK 10.0.403 (user install in `~/.dotnet`), `net10.0` (Core, tests) / `net10.0-windows` (WPF).
- `NAudio.Core` 2.2.1 (Core), `NAudio` 2.2.1 (WPF). Do not bump.
- `xunit.v3` 4.0.2 on Microsoft Testing Platform.
- ffmpeg is not needed to run the unit tests (the script fakes it); real ffmpeg arguments are held by the golden lists and Windows smoke.

### References
- [Source: `docs/planning/linux-build-architecture.md` - §2 D3, D4, D6, D7, D8, D9, D10; §3 Boundaries; §4 Seams; §5.1, §5.4, §5.5, §5.10, §5.11; §6 Verified facts; §7 Risks]
- [Source: `docs/planning/epics-linux-build.md` - §2 Working rules; §3 DoD; §4 Windows smoke; §6 story 1-2; §7 Sequencing]
- [Source: `CLAUDE.md` - Architecture (Segmented recording, Audio path, Process hygiene), Conventions]
- [Source: `src/MyScreenRecorder/Recording/SegmentWriter.cs`, `RecordingSession.cs`, `FfmpegLocator.cs`, `src/MyScreenRecorder/Native/Win32.cs` (`KillOnCloseJob`) at `b6259ee` (identical at `75765fa`)]
- [Source: `src/MyScreenRecorder/MainWindow.xaml.cs` at `75765fa` - lines 26, 52–56, 68, 102, 337, 360]
- [Source: `src/MyScreenRecorder.Core/Platform/PlatformServices.cs`, `AppPaths.cs`, `Recording/Quality.cs` at `75765fa`]
- [Source: `docs/implementation/1-1-core-and-test-projects.md` - Review Findings; `docs/implementation/1-3-audio-capture-seam.md` - Decision Record, Traps, Review Findings]

## Dev Agent Record

### Agent Model Used

Claude Sonnet 5.5 (dev agent); reviewer lenses: sonnet, medium effort.

### Debug Log References

- Baseline: `75765fa`.
- `dotnet build MyScreenRecorder.sln -c Debug`: 0 Warning(s), 0 Error(s).
- `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"`, three runs: total 168, succeeded 168, failed 0, skipped 0 (88 existing + 80 new).
- Core purity grep: only `GdiGrab.cs` lines for `gdigrab` and `__COMPAT_LAYER`.
- Guard checks on a copy of the tree (script in the session scratchpad), mutation -> failing tests: (a) EncoderArgs loop removed -> X11 with/without audio; (b) ApplyEnvironment removed -> Windows_FlagsAndEnvironment, S1; (c) `& ~1` removed -> OddBounds, both golden Windows lists; `Math.Max` removed (width, height) -> TinyBounds; (d) `';'` separator -> Path tests; (e) beside check removed -> BesideExecutable_Wins and all session tests; cache re-check removed -> Resolve_Caches...; `Trim('"')` removed -> Path_EntriesAreTrimmed...; (f) pipe created after start -> S3 ordering and others; (g) DisposePipe in start-failure catch -> S6; (h) ReleaseResources in connect-failure catch -> S7, S8; (i) Kill in connect-failure catch -> S8 only after adding an elapsed-time assertion (< 2.5 s; before that no test failed because disposing the Process closes stdin and the fake exits); (j) `_stopping` in StopCoreAsync -> S1, R5, R8, R9; (k) Interlocked once-only -> only after adding S10_ExitedHandlerFiringAgain... (reflection call of `OnExited`; before no test failed, the race is not reachable by timing); (l) `q` write -> S1 (under 10 s); (m) kill fallback -> S12; (n) concat via launcher -> R3, R5, R7, R11; (o) BOM -> R9, R3, R5, R7; (p) LastError texts -> R9, R10, R11, R16, R13; (q) parts delete on success -> R3, R5; in Cancel -> R8, R16; in start failure -> R12; (r) Extension -> R7 (audio Wav); (s) OutputFolder fallback -> AC6 Movies-missing and no-user-dirs and many session tests; swallow removed -> AC6 file-in-place; (t) end-of-StartAsync exit check -> only after adding S10_ExitBetweenPipeConnectAndActivation (fake pipe `Stream` getter delays so the exit happens between connect and `_active = true`); (u) invariant culture removed from `GdiGrab` -> Grabs_FormatNumbersWithInvariantCulture (the test first used `de-DE`, which cannot fail; changed to `sv-SE`, whose negative sign differs).
- No test fails (recorded as deferred, guard kept): `ExtractEmbedded` null checks for `ResourceAssembly` / `ResourceName` (e4, e5): a removed check yields a NullReferenceException or an unmatched name, both swallowed by the surrounding `catch` and returning `null`, the same result. Not observable.
- Windows smoke W1-W9: pending (user).

### Completion Notes List

- Seams, grabs, locator options, moved pipeline, `AppPaths.OutputFolder`, Windows implementations, composition root and `CLAUDE.md` done per the Decision Record. `README.md`: no change (no user-facing path, command or behaviour changes).
- Deviation: `BuildStartInfo` starts with `bool hasAudio = audioInput != null;` (a local, not in the edit list). The behaviour tests skip on Windows in the test class constructors (`Assert.SkipWhen`) instead of per test. `AppPathsTests` also clears `XDG_VIDEOS_DIR` (an inherited value would win over the file).
- Extra tests beyond the story tables: S10 (two variants), S3 session id/index, locator `Resolve_ReadsOnlyThePathVariable`.
- DoD (epics doc section 3): 1 every AC passes except AC7 (Windows smoke pending); 2 build 0 warnings 0 errors; 3 tests pass (3 runs); 4 guard checks done, see log (two guards unobservable, deferred); 5 comments: only the D7 docstrings on the three interfaces and `JobObjectProcessLauncher`; 6 `CLAUDE.md` updated, `README.md` not applicable; 7 Windows smoke pending (user); 8 Linux smoke not applicable (no Linux story); 9 nothing committed or pushed.

### Review Findings

Lenses run as sonnet/medium subagents: adversarial, verification gap, acceptance.
- [x] [Review][Patch] Behaviour tests lacked the Windows skip the story names -- `SegmentWriterTests.cs`, `RecordingSessionTests.cs` -- fixed (SkipWhen in constructors).
- [x] [Review][Patch] Invariant-culture test could not fail (`de-DE`) -- `ScreenGrabTests.cs` -- fixed (`sv-SE`, mutation u fails it).
- [x] [Review][Patch] `AppPathsTests` did not clear an inherited `XDG_VIDEOS_DIR` -- fixed.
- [x] [Review][Patch] `CLAUDE.md` said `ScreenGrab`/`FfmpegLocatorOptions` are in `MyScreenRecorder.Platform` -- fixed.
- [x] [Review][Dismiss] "OutputFolder has no existence check, Movies-missing test fails" -- false: the .NET `MyVideos` probe (story Contract Guardrail) returns `""` when the directory is missing and the test passes.
- [x] [Review][Defer] Extracted embedded ffmpeg has no execute bit on Linux (`FfmpegLocator.cs`) -- moved as is; Linux has no resource assembly; revisit in epic 2.
- [x] [Review][Defer] `BuildStartInfo` throwing after `_pipes.Create` leaks the pipe (`SegmentWriter.StartAsync`) -- order fixed by the story; only a throwing custom `ScreenGrab` triggers it.
- [x] [Review][Defer] A launcher that starts the process and then throws leaves it running (`SegmentWriter` catch) -- `JobObjectProcessLauncher` swallows; latent for other launchers (stories 2-x).
- [x] [Review][Defer] Verification gaps without a test: `Stats.Bytes`/`Duration`, partial empty/non-empty segments in `list.txt`, `OnSegmentFaulted` guards, `_ops` concurrency, concat 10 min timeout, Windows classes (smoke only), public locator constructor.
- [x] [Review][Dismiss] Lens statements that tests were not run / SDK missing -- they could not run `dotnet`; tests were run by the dev agent.

- [QA] 2026-11-03: PASS WITH CONCERNS (no blocker/major). QA re-ran build (0 warnings) and tests 3× (168/168), re-derived AC1 lists from `b6259ee`, confirmed guards a, c, i, k, t on a copy. Minor: guard i is held only by S8's elapsed-time assertion (fake ffmpeg exits on stdin close); k and t by contrived tests (reflection / delayed `Stream` getter); deferred gaps accepted. Hand-offs written into the epic: 2-2 launcher contract (throw only if not started), 2-3 keeps `ResourceAssembly = null` (no exec bit on extraction).
- [Full review] 2026-11-03: two independent reviewers (service-review, code-review; sonnet/high) plus verification by the orchestrator. No Critical/Important. Fixed (each new test fails with its guard removed, checked on a copy):
  - [x] [Review][Patch] `RecordingSession` grab wiring untested -- `R17_SegmentsUseThePlatformGrab`.
  - [x] [Review][Patch] Pipe leaked when `BuildStartInfo` throws -- try/catch with `DisposePipe()` in `SegmentWriter.StartAsync`; `S15_GrabThrows_DisposesThePipeAndStartsNoProcess`.
  - [x] [Review][Patch] Launcher failure contract missing at the interface -- docstring + architecture §4 (Decision Record).
  - [x] [Review][Patch] Extracted-file extension from `ResourceName` untested -- resources `tool.bin`, `noext`; `ResourceName_ExtensionOfTheExtractedFileComesFromTheResourceName`.
  - [x] [Review][Patch] Dot boundary of the suffix match untested -- `ResourceName_SuffixMatchNeedsADotBoundary`.
  - [x] [Review][Patch] Temp dirs created before the Windows skip leak on Windows -- created in the constructor body after the skip.
  - [x] [Review][Patch] Stale paths and `RecordingSession.OutputFolder` in the architecture doc; amendment line now names the two decisions of 1.2.
  - [x] [Review][Patch] Redundant `using MyScreenRecorder.Platform;` in the three Windows files; tracker column alignment; empty leftover `src/MyScreenRecorder/Recording/` removed.
  - [Review][Dismiss] Smell bundle (grab arg duplication, 11-parameter constructor, `PlatformServices.Ffmpeg` plus a separate locator, Platform/Recording namespace cycle) -- documented choices (architecture §4, Decision Record). 1-3 bookkeeping in this change -- kept by the user's decision. Negative X11 offsets -- not reachable on X11. CI without `dotnet test` -- pre-existing, story 4-2.
  - Result: build 0 warnings/0 errors; tests 172/172 three times.

### File List

Pre-existing (bookkeeping, untouched except tracker/status edits): `docs/implementation/1-3-audio-capture-seam.md`, `docs/implementation/sprint-status.yaml` (also UPDATED: 1-2 -> review), `docs/planning/epics-linux-build.md`, `docs/planning/linux-build-architecture.md`.
- `src/MyScreenRecorder.Core/Platform/IChildProcessLauncher.cs`, `IAudioPipe.cs`, `IAudioPipeFactory.cs` (CREATED)
- `src/MyScreenRecorder.Core/Platform/PlatformServices.cs` (UPDATED: six members)
- `src/MyScreenRecorder.Core/Recording/ScreenGrab.cs`, `GdiGrab.cs`, `X11Grab.cs`, `FfmpegLocatorOptions.cs` (CREATED)
- `src/MyScreenRecorder.Core/Recording/FfmpegLocator.cs`, `SegmentWriter.cs`, `RecordingSession.cs` (MOVED from `src/MyScreenRecorder/Recording/`, UPDATED)
- `src/MyScreenRecorder.Core/AppPaths.cs` (UPDATED: `OutputFolder`)
- `src/MyScreenRecorder/Platform/Windows/JobObjectProcessLauncher.cs`, `WindowsNamedPipeFactory.cs`, `WindowsNamedPipe.cs` (CREATED)
- `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATED: composition root)
- `CLAUDE.md` (UPDATED: architecture)
- `tests/MyScreenRecorder.Core.Tests/`: `ScreenGrabTests.cs`, `SegmentWriterStartInfoTests.cs`, `SegmentWriterTests.cs`, `RecordingSessionTests.cs`, `FfmpegLocatorTests.cs`, `FakeChildProcessLauncher.cs`, `FakeAudioPipe.cs`, `FakeAudioPipeFactory.cs`, `FakeFfmpeg.cs`, `TestPlatform.cs`, `Resources/fake-ffmpeg.bin`, `Resources/fake-suffixed.bin`, `Resources/fake-tool.bin`, `Resources/fake-noext.bin` (CREATED); `AppPathsTests.cs`, `MyScreenRecorder.Core.Tests.csproj` (UPDATED)

## Change Log

- 2026-11-03: Story created (ready-for-dev). Acceptance criteria 1–4 from the epic, 5–6 added, epic AC5 is AC7 (Decision Record). Windows arg lists produced by running the released `BuildStartInfo` at `b6259ee` in a probe.
- 2026-11-03: Implemented by dev agent; status review. Windows smoke pending (user).
- 2026-11-03: QA verification: PASS WITH CONCERNS (minor only); hand-offs to 2-2/2-3 recorded in the epic. Windows smoke pending (user).
- 2026-11-03: Full review (two reviewers + verification): 8 minor findings fixed, 4 new tests (172/172), launcher failure contract written into the interface and architecture §4. Windows smoke pending (user).
