# Story 1.1: Core and test projects; move platform-neutral types

Status: review

## Story

As the maintainer of MyScreenRecorder,
I want a platform-neutral `MyScreenRecorder.Core` library with unit tests that run on Linux,
so that the Linux build can reuse the Windows recorder's logic without changing Windows behaviour.

<!-- Track: Core — Covers: FR4, NFR1, NFR4 — Decisions: D3, D4, D7, D9, D10 — Windows smoke: required -->

## Decision Record

- 2026-10-29: Settings, `CaptureTarget`, `MonitorInfo`, `Quality`, `StereoDownmixSampleProvider`, `AudioDeviceInfo` move to Core; `CaptureTarget` takes monitors as input (D9, approved by the user).
- 2026-10-29: Tests use xUnit v3 4.0.2 on Microsoft Testing Platform via `global.json` (D10). Probe on .NET SDK 10.0.403: without `global.json`, `dotnet test` fails with "Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later".
- 2026-10-29: `CaptureTarget.ForAll` with an empty monitor list throws `ArgumentException`. This is a new input contract: today `ForAll` reads `SystemInformation.VirtualScreen`, which is never empty.
- 2026-10-29: Call sites pass a fresh `ScreenInfo.GetMonitors()` (not the cached `_monitors`), because today `ForAll`/`ForRegion` query the screens at call time.
- 2026-10-29 (review): `AppPaths.SettingsFile` uses `Environment.GetFolderPath(ApplicationData, SpecialFolderOption.DoNotVerify)`, approved by the user. Reason: on Linux the default option returns `""` when `~/.config` or `$XDG_CONFIG_HOME` does not exist, so the settings path became relative to the working directory (probe: missing dir, relative `XDG_CONFIG_HOME`, unset `HOME` all give a rooted path with `DoNotVerify`). Windows: `%APPDATA%` always exists, same value. Rejected: deferring to a Linux story.
- 2026-10-29 (review): `TestResults/` added to `.gitignore` (created by `dotnet test` at the repo root), approved by the user.

## Acceptance Criteria

1. **Solution builds on Linux**
   **Given** the .NET 10 SDK on Linux and a clean checkout
   **When** `dotnet build MyScreenRecorder.sln -c Debug` runs
   **Then** `MyScreenRecorder.Core`, `MyScreenRecorder` (WPF), and `MyScreenRecorder.Core.Tests` build with 0 errors
   **And** there are no warnings that `dotnet build src/MyScreenRecorder -c Debug -p:EnableWindowsTargeting=true` at `b6259ee` did not already print.

2. **Tests run**
   **Given** the solution
   **When** `dotnet test --solution MyScreenRecorder.sln` runs
   **Then** every test in `tests/MyScreenRecorder.Core.Tests` runs and passes.

3. **ALL target is the union of monitors**
   **Given** monitors `1:(0,116,1920,1080)`, `2:(1920,0,2560,1440, primary)`, `3:(4480,143,1920,1080)`
   **When** `CaptureTarget.ForAll(monitors)` is called
   **Then** `Bounds` is `(0,0,6400,1440)`, `Label` is `ALL 6400x1440`, `MonitorNumbers` is `[1,2,3]`, `Kind` is `All`
   **And** `ForAll` with an empty list throws `ArgumentException`.

4. **Region and monitor targets keep today's rules**
   **Given** monitor `1:(0,0,1920,1080)`
   **When** `ForRegion((100,100,641,361), monitors, 1)` → **Then** bounds `(100,100,640,360)`, label `REGION 640x360 @MON 1`
   **When** `ForRegion((1800,1000,400,300), monitors, 1)` → **Then** bounds `(1800,1000,120,80)` (clamped, even)
   **When** `ForRegion((3000,3000,200,200), monitors, 1)` → **Then** bounds `(0,0,1920,1080)` (no overlap → whole monitor)
   **When** `ForRegion((10,10,201,101), monitors, 9)` → **Then** bounds `(10,10,200,100)` (unknown monitor → no clamp)
   **When** `ForMonitor(new MonitorInfo(1, (0,0,1365,767), true))` → **Then** bounds `(0,0,1364,766)`, label `MON 1 1364x766`.

