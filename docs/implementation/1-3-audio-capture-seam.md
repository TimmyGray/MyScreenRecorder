# Story 1.3: Audio capture seam

Status: review

## Story

As the maintainer of MyScreenRecorder,
I want audio mixing (`AudioEngine`, `AudioSource`) in `MyScreenRecorder.Core` behind platform-neutral capture and device-catalog interfaces,
so that the Linux build can reuse the wall-clock audio pump with PulseAudio capture, and story 1.2 can move the recording pipeline (which calls `AudioEngine`) into Core.

<!-- Track: Core — Covers: FR2, FR6, NFR1, NFR4 — Decisions: D3, D4, D7, D9 — Windows smoke: required -->

## Decision Record

- 2026-11-02: Story 1.3 is done **before** 1.2 (user decision). Reason: `SegmentWriter` calls `AudioEngine` (`SegmentWriter.cs:25,69,118,313`), which depends on WASAPI through `AudioSource` until this story; Core cannot reference the WPF project. Rejected: a new narrow audio interface in Core for 1.2 (seam not in architecture §4); splitting 1.2.
- 2026-11-02: This story introduces `PlatformServices` (Core, namespace `MyScreenRecorder.Platform`) as `public sealed record PlatformServices(IAudioCaptureFactory AudioCaptures, IAudioDeviceCatalog AudioDevices);`. Story 1.2 adds its members (launcher, pipes, grab, ffmpeg options) later. Reason: architecture §3.3 (each app builds a `PlatformServices` and passes it into Core); the order of growth follows the swapped sequence.
- 2026-11-02: `AudioEngine` takes `IAudioCaptureFactory` in its constructor (not the whole `PlatformServices`). Reason: the narrowest dependency; tests pass a fake factory.
- 2026-11-02: `AudioSource` takes ownership of the `IAudioCapture` passed to it: it disposes the capture in `Dispose()`, and also when its own constructor throws. Reason: today `AudioSource` owns `MMDevice` + `WasapiCapture` and disposes them on constructor failure (`AudioSource.cs:57-62`) and in `Dispose()`.
- 2026-11-02: Windows implementations go to `src/MyScreenRecorder/Platform/Windows/` in namespace `MyScreenRecorder.Platform.Windows` (folder = namespace, architecture §3.2), marked `[SupportedOSPlatform("windows")]` (architecture §3.3).

## Acceptance Criteria

1. **Peaks**
   **Given** an `AudioSource` over a fake `IAudioCapture` whose `Format` is float32, 16-bit PCM, 24-bit PCM, 32-bit PCM, or `WaveFormatExtensible(48000, 32, 2)` (the WASAPI shared-mode format)
   **When** the fake raises `DataAvailable` with a buffer whose largest absolute sample is 0.75 (values in Testing Requirements)
   **Then** `Peak` equals `0.75` (±1e-4)
   **And** `Peak` is `0` when no data has arrived yet, more than 300 ms after the last data (checked at 450 ms), and right after the fake raises `Stopped`
   **And** a float32 sample of `1.5` gives `Peak == 1`; an 8-bit PCM format gives `Peak == 0`.

2. **Armed buffering**
   **Given** an `AudioSource` over a fake float32/48 kHz/stereo capture
   **When** 9 600 bytes arrive while `Armed` is `false`
   **Then** `BufferedMilliseconds` is `0`
   **When** `Armed` is `true` and the same 9 600 bytes arrive
   **Then** `BufferedMilliseconds` is `25` (±0.001; 48 000 × 8 bytes/s).

3. **Wall-clock pump**
   **Given** an `AudioEngine` whose mic is a fake capture that never raises `DataAvailable`
   **When** `BeginSegment(stream)` runs, ~1 s passes, then `EndSegment()` runs
   **Then** the stream received `elapsed × 48 000 × 8` bytes (±10 %, elapsed = test stopwatch around Begin…End), a multiple of 8, all zero.

4. **Windows behaviour unchanged**
   **Given** `.\build.ps1` on Windows
   **When** the user runs the Windows smoke checklist W1–W9 (`docs/planning/epics-linux-build.md` §4)
   **Then** every item passes; W2 (both meters move) and W3 (voice + system sound audible, both claps) exercise this story. W3's clap check is informational (D8).

## Tasks / Subtasks

- [x] Task 1: Branch and prerequisites (AC: all)
  - [x] `dotnet --version` → `10.0.x` (the SDK is user-installed at `~/.dotnet`; if `dotnet` is not on `PATH`, use `~/.dotnet/dotnet`). If missing, stop and ask.
  - [x] Check that `main` contains PR #1 (`git log origin/main --oneline | grep 60a23cc`).
  - [x] `git fetch origin && git switch -c feat/linux-1-3-audio-capture-seam --no-track origin/main`; `git config branch.feat/linux-1-3-audio-capture-seam.merge` prints nothing.
- [x] Task 2: Platform interfaces in Core (AC: 1–3)
  - [x] `src/MyScreenRecorder.Core/Platform/IAudioCapture.cs` (CREATE), namespace `MyScreenRecorder.Platform`, members exactly as architecture §4: `WaveFormat Format { get; }`, `event Action<byte[], int>? DataAvailable;` (buffer, valid byte count; raised on a capture thread; the buffer may be reused after the handler returns), `event Action? Stopped;`, `void Start();`, `void Stop();`, `: IDisposable`.
  - [x] `src/MyScreenRecorder.Core/Platform/IAudioCaptureFactory.cs` (CREATE): `IAudioCapture Open(string deviceId, bool loopback);` — throws if `deviceId` is not a present device; never falls back to another device.
  - [x] `src/MyScreenRecorder.Core/Platform/IAudioDeviceCatalog.cs` (CREATE): `IReadOnlyList<AudioDeviceInfo> Inputs(); IReadOnlyList<AudioDeviceInfo> Outputs(); string? DefaultInputId(); string? DefaultOutputId();` — lists are empty and defaults `null` when enumeration fails (today's `AudioDevices` contract).
  - [x] `src/MyScreenRecorder.Core/Platform/PlatformServices.cs` (CREATE): `public sealed record PlatformServices(IAudioCaptureFactory AudioCaptures, IAudioDeviceCatalog AudioDevices);`.
  - [x] Short `///` docstrings on the three interfaces (contract, failure modes, side effects) — D7. None elsewhere.
- [x] Task 3: Tests first (AC: 1–3) — write them before Task 4/5; they fail to compile until the types exist in Core.
  - [x] `tests/MyScreenRecorder.Core.Tests/FakeAudioCapture.cs` (CREATE): `FakeAudioCapture : IAudioCapture` (settable `Format`; `Raise(byte[] data)` raises `DataAvailable(data, data.Length)`; `RaiseStopped()`; `Exception? StartException` thrown by `Start()`; counters `Started`, `Disposed`) and `FakeAudioCaptureFactory : IAudioCaptureFactory` (records `(deviceId, loopback)` calls; returns a new `FakeAudioCapture` per call, or throws a configured exception; exposes the created captures).
  - [x] `tests/MyScreenRecorder.Core.Tests/AudioSourceTests.cs` (CREATE): AC1 — one test per format (float32, PCM16, PCM24, PCM32, Extensible 32-bit), no data → 0, 450 ms after data → 0, after `Stopped` → 0, `1.5f` → 1, 8-bit → 0. AC2 — not armed → 0 ms, armed → 25 ms. Chain shape: 44 100 Hz float stereo → `Provider.WaveFormat.SampleRate == 48000`, `Channels == 2`; mono 48 kHz → `Channels == 2`. Ownership: `Dispose()` disposes the capture once (second `Dispose()` does not dispose again).
  - [x] `tests/MyScreenRecorder.Core.Tests/AudioEngineTests.cs` (CREATE):
    - AC3 pump silence (above). Use a `MemoryStream`: `EndSegment()` disposes the stream, `ToArray()` still works after `Dispose`.
    - Data reaches the pipe: after `BeginSegment`, the fake raises 4 800 frames of float `0.5f` stereo; after ~300 ms `EndSegment()`; the stream contains at least one sample equal to `0.5f`; afterwards the source's `Armed` is `false`.
    - `SetMic("mic-1")` → factory called with `("mic-1", false)`, capture `Started == 1`; `SetSpeaker("spk-1")` → `("spk-1", true)`; `HasAudio` true.
    - `SetMic(null)` and `SetMic("")` → `Mic` is `null`, factory not called.
    - Factory throws `InvalidOperationException("no such device")` → `Mic` is `null`, `LastError == "no such device"`.
    - Capture `Start()` throws `InvalidOperationException("busy")` → `Mic` is `null`, `LastError == "busy"`, that capture `Disposed == 1`.
    - `SetMic`/`SetSpeaker` during an active segment throw `InvalidOperationException` (`"Cannot change audio devices while recording."`).
    - `BeginSegment` twice throws `InvalidOperationException`.
    - `Dispose()` disposes mic and speaker captures.
- [x] Task 4: Move `AudioSource` to Core (AC: 1, 2)
  - [x] `git mv src/MyScreenRecorder/Audio/AudioSource.cs src/MyScreenRecorder.Core/Audio/AudioSource.cs` (namespace stays `MyScreenRecorder.Audio`).
  - [x] Constructor becomes `public AudioSource(IAudioCapture capture)`. Remove `MMDevice`, `WasapiCapture`, `MMDeviceEnumerator`, `using NAudio.CoreAudioApi`. Format logic unchanged: `capture.Format is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : capture.Format`. Subscribe `capture.DataAvailable += OnDataAvailable` (signature `(byte[] buffer, int bytes)`), `capture.Stopped += OnStopped`. On constructor failure: dispose the capture, rethrow.
  - [x] `Start()` → `_capture.Start()`; `Stop()` → `_capture.Stop()` in the same try/catch; `Dispose()` → unsubscribe, `Stop()` in try/catch, `_capture.Dispose()` in try/catch, `_peak = 0` (same order as today minus the device, which the Windows capture now owns).
  - [x] Everything else (peak math, `Armed`, `BufferedMilliseconds`, `ClearBuffer`, `DropExcess`, 2 s buffer, downmix, resampler) unchanged.
- [x] Task 5: Move `AudioEngine` to Core (AC: 3)
  - [x] `git mv src/MyScreenRecorder/Audio/AudioEngine.cs src/MyScreenRecorder.Core/Audio/AudioEngine.cs`.
  - [x] Add `public AudioEngine(IAudioCaptureFactory captures)`; `Open(deviceId, loopback)` becomes: `IAudioCapture capture = _captures.Open(deviceId, loopback); source = new AudioSource(capture); source.Start(); return source;` inside today's try/catch (`LastError = ex.Message; source?.Dispose(); return null;`). Everything else unchanged.
- [x] Task 6: Windows implementations (AC: 4)
  - [x] `src/MyScreenRecorder/Platform/Windows/WasapiAudioCapture.cs` (CREATE): `internal sealed class WasapiAudioCapture : IAudioCapture`, ctor `(MMDevice device, bool loopback)` → `loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device)`; `Format` = `_capture.WaveFormat`; forwards `DataAvailable(e.Buffer, e.BytesRecorded)` synchronously and `RecordingStopped` → `Stopped`; `Start` → `StartRecording`; `Stop` → `StopRecording`; `Dispose` → unsubscribe, `StopRecording` (try/catch), `_capture.Dispose()` (try/catch), `device.Dispose()` (try/catch). If the WASAPI constructor throws, dispose the device and rethrow.
  - [x] `src/MyScreenRecorder/Platform/Windows/WasapiAudioCaptureFactory.cs` (CREATE): `Open(id, loopback)` → `using var enumerator = new MMDeviceEnumerator(); MMDevice device = enumerator.GetDevice(id); return new WasapiAudioCapture(device, loopback);` — exactly today's lookup (`AudioSource.cs:27-30`), no extra checks.
  - [x] `git mv src/MyScreenRecorder/Audio/AudioDevices.cs src/MyScreenRecorder/Platform/Windows/MmDeviceCatalog.cs`: `internal sealed class MmDeviceCatalog : IAudioDeviceCatalog`; `Capture()`→`Inputs()`, `Render()`→`Outputs()`, `DefaultCaptureId()`→`DefaultInputId()`, `DefaultRenderId()`→`DefaultOutputId()`; bodies unchanged (`DataFlow`, `DeviceState.Active`, `Role.Multimedia`, swallow-and-empty).
  - [x] All three: namespace `MyScreenRecorder.Platform.Windows`, `[SupportedOSPlatform("windows")]`.
- [x] Task 7: Composition root (AC: 4)
  - [x] `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATE): field `private readonly PlatformServices _platform = new(new WasapiAudioCaptureFactory(), new MmDeviceCatalog());`; `_audio` (line 24) becomes `private readonly AudioEngine _audio;` assigned as the **first** statement of the constructor, before `InitializeComponent()` (`_audio = new AudioEngine(_platform.AudioCaptures);`) — today it is a field initializer, so it exists before `RecordingSession` at line 52. Lines 77–80: `AudioDevices.Capture()/Render()/DefaultCaptureId()/DefaultRenderId()` → `_platform.AudioDevices.Inputs()/Outputs()/DefaultInputId()/DefaultOutputId()`. Add `using MyScreenRecorder.Platform;` and `using MyScreenRecorder.Platform.Windows;`. Nothing else in `MainWindow` changes.
- [x] Task 8: Docs (AC: 4)
  - [x] `CLAUDE.md` (UPDATE), Architecture: the Core line also lists `AudioEngine`, `AudioSource`, the platform interfaces `IAudioCapture`/`IAudioCaptureFactory`/`IAudioDeviceCatalog` and `PlatformServices` (namespace `MyScreenRecorder.Platform`); "Audio path" names the Windows implementations `WasapiAudioCapture`/`WasapiAudioCaptureFactory`/`MmDeviceCatalog` in `src/MyScreenRecorder/Platform/Windows/` and that `MainWindow` builds `PlatformServices`.
- [x] Task 9: Verification (AC: all)
  - [x] `dotnet build MyScreenRecorder.sln -c Debug` → 0 errors, 0 warnings (baseline on `main` after PR #1: 0 warnings).
  - [x] `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` → all pass (42 existing + new).
  - [x] `grep -rn "NAudio.CoreAudioApi\|Wasapi\|MMDevice" src/MyScreenRecorder.Core` prints nothing.
  - [x] Guard checks on a copy, each failing the named test for that reason: (a) `_armed &&` removed in `OnDataAvailable` → AC2 not-armed test; (b) the 300 ms timeout check removed in `Peak` → 450 ms test; (c) `Math.Min(1f, max)` → `max` → `1.5f` test; (d) `_segmentActive` throw removed in `SetMic` → during-segment test; (e) `source?.Dispose()` removed in `Open`'s catch → Start-throws disposal test; (f) capture disposal removed from the `AudioSource` constructor failure path → write a test that reaches it first (a fake `Format` that makes `BufferedWaveProvider`/`ToSampleProvider` throw, e.g. `WaveFormat.CreateCustomFormat(WaveFormatEncoding.Adpcm, …)`); if no input reaches it, stop and ask; (g) `_disposed` check removed in `AudioSource.Dispose` → double-dispose test.
  - [x] `git diff -U0 | grep -E '^\+\s*(#|//|--)'` and the new files: only the D7 docstrings on the three interfaces.
  - [x] Ask the user to run the Windows smoke checklist W1–W9 on a build of this branch; record the result in the Dev Agent Record.

## Dev Notes

### Technical Requirements
- Core may reference only the BCL and `NAudio.Core` 2.2.1 (architecture §3.3). `BufferedWaveProvider`, `MixingSampleProvider`, `WdlResamplingSampleProvider`, `WaveFormatExtensible.ToStandardWaveFormat`, `ToSampleProvider` are in `NAudio.Core` (checked in `~/.nuget/packages/naudio.core/2.2.1/lib/netstandard2.0/NAudio.Core.dll`); `WasapiCapture`, `WasapiLoopbackCapture`, `MMDevice*`, `DataFlow`, `Role` are not — they stay in the WPF project (`NAudio` 2.2.1).
- Keep namespaces of moved Core files (`MyScreenRecorder.Audio`). Use `git mv`.
- Behaviour-preserving move: no change to the pump (`ChunkFrames` 4 800, 10 ms sleep, 200 ms → keep 100 ms drop, clamp to ±1, NaN → 0), the 2 s buffer with discard on overflow, downmix, resampling to 48 kHz, or error handling.

### ⚠️ Trap 1 — Field initialization order in MainWindow
`_audio` is a field initializer today, so it exists before `new RecordingSession(_audio)` (`MainWindow.xaml.cs:52`). When it becomes constructor-assigned, assign it before any line that reads it, or `RecordingSession` gets `null`.

### ⚠️ Trap 2 — The NAudio buffer is reused
`WaveInEventArgs.Buffer` is reused by NAudio after the handler returns. `WasapiAudioCapture` must call `DataAvailable` synchronously (no `Task.Run`, no queue), as today, where `BufferedWaveProvider.AddSamples` copies inside the handler.

### ⚠️ Trap 3 — Ownership on failure
Today a failed `AudioSource` constructor disposes both `WasapiCapture` and `MMDevice` (`AudioSource.cs:57-62`), and `AudioEngine.Open` disposes a source whose `Start()` throws. After the split: the Windows capture disposes its device when the WASAPI constructor throws; `AudioSource` disposes the capture when its constructor throws; `AudioEngine.Open` still disposes the source when `Start()` throws. No path may leak a capture or dispose it twice.

### ⚠️ Trap 4 — `WaveFormatExtensible(48000, 32, 2)` subformat
AC1's extensible case assumes NAudio's `WaveFormatExtensible(int rate, int bits, int channels)` constructor sets the IEEE-float subformat for 32 bits, so `ToStandardWaveFormat()` yields `IeeeFloat`. Confirm by reading NAudio 2.2.1 `NAudio.Core/Wave/WaveFormats/WaveFormatExtensible.cs` before writing the expectation; if it yields PCM, stop and ask.

### ⚠️ Trap 5 — Timing tests
AC1's timeout and AC3's pump are real-time tests. Use the margins in the ACs (0 → read right after `Raise`; > 300 ms → check at 450 ms; ±10 % for the pump). Do not inject a clock: it changes `AudioSource`'s contract for no product gain.

### ⚠️ Trap 6 — WASAPI cannot run here
`WasapiAudioCapture`, `WasapiAudioCaptureFactory`, `MmDeviceCatalog` compile on Linux (`EnableWindowsTargeting`) but cannot run. Their behaviour is held by Windows smoke W1–W3, W9 (AC4). Keep their bodies textually equal to today's code so the review can diff them.

### Contract Guardrail
- Interfaces: architecture §4, verbatim. A member that seems missing → stop and ask.
- Expected values: AC1/AC2 numbers come from the formats' definitions (16-bit full scale 32 768, 24/32-bit full scale 2^31 after the left shift in `ComputePeak`, float as is; bytes per second = rate × channels × 4 for float32 stereo); AC1's 300 ms and 1.0 clamp, AC3's wall-clock padding come from the released code at `b6259ee` (`AudioSource.cs`, `AudioEngine.cs`) — the regression contract (NFR1). AC3's 48 000 × 8 bytes/s is the pipe format the ffmpeg input declares (`-f f32le -ar 48000 -ac 2`, `SegmentWriter.cs:179-184`).
- Out of scope: story 1.2 items (`SegmentWriter`, `RecordingSession`, `FfmpegLocator`, process/pipe seams, `AppPaths.OutputFolder`); Linux capture (2.4); `RecorderController` (1.4). The unreachable `float.IsNaN(max)` branch in `ComputePeak` (comparisons with NaN never raise `max`) is moved as is, without a test.

### File Structure Requirements
- `src/MyScreenRecorder.Core/Platform/IAudioCapture.cs` (CREATE)
- `src/MyScreenRecorder.Core/Platform/IAudioCaptureFactory.cs` (CREATE)
- `src/MyScreenRecorder.Core/Platform/IAudioDeviceCatalog.cs` (CREATE)
- `src/MyScreenRecorder.Core/Platform/PlatformServices.cs` (CREATE)
- `src/MyScreenRecorder.Core/Audio/AudioSource.cs` (MOVE from `src/MyScreenRecorder/Audio/AudioSource.cs`, UPDATE)
- `src/MyScreenRecorder.Core/Audio/AudioEngine.cs` (MOVE from `src/MyScreenRecorder/Audio/AudioEngine.cs`, UPDATE)
- `src/MyScreenRecorder/Platform/Windows/WasapiAudioCapture.cs` (CREATE)
- `src/MyScreenRecorder/Platform/Windows/WasapiAudioCaptureFactory.cs` (CREATE)
- `src/MyScreenRecorder/Platform/Windows/MmDeviceCatalog.cs` (MOVE from `src/MyScreenRecorder/Audio/AudioDevices.cs`, UPDATE)
- `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATE)
- `tests/MyScreenRecorder.Core.Tests/FakeAudioCapture.cs`, `AudioSourceTests.cs`, `AudioEngineTests.cs` (CREATE)
- `CLAUDE.md` (UPDATE)