5. **Quality contract unchanged**
   **When** `Quality.VideoArgList(VideoQuality.P720)` is called
   **Then** it returns exactly `-vf`, `scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p`, `-c:v`, `libx264`, `-preset`, `veryfast`, `-crf`, `28`, `-g`, `30` (480 and 1080 differ only in the number)
   **And** `AudioArgList` returns `-c:a aac -b:a 128k` / `-c:a aac -b:a 256k` / `-c:a pcm_s16le`
   **And** `Extension(Wav, true)` is `mkv`; `Extension(Wav, false)`, `Extension(Aac128, true)`, `Extension(Aac256, true)` are `mp4`
   **And** labels are `480P`, `720P`, `1080P`, `AAC 128K`, `AAC 256K`, `WAV`
   **And** `AllVideo`, `AllAudio`, `VideoArgs`, `AudioArgs` no longer exist.

6. **Existing settings files still load**
   **Given** a file with the content in Dev Notes → Testing Requirements (today's format)
   **When** `Settings.Load(path)` is called
   **Then** `TargetKind="All"`, `MonitorNumber=2`, `MicId="{0.0.1.00000000}.{6b0c}"`, `SpeakerId=null`, `VideoQuality=P1080`, `AudioQuality=Wav`
   **And** for a missing file it returns the defaults (`"Monitor"`, `1`, `null`, `null`, `P720`, `Aac256`)
   **And** for a file containing `{ not json` it returns the defaults
   **And** `Save(path)` into a missing directory creates it and writes enums as strings (`"VideoQuality": "P720"`).

7. **Windows behaviour unchanged**
   **Given** `.\build.ps1` on Windows
   **When** the user runs the Windows smoke checklist W1–W9 (`docs/planning/epics-linux-build.md` §4)
   **Then** every item passes. W3's clap check is informational (D8): a missing clap is reported, it does not fail this story.

## Tasks / Subtasks

- [x] Task 1: Branch and prerequisites (AC: all)
  - [x] Confirm the .NET 10 SDK is available (`dotnet --version` → `10.0.x`). If not, stop and ask the user how to install it.
  - [x] `git fetch origin && git switch -c feat/linux-1-1-core-and-test-projects --no-track origin/main`; `git config branch.feat/linux-1-1-core-and-test-projects.merge` prints nothing.
- [x] Task 2: Solution scaffolding (AC: 1, 2)
  - [x] `global.json` (CREATE, repo root): exactly `{ "test": { "runner": "Microsoft.Testing.Platform" } }` (formatted JSON, no `sdk` section).
  - [x] `src/MyScreenRecorder.Core/MyScreenRecorder.Core.csproj` (CREATE): `net10.0`, `Nullable` enable, `ImplicitUsings` disable, `RootNamespace` `MyScreenRecorder`, `AssemblyName` `MyScreenRecorder.Core`, `PackageReference NAudio.Core 2.2.1`, `<InternalsVisibleTo Include="MyScreenRecorder.Core.Tests" />`.
  - [x] `tests/MyScreenRecorder.Core.Tests/MyScreenRecorder.Core.Tests.csproj` (CREATE): `net10.0`, `Nullable` enable, `ImplicitUsings` disable, `OutputType` `Exe`, `IsPackable` false, `PackageReference xunit.v3 4.0.2` only, `ProjectReference` to Core.
  - [x] `src/MyScreenRecorder/MyScreenRecorder.csproj` (UPDATE): add `<EnableWindowsTargeting>true</EnableWindowsTargeting>` and a `ProjectReference` to Core. Keep everything else (path, `AssemblyName`, `RuntimeIdentifier win-x64`, `NAudio 2.2.1`, embedded ffmpeg, font).
  - [x] `MyScreenRecorder.sln` (CREATE): `dotnet new sln -n MyScreenRecorder --format sln`, then `dotnet sln add` the three projects.
- [x] Task 3: Move Quality (AC: 5)
  - [x] `git mv src/MyScreenRecorder/Recording/Quality.cs src/MyScreenRecorder.Core/Recording/Quality.cs`; delete `AllVideo`, `AllAudio` (lines 21–23 at `b6259ee`) and `VideoArgs`, `AudioArgs` (lines 66–74). They have no callers (`grep -rn "AllVideo\|AllAudio\|VideoArgs\|AudioArgs" src` shows only `Quality.cs`).
- [x] Task 4: Settings and AppPaths (AC: 6)
  - [x] `src/MyScreenRecorder.Core/AppPaths.cs` (CREATE): `public static class AppPaths` with `SettingsFile` = `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyScreenRecorder", "settings.json")`.
  - [x] `git mv src/MyScreenRecorder/Settings.cs src/MyScreenRecorder.Core/Settings.cs`; replace the private `FilePath` with `AppPaths.SettingsFile`; add `internal static Settings Load(string path)` and `internal void Save(string path)` holding today's bodies; the public `Load()`/`Save()` call them with `AppPaths.SettingsFile`. Error behaviour unchanged (load errors → defaults, save errors swallowed).
  - [x] `src/MyScreenRecorder/MainWindow.xaml.cs:50-51` (UPDATE): `_firstRun = !File.Exists(AppPaths.SettingsFile);`.
- [x] Task 5: Capture types (AC: 3, 4)
  - [x] `src/MyScreenRecorder.Core/Capture/MonitorInfo.cs` (CREATE): move the record `MonitorInfo(int Number, System.Drawing.Rectangle Bounds, bool IsPrimary)` from `src/MyScreenRecorder/Capture/ScreenInfo.cs:6` unchanged (namespace `MyScreenRecorder.Capture`). `ScreenInfo` stays in the WPF project.
  - [x] `git mv src/MyScreenRecorder/Capture/CaptureTarget.cs src/MyScreenRecorder.Core/Capture/CaptureTarget.cs`; `ForAll(IReadOnlyList<MonitorInfo> monitors)` → bounds = union of `monitors[i].Bounds` (`Rectangle.Union`), then `Even`; numbers from the list; empty list → `ArgumentException`. `ForRegion(Rectangle bounds, IReadOnlyList<MonitorInfo> monitors, int monitorNumber)` → same logic as today over the given list. Remove the `System.Windows.Forms` and `ScreenInfo` references.
  - [x] `src/MyScreenRecorder/MainWindow.xaml.cs:144` (UPDATE): `CaptureTarget.ForAll(ScreenInfo.GetMonitors())`; `:200`: `CaptureTarget.ForRegion(result.Bounds, ScreenInfo.GetMonitors(), result.MonitorNumber)`.
- [x] Task 6: Audio types (AC: 1)
  - [x] `git mv src/MyScreenRecorder/Audio/StereoDownmixSampleProvider.cs src/MyScreenRecorder.Core/Audio/StereoDownmixSampleProvider.cs` (unchanged).
  - [x] `src/MyScreenRecorder.Core/Audio/AudioDeviceInfo.cs` (CREATE): move `AudioDeviceInfo(string Id, string Name)` from `src/MyScreenRecorder/Audio/AudioDevices.cs:7` unchanged (namespace `MyScreenRecorder.Audio`).
- [x] Task 7: Tests (AC: 2–6)
  - [x] `tests/MyScreenRecorder.Core.Tests/QualityTests.cs` (AC 5).
  - [x] `tests/MyScreenRecorder.Core.Tests/CaptureTargetTests.cs` (AC 3, 4, including the empty-list and no-overlap cases).
  - [x] `tests/MyScreenRecorder.Core.Tests/SettingsTests.cs` (AC 6; temp directory per test via `Directory.CreateTempSubdirectory()`, deleted afterwards).
  - [x] `tests/MyScreenRecorder.Core.Tests/AppPathsTests.cs`: on Linux (`Assert.Skip` elsewhere), `SettingsFile` equals `$XDG_CONFIG_HOME/MyScreenRecorder/settings.json`, or `$HOME/.config/...` when `XDG_CONFIG_HOME` is unset.
  - [x] `tests/MyScreenRecorder.Core.Tests/StereoDownmixSampleProviderTests.cs`: mono `[a]` → `[a,a]`; 4 channels `[1,2,3,4]` → `[2,3]` (even channels averaged left, odd right); stereo passes through.
- [x] Task 8: Docs (AC: 1, 2)
  - [x] `CLAUDE.md` (UPDATE): Commands gain `dotnet build MyScreenRecorder.sln -c Debug` (works on Linux via `EnableWindowsTargeting`) and `dotnet test --solution MyScreenRecorder.sln`; replace "There are no tests…"; drop the sentence about `-g` being duplicated in `VideoArgs`; add one line that `Quality`, `Settings`/`AppPaths`, `CaptureTarget`/`MonitorInfo`, `StereoDownmixSampleProvider`, `AudioDeviceInfo` live in `src/MyScreenRecorder.Core` (`net10.0`).
- [ ] Task 9: Verification (AC: all)
  - [x] `dotnet build MyScreenRecorder.sln -c Debug` (0 errors; compare warnings with `b6259ee`).
  - [x] `dotnet test --solution MyScreenRecorder.sln`.
  - [x] Guard checks: on a copy, remove (a) the empty-list `ArgumentException`, (b) `clamped.IsEmpty ? monitor.Bounds : clamped`, (c) the `monitor != null` check, (d) the `catch` in `Settings.Load(path)`; each matching test fails for that reason.
  - [x] `git diff -U0 | grep -E '^\+\s*(//|///)'`: no comments except what D7 allows (none expected in this story).
  - [ ] Ask the user to run the Windows smoke checklist on a build of this branch; record the result in the Dev Agent Record.

## Dev Notes

### Technical Requirements
- Core may reference only the BCL and `NAudio.Core` 2.2.1. No `System.Windows.*`, no WinForms, no P/Invoke, no `OperatingSystem.Is*` in Core (architecture §3.3).
- Keep namespaces of moved types (`MyScreenRecorder.Recording`, `MyScreenRecorder.Capture`, `MyScreenRecorder.Audio`, `MyScreenRecorder`). Use `git mv` so history follows the files.
- `System.Drawing.Rectangle` comes from `System.Drawing.Primitives`, part of the shared framework, so it works in `net10.0`. Do not add `System.Drawing.Common`.
- Do not bump NAudio (3.3.0 exists); WPF keeps `NAudio` 2.2.1, Core uses `NAudio.Core` 2.2.1, which is the same assembly version.

### ⚠️ Trap 1 — Stale monitor lists
`MainWindow` caches `_monitors` at startup, but today `ForAll`/`ForRegion` query the screens at call time. Pass `ScreenInfo.GetMonitors()` at the call site, not `_monitors`.

### ⚠️ Trap 2 — Record equality is load-bearing
`OverlayManager.EnsureWindows` compares monitor lists with `SequenceEqual` (`OverlayManager.cs:76-79`). Keep `MonitorInfo` a positional record with the same three members.

### ⚠️ Trap 3 — xUnit v3 on .NET 10 SDK
Do not add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`; with `xunit.v3` 4.x they trigger the VSTest error. `global.json` must contain the `test.runner` entry. The trait filter syntax is `--filter-not-trait "Category=Integration"` (not `--filter`).

### ⚠️ Trap 4 — Solution format
`dotnet new sln` on SDK 10 creates `.slnx` by default; pass `--format sln`.

### ⚠️ Trap 5 — Windows build inputs
`build.ps1` publishes `src\MyScreenRecorder` and `release.yml` uploads `dist/MyScreenRecorder.exe`. Do not rename or move the WPF project, its `AssemblyName`, or its RID.

### Contract Guardrail
- Behaviour-preserving refactor: the expected values for `Quality`, `CaptureTarget` labels/clamping and `Settings` format come from the released code at commit `b6259ee` (the regression contract, NFR1), from `README.md` ("QUALITY: video 480P / 720P / 1080P (never upscaled), audio AAC 128K / AAC 256K / WAV"; "`.mkv` for WAV audio"), and from `CLAUDE.md` ("forced to even dimensions (x264 requirement)").
- `ForAll` union = `SystemInformation.VirtualScreen` (documented by Microsoft as the bounding rectangle of all displays). The AC 3 monitors are the dev machine's real layout (`xrandr --listmonitors`), whose X root is 6400×1440.

### File Structure Requirements
- `global.json` (CREATE)
- `MyScreenRecorder.sln` (CREATE)
- `src/MyScreenRecorder.Core/MyScreenRecorder.Core.csproj` (CREATE)
- `src/MyScreenRecorder.Core/AppPaths.cs` (CREATE)
- `src/MyScreenRecorder.Core/Settings.cs` (MOVE from `src/MyScreenRecorder/Settings.cs`, UPDATE)
- `src/MyScreenRecorder.Core/Recording/Quality.cs` (MOVE, UPDATE)
- `src/MyScreenRecorder.Core/Capture/CaptureTarget.cs` (MOVE, UPDATE)
- `src/MyScreenRecorder.Core/Capture/MonitorInfo.cs` (CREATE, record moved from `ScreenInfo.cs`)
- `src/MyScreenRecorder.Core/Audio/StereoDownmixSampleProvider.cs` (MOVE)
- `src/MyScreenRecorder.Core/Audio/AudioDeviceInfo.cs` (CREATE, record moved from `AudioDevices.cs`)
- `src/MyScreenRecorder/MyScreenRecorder.csproj`, `src/MyScreenRecorder/MainWindow.xaml.cs`, `src/MyScreenRecorder/Capture/ScreenInfo.cs`, `src/MyScreenRecorder/Audio/AudioDevices.cs` (UPDATE)
- `tests/MyScreenRecorder.Core.Tests/MyScreenRecorder.Core.Tests.csproj` + 5 test files (CREATE)
- `CLAUDE.md` (UPDATE)

### Testing Requirements
- Settings fixture (today's format, written by `JsonStringEnumConverter` + `WriteIndented`):
  ```json
  {
    "TargetKind": "All",
    "MonitorNumber": 2,
    "MicId": "{0.0.1.00000000}.{6b0c}",
    "SpeakerId": null,
    "VideoQuality": "P1080",
    "AudioQuality": "Wav"
  }
  ```
- Rectangles in ACs are `(X, Y, Width, Height)`.
- Negative paths: empty monitor list, region with no overlap, unknown monitor number, missing settings file, corrupt settings file.

### Previous Story Intelligence
- First story of the epic; no previous story.

### Git Intelligence Summary
- `b6259ee` docs: add CLAUDE.md · `2114ef4` feat: reduce recording file size (set today's x264 args: `veryfast`, `crf 28`, `-g 30`, bicubic scale) · `bda7bb8` cancel button, tooltips, click-through overlays · `8763bc0` initial recorder.

### Latest Stack Information
- .NET SDK 10.0.403 (probed), `net10.0` / `net10.0-windows`.
- `NAudio.Core` 2.2.1 (netstandard2.0, no dependencies), `NAudio` 2.2.1.
- `xunit.v3` 4.0.2 (targets net8.0+, published 2026-10-09).

### References
- [Source: `docs/planning/linux-build-architecture.md` - §2 D3, D4, D7, D9, D10; §3 Boundaries; §6 Verified facts]
- [Source: `docs/planning/epics-linux-build.md` - §2 Working rules; §3 DoD; §4 Windows smoke checklist]
- [Source: `CLAUDE.md` - Commands, Conventions]
- [Source: `src/MyScreenRecorder/Capture/CaptureTarget.cs` at `b6259ee`]
- [Source: `src/MyScreenRecorder/Recording/Quality.cs` at `b6259ee`]
- [Source: `src/MyScreenRecorder/Settings.cs` at `b6259ee`]

## Dev Agent Record

### Agent Model Used

claude-opus-5-5 (agile-dev)

Baseline revision: `b6259ee1d9c7382ee9d0919fedc25c6a0ab95a4a`

### Debug Log References

- .NET SDK: not installed at start; installed 10.0.403 into `~/.dotnet` with `dotnet-install.sh --version 10.0.403` (user's choice). `~/.dotnet/dotnet --version` → `10.0.403`.
- Baseline warnings (AC1), clean export of `b6259ee`: `dotnet build src/MyScreenRecorder -c Debug -p:EnableWindowsTargeting=true` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- `dotnet build MyScreenRecorder.sln -c Debug --no-incremental` → `MyScreenRecorder.Core`, `MyScreenRecorder.Core.Tests`, `MyScreenRecorder` built; `Build succeeded. 0 Warning(s) 0 Error(s)`.
- `dotnet test --solution MyScreenRecorder.sln` → `Test run summary: Passed! total: 42 failed: 0 succeeded: 42 skipped: 0`; same with `--filter-not-trait "Category=Integration"`.
- Guard checks on copies (`mutate.py`, each copy built and tested separately):
  - (a) empty-list guard removed → `ForAll_EmptyMonitorList_Throws` fails: expected `ArgumentException`, got `InvalidOperationException: Sequence contains no elements`.
  - (b) `clamped.IsEmpty ? monitor.Bounds : clamped` → `clamped` → `ForRegion_WithoutOverlap_TakesTheWholeMonitor` fails: `{0,0,0,0}`.
  - (c) `monitor != null` → `true` → `ForRegion_UnknownMonitor_IsNotClamped` fails: `NullReferenceException`.
  - (d) `catch` in `Settings.Load(path)` removed → `Load_CorruptFile_ReturnsDefaults` fails: `JsonException`.
  - (e) `JsonStringEnumConverter` removed → `Load_ReadsEveryFieldOfTodaysFormat` and `Save_IntoMissingDirectory_CreatesItAndWritesEnumsAsStrings` fail.
  - (f) `global.json` removed → `dotnet test --solution` fails (`MSB1001: Unknown switch`, VSTest mode).
  - (g) `EnableWindowsTargeting` removed → `NETSDK1100`.
  - (h) `Even` removed from `ForAll` → `ForAll_ForcesEvenSize` fails: height 1081.
  - (i) `catch` in `Settings.Save(path)` → `finally` → `Save_Failure_IsSwallowed` fails: `IOException`.
  - (j) `DoNotVerify` removed → 4 AppPaths cases fail: `MyScreenRecorder/settings.json` (relative).
  - (k) probe: a reader test looping over `HOME` for 3 s while `AppPathsTests` hold a temp `HOME` for 200 ms: with `[Collection(typeof(ProcessEnvironmentCollection))]` 43/43 pass; without it the reader sees `HOME=/tmp/srec-apppaths-…`.
- Probe of `Environment.GetFolderPath(ApplicationData)` on Linux: default option → `""` when the folder is missing; `DoNotVerify` → rooted path for HOME without `.config`, missing `XDG_CONFIG_HOME`, relative `XDG_CONFIG_HOME` (ignored per XDG spec), and unset `HOME`.
- `git diff -U0 | grep -E '^\+\s*(#|//|--)'` and new files: no comments added.

### Completion Notes List

- Tasks 3–6 were done test-first: each Task 7 test file was written before its move and failed to compile (type missing from Core) or failed on assertion (`RemovedMembers_DoNotExist` before the dead members were deleted), then passed after the move.
- `AppPaths.SettingsFile` uses `SpecialFolderOption.DoNotVerify` (Decision Record, review). The story text prescribed the default option; the change was approved by the user.
- `AppPathsTests` change `HOME`/`XDG_CONFIG_HOME`; they run in `ProcessEnvironmentCollection` (parallelization disabled). Future tests that change or read the process environment belong in that collection.
- Added tests beyond the story list, from review: `ForAll_ForcesEvenSize`, `Save_Failure_IsSwallowed`, `SettingsFile_OnLinux_IsUnderXdgConfigHomeThatDoesNotExistYet`, `SettingsFile_OnLinux_FallsBackToHomeConfigEvenWhenItDoesNotExistYet` (null, empty, relative).
- AC7 (Windows smoke W1–W9) is pending: the user runs it on a Windows build of this branch; the result goes below.
- Windows smoke result: _pending_.

### Review Findings

Lenses: Acceptance and Adversarial (subagents, change judged small/low-risk); every finding verified before routing.

- [x] [Review][Patch] Settings path relative on Linux when the config folder is missing (`GetFolderPath` returns `""`) `src/MyScreenRecorder.Core/AppPaths.cs` — fixed with `DoNotVerify` after the user's decision (intent gap); tests added.
- [x] [Review][Patch] Fallback test created `.config` and hid the missing-folder case `tests/MyScreenRecorder.Core.Tests/AppPathsTests.cs` — fixed (same root cause).
- [x] [Review][Patch] Even size of `ForAll` untested `tests/MyScreenRecorder.Core.Tests/CaptureTargetTests.cs` — fixed (`ForAll_ForcesEvenSize`).
- [x] [Review][Patch] "Save errors swallowed" untested `tests/MyScreenRecorder.Core.Tests/SettingsTests.cs` — fixed (`Save_Failure_IsSwallowed`).
- [x] [Review][Patch] AppPaths tests mutate process environment while classes run in parallel `tests/MyScreenRecorder.Core.Tests/ProcessEnvironmentCollection.cs` — fixed (non-parallel collection, probe k).
- [x] [Review][Patch] `dotnet test` creates `TestResults/` at the repo root `.gitignore` — fixed after the user's decision.
- [Review][Dismiss] `ForAll` could throw on Windows if `ScreenInfo.GetMonitors()` were empty — unreachable: `Screen.AllScreens` falls back to the primary screen (`dotnet/winforms` release/10.0 `src/System.Windows.Forms/System/Windows/Forms/Screen.cs:115,119`).
- [Review][Dismiss] Story bookkeeping incomplete — the lens read the story before it was filled in.
- [Review][Defer→AC7] Union of `Screen.AllScreens` bounds equals `SystemInformation.VirtualScreen` cannot be checked on Linux — covered by Windows smoke W5 (ALL target) and W1.

### File List

- `global.json` (CREATED)
- `MyScreenRecorder.sln` (CREATED)
- `.gitignore` (UPDATED — `TestResults/`, review decision)
- `CLAUDE.md` (UPDATED — commands, tests, Core line; dropped the `VideoArgs` sentence)
- `src/MyScreenRecorder.Core/MyScreenRecorder.Core.csproj` (CREATED)
- `src/MyScreenRecorder.Core/AppPaths.cs` (CREATED)
- `src/MyScreenRecorder.Core/Settings.cs` (UPDATED — moved from `src/MyScreenRecorder/Settings.cs`; `Load(path)`/`Save(path)`, path from `AppPaths`)
- `src/MyScreenRecorder.Core/Recording/Quality.cs` (UPDATED — moved; `AllVideo`, `AllAudio`, `VideoArgs`, `AudioArgs` removed)
- `src/MyScreenRecorder.Core/Capture/CaptureTarget.cs` (UPDATED — moved; `ForAll`/`ForRegion` take the monitor list)
- `src/MyScreenRecorder.Core/Capture/MonitorInfo.cs` (CREATED — record moved from `ScreenInfo.cs`)
- `src/MyScreenRecorder.Core/Audio/StereoDownmixSampleProvider.cs` (UPDATED — moved, unchanged)
- `src/MyScreenRecorder.Core/Audio/AudioDeviceInfo.cs` (CREATED — record moved from `AudioDevices.cs`)
- `src/MyScreenRecorder/MyScreenRecorder.csproj` (UPDATED — `EnableWindowsTargeting`, Core reference)
- `src/MyScreenRecorder/MainWindow.xaml.cs` (UPDATED — `AppPaths.SettingsFile`; `ForAll`/`ForRegion` get `ScreenInfo.GetMonitors()`)
- `src/MyScreenRecorder/Capture/ScreenInfo.cs` (UPDATED — `MonitorInfo` moved out)
- `src/MyScreenRecorder/Audio/AudioDevices.cs` (UPDATED — `AudioDeviceInfo` moved out)
- `tests/MyScreenRecorder.Core.Tests/MyScreenRecorder.Core.Tests.csproj` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/QualityTests.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/CaptureTargetTests.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/SettingsTests.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/AppPathsTests.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/StereoDownmixSampleProviderTests.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/ProcessEnvironmentCollection.cs` (CREATED — review)
- `docs/planning/linux-build-architecture.md` (UPDATED — §5.11 Settings row: `DoNotVerify`)

## Change Log

- 2026-10-29: Story created (ready-for-dev).
- 2026-10-29: Implemented Tasks 1–9 (Windows smoke pending); review fixes: `DoNotVerify` in `AppPaths`, non-parallel environment tests, `ForAll` even-size and `Save` error tests, `TestResults/` ignored. Status → review.