### Testing Requirements
- AC1 buffers (stereo, little-endian, largest absolute value 0.75):
  - float32 (`WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)`): `0.1f, -0.75f, 0.3f, 0f`
  - PCM16 (`new WaveFormat(48000, 16, 2)`): `1000, -24576, 300, 0` (24 576 / 32 768 = 0.75)
  - PCM24 (`new WaveFormat(48000, 24, 2)`): bytes `00 00 10`, `00 00 A0`, `00 00 00`, `00 00 00` (0xA0 sign-extends through `<< 24` to −0.75; 0x10 → 0.125)
  - PCM32 (`new WaveFormat(48000, 32, 2)`): `268435456, -1610612736` (0.125, −0.75)
  - Extensible (`new WaveFormatExtensible(48000, 32, 2)`): the float32 buffer (see Trap 4)
  - 8-bit (`new WaveFormat(48000, 8, 2)`): any non-zero bytes → `Peak == 0`
- AC2: 9 600 bytes = 1 200 stereo float frames = 25 ms.
- Negative paths: not armed, timeout, `Stopped`, unknown bit depth, factory throws, `Start()` throws, device change during a segment, second `BeginSegment`, double `Dispose`.
- Tests touch no process environment, so they need not join `ProcessEnvironmentCollection`.

### Previous Story Intelligence (1.1, done)
- Test-first worked: write the test, see it fail to compile (type not in Core) or fail on assertion, then move.
- Review found untested branches every time (even size, swallowed save errors, odd channel counts): cover every branch you move that has an observable effect.
- `TestResults/` is ignored; `dotnet test` needs the repo-root `global.json` (MTP).
- Tests that change the process environment belong in `ProcessEnvironmentCollection` (not needed here).
- Deferred from 1.1 (do not fix here): a region touching a monitor edge can yield a zero-width target.

### Git Intelligence Summary
- `60a23cc` merge PR #1 · `d88a178` review test gaps · `0077a81` Core split (moved `StereoDownmixSampleProvider`, `AudioDeviceInfo` out of `Audio/`) · `8763bc0` initial audio implementation (WASAPI capture, mixer, pump).

### Latest Stack Information
- .NET SDK 10.0.403 (user install in `~/.dotnet`), `net10.0` / `net10.0-windows`.
- `NAudio.Core` 2.2.1 (Core), `NAudio` 2.2.1 (WPF, includes `NAudio.Wasapi`). Do not bump.
- `xunit.v3` 4.0.2 on Microsoft Testing Platform.

### References
- [Source: `docs/planning/linux-build-architecture.md` - §2 D3, D4, D7, D9; §3 Boundaries; §4 Seams; §5.2 (shared chain)]
- [Source: `docs/planning/epics-linux-build.md` - §2 Working rules; §3 DoD; §4 Windows smoke; §6 story 1-3]
- [Source: `CLAUDE.md` - Audio path, Conventions]
- [Source: `src/MyScreenRecorder/Audio/AudioSource.cs`, `AudioEngine.cs`, `AudioDevices.cs` at `60a23cc`]
- [Source: `docs/implementation/1-1-core-and-test-projects.md` - Review Findings, Completion Notes]

## Dev Agent Record

### Agent Model Used

claude-opus-5-5 (agile-dev)

Baseline revision: `60a23cce05612e254043b79da627615d612f5d9f` (`origin/main`, PR #1 merged). Branch `feat/linux-1-3-audio-capture-seam` created with `--no-track`; `git config branch.feat/linux-1-3-audio-capture-seam.merge` prints nothing.

### Debug Log References

- `~/.dotnet/dotnet --version` → `10.0.403`. Local `main` = `origin/main` = `60a23cc`.
- Trap 4 probe (console app on `NAudio.Core` 2.2.1): `new WaveFormatExtensible(48000, 32, 2).SubFormat` = `00000003-0000-0010-8000-00aa00389b71` (IEEE float); `ToStandardWaveFormat()` → `IeeeFloat`, 32 bits. 16-bit extensible → `Pcm`. `ToSampleProvider()` exists for float32, PCM 8/16/24/32; ADPCM → `ArgumentException: Unsupported source encoding` (input for guard f).
- Red: before Task 4/5 the test project failed to compile (`CS0246: AudioSource`/`AudioEngine` not found, 34 errors).
- `dotnet build MyScreenRecorder.sln -c Debug --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)` (Core, Core.Tests, WPF app).
- `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` → `Test run summary: Passed! total: 88 failed: 0 succeeded: 88 skipped: 0` (45 existing + 43 new; three consecutive runs, all pass).
- `grep -rn "NAudio.CoreAudioApi\|Wasapi\|MMDevice" src/MyScreenRecorder.Core` → no output.
- Guard checks (copy of Core + tests, built with `--no-incremental`, full test run per mutation):
  - (a) `_armed &&` removed → `NotArmed_DataIsNotBuffered`: expected 0, actual 25.
  - (b) 300 ms timeout removed → `Peak_MoreThan300MsAfterTheLastData_IsZero`: expected 0, actual 0.75.
  - (c) `Math.Min(1f, max)` → `max` → `Peak_AboveFullScale_IsClampedToOne`: expected 1, actual 1.5.
  - (d) `_segmentActive` throw removed in `SetMic` → `SetMic_DuringASegment_Throws`: no exception thrown; same in `SetSpeaker` → `SetSpeaker_DuringASegment_Throws`.
  - (e) `source?.Dispose()` removed in `Open` → `SetMic_StartThrows_RecordsTheErrorAndDisposesTheCapture`: `Disposed` expected 1, actual 0.
  - (f) capture disposal removed from the `AudioSource` constructor catch → `Constructor_UnsupportedFormat_DisposesTheCaptureAndThrows` (ADPCM format reaches the catch): expected 1, actual 0.
  - (g) `_disposed` check removed → `Dispose_Twice_DisposesTheCaptureOnce`: expected 1, actual 2.
  - Further moved lines, each fails its test: extensible → standard conversion (`Peak_WasapiSharedModeExtensibleFormat_IsReadAsFloat`, `ArgumentException`), `_armed = false` in Dispose, unsubscribe in Dispose, final `_peak = 0f` in Dispose (`Dispose_ResetsPeak`, actual 0.75), try/catch in `Stop` and in Dispose (stop, dispose), `LastError = null`, `Mic?.Dispose()` in `SetMic`, disarm in `EndSegment`, resampler condition (`IsType` fails: `WdlResamplingSampleProvider`), `AddSamples`, engine Dispose of mic/speaker, pump clamp +1 and −1, NaN → 0, backlog drop, `ClearBuffer` in `BeginSegment`.
  - `OnStopped`: removing either `_peak = 0f` or the tick reset alone changes nothing (each alone makes `Peak` 0); removing both fails `Peak_AfterCaptureStopped_IsZero` (actual 0.75). Moved unchanged.
  - Zero-fill loop after `mixer.Read` in `Pump`: removing it fails no test. Probe: `MixingSampleProvider { ReadFully = true }` returns the full count with no inputs and with an exhausted input, so the loop is unreachable. Moved unchanged (behaviour-preserving move); see Review Findings.
- NAudio.Wasapi 2.2.1 IL (reflection dump of `WasapiCapture`): `StopRecording` only sets `captureState = StopRequested` when it is not `Stopped`; `Dispose` calls `StopRecording`, joins the capture thread and disposes the audio client; `WaveFormat` returns `waveFormat.AsStandardWaveFormat()`. So the second `StopRecording` (from `AudioSource.Dispose` and `WasapiAudioCapture.Dispose`) is harmless.
- Comments: `git diff -U0 | grep -E '^\+\s*(#|//|--)'` over src/tests/CLAUDE.md and the new files: only the `///` docstrings on `IAudioCapture`, `IAudioCaptureFactory`, `IAudioDeviceCatalog` (D7).

### Completion Notes List

- Tasks 2–8 done test-first: interfaces, then the tests (failed to compile), then the moves (`git mv`) and Windows implementations.
- `AudioSource.Dispose` calls `_capture.Stop()` in its try/catch (the one-to-one replacement of today's `_capture.StopRecording()`); calling `AudioSource.Stop()` there would behave the same (it also swallows and Dispose resets `_peak` anyway).
- Tests added beyond the story list, from guard analysis and review: `Buffer_HoldsAtMostTwoSeconds`, `DropExcess_KeepsTheRequestedMilliseconds`, `ClearBuffer_EmptiesTheBuffer`, `Start_StartsTheCapture`, `Stop_SwallowsCaptureErrorsAndResetsPeak`, `Dispose_StopsDisarmsUnsubscribesAndDisposesTheCapture`, `Dispose_ResetsPeak`, `Dispose_SwallowsCaptureErrors`, `Segment_ClampsTheMixToFullScale`, `Segment_ReplacesNaNWithSilence`, `Segment_BacklogAbove200MsIsCutTo100Ms`, `BeginSegment_DiscardsAudioBufferedBeforeTheSegment`, `SetMic_Again_DisposesThePreviousCapture`, `SetMic_AfterAFailure_ClearsLastError`, `SetSpeaker_DuringASegment_Throws`, `MicAndSpeakerPeak_ForwardTheSourcePeaks`. Expected values come from the format definitions, the pipe format (`f32le`, 48 kHz, stereo) and the released code at `b6259ee` (2 s buffer, 200/100 ms drop, ±1 clamp, NaN → 0).
- The fake capture has `Stops`, `StopException`, `DisposeException` in addition to the members the story lists (needed for the Stop/Dispose error paths).
- AC4 (Windows smoke W1–W9) passed; see below.
- Windows smoke result: W1–W9 passed (reported by the user on 2026-11-03).

### Review Findings

The four reviewer subagents (Adversarial, Edge-case, Verification gap, Acceptance) stopped on the API rate limit (HTTP 429) without returning findings; the lenses were then run by the dev agent itself, one after another.

- [x] [Review][Patch] `_peak = 0f` at the end of `AudioSource.Dispose` untested `tests/MyScreenRecorder.Core.Tests/AudioSourceTests.cs` — fixed (`Dispose_ResetsPeak`; fails with the reset removed).
- [x] [Review][Patch] `IAudioCapture.Start` docstring listed WASAPI-specific causes; `IAudioDeviceCatalog` docstring did not say an unreadable device is left out `src/MyScreenRecorder.Core/Platform/IAudioCapture.cs`, `IAudioDeviceCatalog.cs` — fixed.
- [x] [Review][Patch] Moved pump branches (clamp, NaN, backlog drop, `ClearBuffer` at segment start) untested `tests/MyScreenRecorder.Core.Tests/AudioEngineTests.cs` — fixed (four tests; each fails with its branch removed).
- [Review][Dismiss] `StopRecording` now runs twice on dispose (`AudioSource.Dispose` → `WasapiAudioCapture.Stop`, then `WasapiAudioCapture.Dispose`) — harmless: NAudio 2.2.1 `WasapiCapture.StopRecording` only sets `StopRequested` when not `Stopped`, and `WasapiCapture.Dispose` already calls it (IL dump). Prescribed by Tasks 4 and 6.
- [Review][Dismiss] Ownership on failure (Trap 3): WASAPI ctor throws → `WasapiAudioCapture` disposes the device; `AudioSource` ctor throws → capture disposed once, `source` stays `null` in `Open`, so no second dispose; `Start` throws → `Open` disposes the source once (`_disposed` guard). Held by guards e, f, g.
- [Review][Dismiss] Trap 2: `WasapiAudioCapture.OnDataAvailable` invokes `DataAvailable` synchronously; `AudioSource.OnDataAvailable` copies with `AddSamples` inside the handler.
- [Review][Dismiss] Trap 1: `_audio` is assigned before `InitializeComponent()` and before `new RecordingSession(_audio)`; `_platform` is a field initializer, so it exists before the constructor body.
- [Review][Defer] Zero-fill loop after `mixer.Read` in `AudioEngine.Pump` is unreachable (`ReadFully = true` always returns the full count, probe) — same at `b6259ee`; the story forbids pump changes, so it is moved unchanged.
- [Review][Defer] `WasapiAudioCapture.Format` is never `WaveFormatExtensible` on Windows (`WasapiCapture.WaveFormat` already returns `AsStandardWaveFormat()`, IL dump); `AudioSource`'s extensible branch therefore serves other `IAudioCapture` implementations only. Same at `b6259ee`; AC1 keeps the case as contract.
- [Review][Defer] `IAudioCaptureFactory` contract "throws if the id is not a present device": the Windows factory keeps today's lookup without extra checks (Task 6); a known but unplugged id throws only when `WasapiCapture` activates the device. Cannot be checked on Linux; covered by Windows smoke W2/W9.
- [Review][Defer] Redundant pair in `AudioSource.OnStopped` (either line alone makes `Peak` 0) — moved unchanged, behaviour-preserving.
- [Review][Dismiss] Timing tests (AC1 450 ms, AC3 ±10 %, pump tests) — margins as the story prescribes (Trap 5); three consecutive full runs passed.

### File List

- `src/MyScreenRecorder.Core/Platform/IAudioCapture.cs` (CREATED)
- `src/MyScreenRecorder.Core/Platform/IAudioCaptureFactory.cs` (CREATED)
- `src/MyScreenRecorder.Core/Platform/IAudioDeviceCatalog.cs` (CREATED)
- `src/MyScreenRecorder.Core/Platform/PlatformServices.cs` (CREATED)
- `src/MyScreenRecorder.Core/Audio/AudioSource.cs` (UPDATED — moved from `src/MyScreenRecorder/Audio/AudioSource.cs`; constructor takes `IAudioCapture`, device handling moved to the Windows capture)
- `src/MyScreenRecorder.Core/Audio/AudioEngine.cs` (UPDATED — moved from `src/MyScreenRecorder/Audio/AudioEngine.cs`; constructor takes `IAudioCaptureFactory`)
- `src/MyScreenRecorder/Platform/Windows/WasapiAudioCapture.cs` (CREATED)
- `src/MyScreenRecorder/Platform/Windows/WasapiAudioCaptureFactory.cs` (CREATED)
- `src/MyScreenRecorder/Platform/Windows/MmDeviceCatalog.cs` (UPDATED — moved from `src/MyScreenRecorder/Audio/AudioDevices.cs`; instance class implementing `IAudioDeviceCatalog`, bodies unchanged)
- `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATED — builds `PlatformServices`, `_audio` assigned first in the constructor, device lists from the catalog)
- `tests/MyScreenRecorder.Core.Tests/FakeAudioCapture.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/AudioSourceTests.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/AudioEngineTests.cs` (CREATED)
- `CLAUDE.md` (UPDATED — Core line and Audio path name the moved classes, interfaces, `PlatformServices`, Windows implementations)
- `docs/implementation/sprint-status.yaml` (UPDATED — `1-3-audio-capture-seam: review`)
- this story (UPDATED — tasks, Dev Agent Record, status)

## Change Log

- 2026-11-02: Story created (ready-for-dev). Sequenced before 1.2 (Decision Record).
- 2026-11-03: Implemented Tasks 1–8 and Task 9 verification (Windows smoke pending); review fixes: `Dispose_ResetsPeak`, pump branch tests, docstring wording. Status → review.
- 2026-11-03: Windows smoke W1–W9 passed (user report).
