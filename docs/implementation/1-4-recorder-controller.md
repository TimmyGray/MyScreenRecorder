# Story 1.4: RecorderController

Status: review

## Story

As the maintainer of MyScreenRecorder,
I want the UI-agnostic logic of `MainWindow.xaml.cs` (targets, device lists, initial selection, settings save mapping, start/pause/resume/stop/cancel, button enablement, status text, overlay state, level meters) in a `RecorderController` in `MyScreenRecorder.Core`,
so that the Linux UI (story 3-1) and later the Windows UI (backlog 5-1) are thin views over one tested implementation, while the shipped WPF `MainWindow` stays untouched.

<!-- Track: Core — Covers: NFR4 (FR2, FR3, FR6 logic) — Decisions: D3, D7, D9 — Windows smoke: NOT required (no file under src/MyScreenRecorder/ changes) -->

## Decision Record

Decisions D1–D11 of the architecture document are not reopened here. The entries below are the decisions this story adds; the reviewer may challenge any of them before development starts.

- 2026-11-03: 1-2 and 1-3 are done and merged (1-2 merged in `df93a35`; tracker commit `3d2d000`), so 1-4 is unblocked. `MainWindow.xaml.cs` is the reference ("today"). Every line number in this story is a line of `src/MyScreenRecorder/MainWindow.xaml.cs` at `3d2d000` unless another file is named; `git diff 3d2d000 HEAD -- src/MyScreenRecorder/MainWindow.xaml.cs` must stay empty (Task 1).
- 2026-11-03: **No Windows smoke.** The epic marks 1-4 without *Windows smoke*, and this story changes nothing under `src/MyScreenRecorder/`: the controller is new code that nothing in the WPF project calls. The one edit to existing Core code (`RecordingSession` implements the new `IRecordingSession`) adds a base interface and no behaviour. DoD item 7 therefore does not apply; it is replaced by a hard check: `git diff --stat -- src/MyScreenRecorder/` prints nothing.
- 2026-11-03: **`PlatformServices` is NOT extended with the screen catalog.** Architecture §4 says "1.4 adds the screen catalog" to `PlatformServices`. Adding a seventh positional member breaks `new PlatformServices(...)` at `MainWindow.xaml.cs:26-32`, i.e. forces an edit of the WPF project (and a Windows `IScreenCatalog` implementation) that the epic forbids in this story. `RecorderController` takes `IScreenCatalog` as its own constructor parameter, like `AudioEngine` takes only `IAudioCaptureFactory` (1-3) and `SegmentWriter` only the launcher/pipe/grab (1-2): the narrowest dependency. Story 3-1's composition root passes the Avalonia catalog directly. Whether to also fold it into `PlatformServices` is left to 5-1, when the WPF root is edited anyway. Architecture §4 is amended (listed in the Change Log). Rejected: extending the record and editing `MainWindow.xaml.cs` line 26 plus adding `WindowsScreenCatalog` (reopens Windows verification for no gain in this story).
- 2026-11-03: **`IRecordingSession` is introduced** (`MyScreenRecorder.Recording`, Core) with exactly the members `RecorderController` uses; `RecordingSession : IRecordingSession, IDisposable` (declaration change only). Reason: `RecordingSession.Stats` reads a private `Stopwatch` (`RecordingSession.cs:33,74`), so elapsed time cannot be injected, and AC4 (overlay changes *before* `StartAsync`) needs a session whose `StartAsync` can be held pending and that logs the moment it is entered. With the interface a fake session gives deterministic `SessionStats`, deterministic states and a gated `StartAsync`; the real session is still covered by a small wiring test class (Testing Requirements, W-tests). Rejected: (a) controller over the concrete `RecordingSession` only (elapsed time and MB are real time, so the status text could be tested only with regexes, and AC4 could be observed only through a launcher callback deep inside the pipeline); (b) a clock interface injected into `RecordingSession` (changes moved 1-2 code that Windows smoke covered); (c) the controller computing elapsed time itself from its own timer (a second clock that can disagree with the one that stops during pause).
- 2026-11-03: **Time and timers.** The controller owns no timer and reads no clock. The elapsed time and byte count in the status text come from `IRecordingSession.Stats` at the moment `UpdateStatus()` runs; the UI calls `UpdateStatus()` every `RecorderController.StatusIntervalMs` (500, `MainWindow.xaml.cs:39`) and `TickMeters()` every `MeterIntervalMs` (50, `:38`). Tests call the methods directly with a fake session whose `Stats` they set, which is the "injected clock": no test waits for real time except the 300 ms peak window is avoided (see Trap 4). Rejected: an `ITimer`/`IClock` abstraction inside Core (adds a type that only restates "call this method every N ms"; the two intervals are different, and the UI frameworks already have timers).
- 2026-11-03: **Threading.** The constructor takes `Action<Action> marshal`. Every `IRecordingSession` callback (`StateChanged`, `Faulted`) is passed through `marshal` before it touches controller state; the UI passes its dispatcher post (WPF `Dispatcher.BeginInvoke`, Avalonia `Dispatcher.UIThread.Post`); tests pass `a => a()` or a queue. Reason: `RecordingSession` raises `StateChanged` from `SetState` after `ConfigureAwait(false)` continuations (`RecordingSession.cs:131,434-438`) and `Faulted` from `Task.Run` (`:301-313`), i.e. on pool threads; today `MainWindow` marshals both with `Dispatcher.BeginInvoke` (`:66-71`). All other members (commands, `Select*`, `UpdateStatus`, `TickMeters`, property reads) are called on the UI thread only. The command methods `await` without `ConfigureAwait(false)` so their continuations return to the UI thread through its `SynchronizationContext`, exactly like the async-void handlers they replace. `Changed` is therefore always raised on the UI thread. A posted action first checks `_disposed` and does nothing when the controller was disposed after the action was queued: no state change, no `Changed` (test E15b). Rejected: locking inside the controller (UI would still read inconsistent combinations of properties); an `IUiThread` interface (a delegate says the same).
- 2026-11-03: **One event, snapshot properties.** `public event Action? Changed;` is raised after every mutation that can change a property (selection, controls, status, overlay, target). The UI re-reads the properties; it does not get diffs. `UpdateStatus()` raises it only when the new `StatusLine` differs from the stored one (record equality), so the 500 ms timer does not cause a repaint while idle. `TickMeters()` never raises it (20 Hz; the UI reads `MicLevel`/`SpeakerLevel` after its own timer tick). Rejected: one event per property group (more surface, same information).
- 2026-11-03: **Initialization in the constructor.** `new RecorderController(...)` does what `MainWindow.Initialize()` does at `:82-94` and `:110-113`: reads the monitors, builds the targets, reads the device lists, picks the initial indices, applies the initial mic/speaker to the `AudioEngine`, checks ffmpeg. There is no `_ready` flag (`:51,182,357,386`): nothing can call back before the constructor returns, and no save happens during construction. Rejected: a separate `Initialize()` (two-phase initialization invites using a half-built object).
- 2026-11-03: **Settings are injected as an object plus a save delegate and a first-run flag**: `Settings settings, bool firstRun, Action saveSettings`. The composition root computes `firstRun = !File.Exists(AppPaths.SettingsFile)` *before* `Settings.Load()` (`:62-63`) and passes `settings.Save`. Reason: `Settings.Load()/Save()` use `AppPaths.SettingsFile` (real `$HOME` or `%APPDATA%`); with a delegate the tests read the mutated `Settings` at each save call and never touch the file system or the process environment. Rejected: the controller calling `Settings.Load/Save` itself (tests would need `HOME`/`XDG_CONFIG_HOME` swaps and the non-parallel collection for every test).
- 2026-11-03: **Monitors are snapshotted once**, at construction (`IScreenCatalog.GetMonitors()` called once). `ALL`, the monitor targets and `CaptureTarget.ForRegion` all use that snapshot. Today the monitors are read once (`:82`) but `ALL` and the region clamp re-read them (`:155`, `:211`), so after a hot-plug the list and `ALL` would disagree. This is a deliberate small deviation; hot-plug handling is not in scope for any story. Rejected: copying the mixed stale/fresh reads.
- 2026-11-03: **Zero monitors** make the constructor throw `InvalidOperationException("No monitors found.")`. Today `CaptureTarget.ForAll` throws `ArgumentException` inside `RebuildTargets` (`:155`, `CaptureTarget.cs` `ForAll`), also an unhandled startup crash. The new type/message is a decision of this story; story 3-1 must catch it and show it (listed in Contract Guardrail).
- 2026-11-03: **Selections are ignored unless `State == Idle && !busy`.** `SelectSource/VideoQuality/AudioQuality/Mic/Speaker` and `SetRegion` do nothing (but raise `Changed`, so a view snaps back to the controller's indices) when the session is not idle or a command is in flight. Today the cyclers are disabled by `UpdateUi` (`:362-367`), but between the REC click and `StateChanged` the session is `Idle`, `_busy` is true and the cyclers are still enabled; `AudioEngine.SetMic/SetSpeaker` throw `InvalidOperationException("Cannot change audio devices while recording.")` once a segment is active (`AudioEngine.cs:44-57`), which would crash the async-void handler. `ControlState.*Enabled` for the selectors stays exactly `idle` (AC3 equals `UpdateUi`); the extra `!busy` guard is only in the methods. **The guard reads live values** (`_session.State == RecordingState.Idle && !_busy`), never the cached `Controls`: with a queueing `marshal`, `Controls` is stale between a session `StateChanged` and the drain of the queue (tests B10b, E5b). An index outside the list throws `ArgumentOutOfRangeException` in every state, checked **first**, before the guards (programming error; the cycler wraps before calling). **Selecting the current index is a no-op** (`SelectSource(SourceIndex)`, `SelectMic(MicIndex)`, …): no engine call, no save, no `Changed`. Reason: the cycler never raises `Changed` without moving (`Cycler.cs:47-52`: `Step` returns when there are fewer than 2 items and otherwise always changes the index), so a same-index call does not happen today and must not cause a device reopen or a write. Order inside each method: the range check first (throws in every state, also when the session is not idle), then the idle-and-not-busy guard (a refused call raises `Changed`), then the same-index check, then the change (tests B11, B10, B12). `SetRegion` has no current index and always applies.
- 2026-11-03: **The settings mapping drops the `MonitorNumbers.Length > 0` condition of `:184`** and writes `MonitorNumber = Target.MonitorNumbers[0]` whenever `Target.Kind != All`. Reason: the condition is unreachable, because every target the controller holds comes from `CaptureTarget.ForMonitor` (`[m.Number]`), `ForAll` (throws on an empty list, otherwise all numbers) or `ForRegion` (`[monitorNumber]`), so `MonitorNumbers` is never empty. Confirmed by QA (error log 168).
- 2026-11-03: **`StartAsync` is guarded by live state**: `_session.State == RecordingState.Idle && !_busy && _ffmpeg.Resolve() != null`, evaluated when the method is called, not by reading the cached `Controls.RecEnabled`, and not only by `_busy` (`:224`). Today the REC button is disabled in those cases (`:368`), so the guard is the same condition expressed where the logic lives. Reason for the live read: with a queueing `marshal`, a session `StateChanged` that is queued but not yet drained leaves `Controls` stale (test E5b). Rejected: reading `Controls.RecEnabled`.
- 2026-11-03: **AC4 mechanism.** `StartAsync()` sets `busy` and an internal `starting` flag, clears the message and last file, calls `Refresh()` (which makes `Overlay` = `OverlayMode.Recording` while `starting`, and raises `Changed`), and only then awaits `IRecordingSession.StartAsync`. In `finally`: `busy = false; starting = false; Refresh()`. A failed start therefore returns `Overlay` to `Idle` (derived from the session state) and shows `ERROR: <message>`. Resume does **not** pre-change the overlay: architecture §5.8 names `ResumeAsync` next to `StartAsync`, but badges are shown only while `Idle` and a paused recording is already non-idle (`OverlayManager.Update`, `showBadge = state == OverlayState.Idle`, `OverlayManager.cs`), so there is no badge to hide; the border colour changes from yellow to red when the session reports `Recording`, as today.
- 2026-11-03: **Overlay type name `OverlayMode`** (namespace `MyScreenRecorder.Controller`, values `Idle, Recording, Paused`), not `OverlayState`. Reason: `MyScreenRecorder.Overlay.OverlayState` already exists in the WPF project with the same values (`OverlayManager.cs`); a second type of that name in Core would be an ambiguity or a CS0436 warning in `MainWindow.xaml.cs`, which imports both namespaces. 5-1 may unify them. The target the overlay is drawn for is the controller's `Target`. Which edges, badges or borders are drawn is the overlay manager's business (3-4), not the controller's.
- 2026-11-03: **Status severity enum `StatusSeverity { Normal, Recording, Warning, Success, Error }`** and `record StatusLine(string Text, StatusSeverity Severity)`. Mapping to the brushes in `UpdateStatus` (`:388,398,403,407-408`): `Normal` = `FgBrush`, `Recording` = `RedBrush` (the `* REC` line), `Warning` = `YellowBrush` (the `|| PAUSED` line and `SAVING...`), `Success` = `GreenBrush` (`SAVED…`), `Error` = `RedBrush` (`ERROR…`). `Recording` and `Error` are separate members although both are red today, so a view may style them differently without parsing text. The classification of an idle message is by prefix, exactly as today: `StartsWith("ERROR", StringComparison.Ordinal)` → `Error`, `StartsWith("SAVED", StringComparison.Ordinal)` → `Success`, anything else (including `CANCELED…`, `CANCELING...`, `Finalize failed: …`, `Could not delete …`, `No video data was recorded…`) → `Normal`. That `RecordingSession.LastError` texts without the `ERROR` prefix are shown in the normal colour is existing behaviour and is kept (AC2: "match today's strings"); changing it is a decision for the user.
- 2026-11-03: **The number in the status text uses the current culture**, as today (`$"{stats.Bytes / 1048576.0:F1}"` at `:397` has no culture argument). A machine with a comma decimal separator shows `1,5 MB`. The epic's `N.N` is the shape, not the separator. Rejected: `InvariantCulture` (changes what a German or Russian Windows user sees when 5-1 adopts the controller; no requirement asks for it). `TimeSpan` custom format `hh\:mm\:ss` is culture-independent and keeps today's quirks: hours wrap at 24 (`25 h` prints `01:00:00`) and fractions are truncated (probe, below).
- 2026-11-03: **Level meters are in the controller** (`MicLevel`, `SpeakerLevel`, `TickMeters()`, the `0.85f` decay of `:195-201`), although the epic's list does not name them. Reason: the same tick method is needed for the UI timer anyway, FR6 is a Linux requirement, and leaving 5 lines of smoothing to each UI would duplicate logic. The decay is a pure `internal static float LevelMeter.Next(float peak, float previous)` so it is tested without waiting for the 300 ms peak window.
- 2026-11-03: **Cancel confirmation is a delegate**: `Task CancelAsync(Func<Task<bool>> confirm)`. The controller keeps the guard sequence of `:272-284` (busy/state check, ask, re-check the state after the answer); the view supplies the Yes/No dialog (default No, FR3, architecture §5.12). Rejected: `bool` flags set by the view (the re-check after the dialog would move into each view).
- 2026-11-03: **Closing**: `Task<bool> PrepareCloseAsync()` implements `OnClosing` (`:421-437`): `Recording`/`Paused` → run the stop flow and return `true`; `Finalizing` → return `false` (the view cancels the close); otherwise `true`. The `_closing` re-entrancy flag, `e.Cancel` and `Close()` stay in the view.
- 2026-11-03: **OPEN**: the process start (`explorer.exe` today, `xdg-open` on Linux) stays in the view. The controller exposes `string? LastFile` (the last saved path **only if the file still exists**, `:339`; otherwise `null`) and `void ReportError(string message)` (sets `"ERROR: " + message`, `:350`). The output folder is `AppPaths.OutputFolder`, read by the view.
- 2026-11-03: **Namespace and folder**: `MyScreenRecorder.Controller`, `src/MyScreenRecorder.Core/Controller/`. `IScreenCatalog` is in `MyScreenRecorder.Platform` (architecture §4: platform seams live there). `IRecordingSession` is in `MyScreenRecorder.Recording` next to `RecordingSession`. Rejected: root namespace `MyScreenRecorder` (the root holds `Settings`/`AppPaths` only) and `MyScreenRecorder.Recording` (the controller composes Recording, Audio, Capture and Settings; it is not part of the pipeline).
- 2026-11-03: **Quirks of `MainWindow` that are copied on purpose** (each has a test): (1) a failed `AudioEngine.Open` leaves `Mic`/`Speaker` null with only `AudioEngine.LastError` set and the controller shows nothing (`MainWindow` never reads `_audio.LastError`); (2) a saved `VideoQuality`/`AudioQuality` that is a number outside the enum (the JSON converter accepts numbers; probe) gives index −1, which the cycler clamps to 0, so the controller clamps to `P480`/`Aac128` (`Cycler.cs` `SetItems`); (3) the `CANCELING...` message is shown only while the session state is `Idle`; while the state is `Finalizing` the line reads `SAVING...` (`:401-404`), also during a cancel; (4) after `CancelAsync` the message is `_session.LastError ?? "CANCELED: recording discarded"` (`:292`), and `LastError` is cleared only by `StartAsync`, so a stale `LastError` can hide `CANCELED`; (5) the saved `TargetKind` of a REGION target is `"Monitor"` with the region's monitor number, so a region is not restored (`:183-187`); (6) `StopAsync` checks only the session state, not `busy` (`:307`); (7) `PrepareCloseAsync` while a `StartAsync` is pending (state `Idle`, busy) returns `true`, so the view closes while the start is in flight, because `OnClosing` looks only at the state (`:425-436`); do not "fix" it here (row in E10).
- 2026-11-03: Acceptance-criteria numbering: story AC1–AC4 = epic AC1–AC4 (wording sharpened with exact inputs); AC5 (commands), AC6 (threading, timers, meters, close) and AC7 (WPF untouched, build, no Windows change) are added.

- 2026-11-03 (full review): **A throwing `Changed` subscriber must not leave the controller busy (R1).** In `StartAsync`, `PauseOrResumeAsync`, `CancelAsync` and `StopRecordingAsync` the flags and the message are set before `try`, and the first `Refresh()` is the first statement inside the `try`; the existing `catch` then shows `ERROR: <message>` and `finally` clears `_busy`/`_starting` and refreshes. Rejected: swallowing subscriber exceptions where `Changed` is invoked, because it hides view bugs.
- 2026-11-03 (full review): **Cancel re-entry (R2).** `CancelAsync` returns when `_busy || _confirming`; `_confirming` is set around `await confirm()` in `try/finally`; after the answer it returns when `!confirmed || _busy || !IsActive()`. `_busy` is not set during the dialog, so `Controls` during the dialog stay as `UpdateUi` computes them (rejected: setting `_busy` during the dialog, which would grey out the buttons behind the dialog). A pause that starts during the dialog wins; a later "yes" does not cancel.
- 2026-11-03 (full review): **`Dispose` clears `Changed` (R7)**, so a command that completes after `Dispose` raises nothing into a disposed view. **`Targets` and `SourceLabels` are cached read-only objects (R9)**, rebuilt only by `RebuildTargets`.

## Acceptance Criteria

1. **Initial selection**
   **Given** monitors `1: (0,116,1920×1080)`, `2: (1920,0,2560×1440) primary`, `3: (4480,143,1920×1080)` and the settings and devices of Testing Requirements group A
   **When** a `RecorderController` is constructed
   **Then** `SourceLabels` is `MON 1 1920x1080`, `MON 2 2560x1440`, `MON 3 1920x1080`, `ALL 6400x1440`; first run → the default devices are selected and the saved ids are ignored; later runs → the saved ids; an unknown saved id → `NONE` (index 0, nothing opened); saved `TargetKind "All"` → `ALL`; an unknown `MonitorNumber` → the primary monitor (index 0 when no monitor is primary); quality indices come from the settings; the constructor performs no save (cases A1–A17).

2. **Status texts match today's strings**
   **Given** a fake session in the states and with the `SessionStats` / messages of Testing Requirements group C
   **Then** `Status.Text` and `Status.Severity` equal the table exactly: `READY`; `* REC  hh:mm:ss  N.N MB  <quality>`; `|| PAUSED  …` (same fields); `SAVING...`; `SAVED: <file name>`; `CANCELING...`; `CANCELED: recording discarded`; `ERROR: <message>`; `ERROR: ffmpeg not found`; `ERROR: save failed`; session `LastError` texts as they are (cases C1–C17).

3. **Enablement per state equals `MainWindow.UpdateUi`**
   **Given** each state × busy × ffmpeg combination of the table in Testing Requirements group D
   **Then** `Controls` equals the table, which is `MainWindow.xaml.cs:362-372` evaluated for that combination.

4. **Badges hidden before capture**
   **Given** a fake session whose `StartAsync` is held pending
   **When** `StartAsync()` of the controller is called
   **Then** while the session call is still pending `Overlay == OverlayMode.Recording` and `Controls.RecEnabled == false`, and the log of events shows `Changed` (overlay non-idle) **before** `session.StartAsync` was entered; when the session call then fails, `Overlay` is `Idle` again and the status is `ERROR: <message>`; when it succeeds and the session reports `Recording`, `Overlay` stays `Recording` (cases E1–E3).

5. **Commands, settings mapping, region, close**
   **Given** the fake session and settings delegate
   **Then** start/pause/resume/stop/cancel call the session as `MainWindow` does, with the same guards and messages; every selection change writes exactly the settings fields of the mapping table; `SetRegion` adds the region target; `PrepareCloseAsync` follows `OnClosing` (cases B1–B12, E4–E12, E5b, E15b).

6. **Threading, timers, meters**
   **Given** a `marshal` that queues actions
   **When** the session raises `StateChanged`/`Faulted` from a pool thread
   **Then** the controller changes nothing and raises nothing until the queue is drained, then exactly one `Changed`; after `Dispose` the session events no longer reach the controller; `UpdateStatus()` raises `Changed` only when the status line changed; `TickMeters()` follows `max(peak, previous × 0.85)` (cases E13–E16 including E15b, M1–M3).

7. **WPF and Windows untouched**
   **Given** the branch after the story
   **Then** `git diff --stat 3d2d000 -- src/MyScreenRecorder/` prints nothing; `dotnet build MyScreenRecorder.sln -c Debug` has 0 errors and 0 new warnings (the WPF project still compiles with the unchanged `MainWindow.xaml.cs`); the real `RecordingSession` works through the controller (cases W1–W3).

## Tasks / Subtasks

- [x] Task 1: Branch and prerequisites (AC: all)
  - [x] `dotnet --version` (or `~/.dotnet/dotnet --version`) → `10.0.x`. If missing, stop and ask.
  - [x] `git branch --show-current` → `feat/linux-1-4-recorder-controller`; `git log --oneline -1` shows `3d2d000` (the tracker commit on top of the merge `df93a35`) or a descendant containing it; `git config branch.feat/linux-1-4-recorder-controller.merge` prints nothing. If not, stop and ask.
  - [x] `git status` shows only the pre-existing bookkeeping changes of this planning step (this story file, `docs/implementation/sprint-status.yaml`, `docs/planning/epics-linux-build.md`, `docs/planning/linux-build-architecture.md`). Leave them; they go into the File List as pre-existing.
  - [x] Confirm "code unchanged": `git diff 3d2d000 HEAD -- src/MyScreenRecorder/MainWindow.xaml.cs src/MyScreenRecorder.Core/Recording/RecordingSession.cs src/MyScreenRecorder.Core/Audio/AudioEngine.cs` prints nothing (line numbers in this story rely on it). If it prints a diff, stop and ask.
  - [x] `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` passes before any change (baseline count goes into the Dev Agent Record).
- [x] Task 2: Tests first (AC: 1–6) — write them before Tasks 3–6; they do not compile until the types exist.
  - [x] Test support (CREATE in `tests/MyScreenRecorder.Core.Tests/`): `FakeRecordingSession.cs`, `FakeScreenCatalog.cs`, `FakeAudioDeviceCatalog.cs`, `ControllerFixture.cs` (shapes in Testing Requirements → Test support). Reuse, do not duplicate: `FakeAudioCapture`/`FakeAudioCaptureFactory`, `FakeFfmpeg`, `FakeChildProcessLauncher`, `FakeAudioPipeFactory`, `TestPlatform`, `ProcessEnvironmentCollection`.
  - [x] `RecorderControllerInitTests.cs` (A1–A17), `RecorderControllerSettingsTests.cs` (B1–B12, including B10b), `RecorderControllerStatusTests.cs` (C1–C21, including C10b, C14b, C18b), `RecorderControllerControlsTests.cs` (D1–D9), `RecorderControllerCommandTests.cs` (E1–E16 including E1b, E5b, E6b, E8c, E8d, E16b, plus the review tests R1_x4, R2_x2, R7), `LevelMeterTests.cs` (M1–M3), `RecorderControllerSessionTests.cs` (W1–W3, real `RecordingSession`).
- [x] Task 3: Seams (AC: 1, 7)
  - [x] `src/MyScreenRecorder.Core/Platform/IScreenCatalog.cs` (CREATE): architecture §4 verbatim plus a `///` (D7). Text of the docstring: `<summary>Lists the monitors of the desktop. Ordered by <c>Left</c>, then <c>Top</c>; numbered from 1 in that order; exactly one entry has <c>IsPrimary</c> true when the platform has a primary monitor. Called on the UI thread, once per controller. May throw when the display cannot be queried; an empty list is a failure the caller reports.</summary>`
  - [x] `src/MyScreenRecorder.Core/Recording/IRecordingSession.cs` (CREATE) with the members of Decision Record (listed in Contract Guardrail). `src/MyScreenRecorder.Core/Recording/RecordingSession.cs` (UPDATE): `public sealed class RecordingSession : IRecordingSession, IDisposable`; nothing else changes (`git diff` shows one line).
- [x] Task 4: Value types (AC: 2, 3, 4)
  - [x] `src/MyScreenRecorder.Core/Controller/OverlayMode.cs`, `StatusLine.cs` (record `StatusLine` + enum `StatusSeverity`), `ControlState.cs` (CREATE). Shapes in Contract Guardrail. No docstrings (D7).
  - [x] `src/MyScreenRecorder.Core/Controller/LevelMeter.cs` (CREATE): `internal static class LevelMeter { internal static float Next(float peak, float previous) => Math.Max(peak, previous * 0.85f); }`.
- [x] Task 5: `RecorderController` (AC: 1–6) — `src/MyScreenRecorder.Core/Controller/RecorderController.cs` (CREATE). Follow the reference map (Dev Notes) line by line; keep the order of side effects of `MainWindow` (apply mic/speaker, then save; message set, then refresh).
  - [x] Constructor and initialization (A-cases), `Targets`/`SourceLabels` build exactly as `RebuildTargets` (`:152-158`): `CaptureTarget.ForMonitor` per monitor, then `ForAll(monitors)`, then the region if any.
  - [x] `Select*`/`SetRegion` with the idle-and-not-busy guard, `ApplyMic/ApplySpeaker`, `SaveSettings` mapping (B-cases).
  - [x] Every command (start, pause/resume, stop, cancel) sets `busy` and calls `Refresh()` **before** it awaits the session (`:226,248,285-287,309-310`: the disabled buttons); groups D2, D5, D7, D9 read that state while the session call is pending.
  - [x] `Refresh()` (controls, overlay, status, then `Changed`), `UpdateStatus()`, `TickMeters()`, commands, `PrepareCloseAsync`, `LastFile`, `ReportError`, `Dispose` (unsubscribe only; the controller does not dispose the session, engine or locator — the composition root owns them, `:446-447`).
- [x] Task 6: Make the tests green, then the guard checks (AC: all)
  - [x] `dotnet test --project tests/MyScreenRecorder.Core.Tests` after each group; fix the code, never the expectation, unless the expectation contradicts its named source (then stop and ask).
  - [x] Guard checks (Testing Requirements → Guard checks) on a copy, one mutation at a time, each with `--no-incremental` and the full test run; record the failing test and message of each in the Dev Agent Record.
- [x] Task 7: Docs (AC: 7)
  - [x] `CLAUDE.md` (UPDATE), Architecture section: Core also holds `IScreenCatalog`, `IRecordingSession`, `RecorderController` (+ `ControlState`, `StatusLine`/`StatusSeverity`, `OverlayMode`) in `MyScreenRecorder.Controller`; state that `MainWindow` does not use it yet (backlog 5-1) and that `PlatformServices` does not carry the screen catalog.
  - [x] Do not touch `README.md` (nothing user-facing changes).
- [x] Task 8: Verification and handover (AC: 7)
  - [x] `dotnet build MyScreenRecorder.sln -c Debug` → 0 errors, no new warnings (record the warning count before and after).
  - [x] `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` three times in a row → all pass.
  - [x] `git diff --stat 3d2d000 -- src/MyScreenRecorder/` prints nothing (AC7).
  - [x] Comment check: `git diff -U0 | grep -E '^\+\s*(#|//|--)'` and the comments of all new files; remove each that restates the code (global rule, D7). Only the interface docstring of `IScreenCatalog` is required.
  - [x] Walk the Definition of Done (`docs/planning/epics-linux-build.md` §3) item by item in the handover: 1 ACs with test names; 2 build; 3 tests; 4 guard checks; 5 comments; 6 `CLAUDE.md`; 7 not applicable (no WPF/Windows file changes, proven by the empty diff stat); 8 not applicable (no Linux smoke item); 9 nothing committed.
  - [x] Error log: every error found while implementing (own analysis, test, lint, review) becomes an entry via `python3 /home/timmy/.agents/skills/learn-from-review/scripts/error_log.py add '<JSON>'` (global rule).
  - [x] Nothing is staged, committed or pushed without the user's explicit permission.

## Dev Notes

### Boundaries (derived from the documented ones; no boundary is moved)

| Boundary | Status | Holds for this story |
|---|---|---|
| `src/MyScreenRecorder.Core` (`net10.0`, BCL + `NAudio.Core`) | documented (architecture §3.2, §3.3) | New: `IScreenCatalog` (`Platform`), `IRecordingSession` (`Recording`), `RecorderController`, `ControlState`, `StatusLine`, `StatusSeverity`, `OverlayMode`, `LevelMeter` (`Controller`). Existing types it uses: `AudioEngine`, `FfmpegLocator`, `Settings`, `CaptureTarget`, `MonitorInfo`, `Quality`, `IAudioDeviceCatalog`, `AudioDeviceInfo`, `RecordingState`, `SessionStats` (`AppPaths.OutputFolder` is read by the view, not by the controller). |
| `src/MyScreenRecorder` (WPF) | documented | **Untouched.** `MainWindow`, `OverlayManager`, `PreviewGrabber`, `ScreenInfo`, `Platform/Windows/*` stay as they are. |
| `src/MyScreenRecorder.Platform.Linux`, `src/MyScreenRecorder.Linux`, tests for them | documented, do not exist yet | Not created here. Their future contract with this story: the Linux UI implements `IScreenCatalog` (Avalonia `Screens`, architecture §3.2), builds the controller with a `marshal` over `Dispatcher.UIThread`, owns the two timers, the overlay manager, the preview and the dialogs. |

Dependency direction after the story: `Controller → {Recording, Audio, Capture, Platform (IScreenCatalog, IAudioDeviceCatalog), root (Settings, AppPaths)}`; nothing in Core depends on `Controller`; no UI framework, no OS API, no `OperatingSystem.Is*` in Core (architecture §3.3). The `Controller → Recording` and `Recording → Platform` edges already exist; `Controller → Platform` is new but follows the rule "Core code uses platform interfaces". `Platform` references `Capture` (`MonitorInfo`) and `Audio` (`AudioDeviceInfo`), as `IAudioDeviceCatalog` already does.

### Technical Requirements
- Core may reference only the BCL and `NAudio.Core` 2.2.1. `System.Drawing.Rectangle` is already used by `CaptureTarget` (BCL `System.Drawing.Primitives`). `ImplicitUsings` disabled: explicit `using`s. `Nullable` enabled.
- The controller's public members are the contract below. A member that seems missing → stop and ask.
- `MainWindow.xaml.cs` is not edited, moved or deleted. The controller is not referenced from the WPF project.

### ⚠️ Trap 1 — Order of side effects in the start path (AC4)
The pre-start `Refresh()` is the whole point of AC4. If the implementation sets `starting` but raises `Changed` only after `await session.StartAsync(...)`, the test that holds the session call pending fails; that is intended. Also clear `starting` in `finally`, or a failed start leaves the badges hidden forever.

### ⚠️ Trap 2 — `Select*` and the settings write order
`MicCycler.Changed` does `ApplyMic()` then `SaveSettings()` (`:99-103`). The save reads the *new* index, so `SelectMic` must set the index, apply to the engine, then save. A test records the engine call and the save in one list to hold the order (B6).

### ⚠️ Trap 3 — Marshalled callbacks run later
With a queueing `marshal`, a callback that arrives while a command is in flight runs after the command's own `Refresh()`. Do not read session state in the callback closure at the time of the event; read it when the posted action runs (as `UpdateUi` reads `_session.State`, `:358`). A test raises the event, changes the fake state, then drains the queue and expects the new state (E13).

### ⚠️ Trap 4 — Real time in the meters
`AudioSource.Peak` is `0` 300 ms after the last data (`AudioSource.cs` `PeakTimeoutMs`, 1-3). The controller test raises data and calls `TickMeters()` immediately (inside the window); the decay arithmetic is tested on `LevelMeter.Next` directly. No test sleeps.

### ⚠️ Trap 5 — Fake session semantics
`FakeRecordingSession` mirrors the real transitions only when `AutoTransitions` is true (default): `StartAsync` → `Recording`; `PauseAsync` → `Paused`; `ResumeAsync` → `Recording`; `StopAsync` → `Finalizing` then `Idle`; `CancelAsync` → `Finalizing` then `Idle`; each assignment raises `StateChanged`. With a `Gate` on `StopAsync`/`CancelAsync` the fake sets `Finalizing` (and raises) first, then awaits the gate, then sets `Idle`; with a `Gate` on `StartAsync`/`PauseAsync`/`ResumeAsync` the state changes only after the gate completes. A test that needs a state without the transition (e.g. `Paused` at construction) sets `State` and calls `RaiseStateChanged()`. A gated call (`Gate = new TaskCompletionSource(...)`) completes only when the test completes the gate; create gates with `TaskCreationOptions.RunContinuationsAsynchronously`. Awaiting `controller.StartAsync()` before completing the gate would deadlock the test: keep the `Task` and `await` it after the assertions.

### ⚠️ Trap 6 — Culture in the status tests
`CurrentCulture` flows with the test's execution context. Tests that expect `.` set `CultureInfo.CurrentCulture = CultureInfo.InvariantCulture` at the start of the test; the comma case (C16) sets a writable clone of `InvariantCulture` with `NumberDecimalSeparator = ","` (no ICU data needed; probed). Restore in `Dispose`.

### ⚠️ Trap 7 — Temp files for `LastFile`
`LastFile` checks `File.Exists`. Tests create a real file under a `Directory.CreateTempSubdirectory` and delete the directory in `Dispose`. The stop-flow tests use a path in that directory as the fake session's `StopAsync` result.

### ⚠️ Trap 8 — `AudioEngine` in controller tests is the real class
`AudioEngine` is `sealed` (no interface; 1-3). Use `new AudioEngine(FakeAudioCaptureFactory)` and dispose it in the test's `Dispose`. `engine.Mic` / `engine.Speaker` being null/non-null, and `FakeAudioCaptureFactory.Calls` `(DeviceId, Loopback)`, are the observations. `SetSpeaker` opens with `loopback: true`, `SetMic` with `false` (`AudioEngine.cs:57,72`).

### ⚠️ Trap 9 — Do not widen the WPF project's surface
Do not make the controller or its types visible to `MainWindow` by changing its usings; the WPF project does not reference these types. If `dotnet build` warns CS0436 or CS0104 anywhere, a type name collides with the WPF project: stop and ask (`OverlayState` is the known candidate, hence `OverlayMode`).

### Reference map: what the controller takes from `MainWindow.xaml.cs` (kept / dropped, line by line)

| `MainWindow.xaml.cs` | What it does | In the controller |
|---|---|---|
| `:22` `NoneLabel = "NONE"` | first entry of both device lists | kept: `RecorderController.NoneLabel` |
| `:24-25,62-63` `_settings`, `_firstRun`, `Settings.Load()` | settings and first-run flag | constructor parameters `settings`, `firstRun`; computed by the composition root before `Load()` |
| `:26-37` platform services, locator, audio, session, overlay, preview | composition | parameters `session`, `audio`, `devices`, `screens`, `ffmpeg`; overlay and preview are views and are dropped (their inputs are `Overlay` and `Target`) |
| `:38-39` meter 50 ms, stats 500 ms timers | UI timers | dropped; constants `MeterIntervalMs`, `StatusIntervalMs` |
| `:40-41` quality arrays | `Enum.GetValues<VideoQuality>()`, `<AudioQuality>()` | kept (labels via `Quality.Label`) |
| `:43-55` state fields | monitors, targets, devices, region, target, `_lastFinal`, `_message`, `_busy`, levels | kept as private fields; `_ready` and `_closing` dropped (Decision Record, Close) |
| `:66-71` `StateChanged`/`Faulted` → `Dispatcher.BeginInvoke` | marshal | kept via the `marshal` parameter |
| `:72-76` timer wiring, `Tips` | UI | dropped |
| `:77` `Loaded += Initialize` | init | constructor |
| `:82-84` monitors, `RebuildTargets(InitialTargetIndex())`, `_target` | initial target | kept |
| `:86-87` quality cyclers `SetItems(labels, IndexOf(setting))` | initial quality indices (cycler clamps) | kept, with `Math.Clamp(index, 0, count − 1)` |
| `:89-94` device lists, `InitialDeviceIndex`, `ApplyMic`, `ApplySpeaker` | initial devices | kept |
| `:96-108` handlers | selection → apply → save | become `Select*` |
| `:110-113` ffmpeg check | `ERROR: ffmpeg not found` | kept |
| `:115-117` layout, work-area clamp | UI | dropped |
| `:119-125` `_ready`, preview start, timers start, `_overlay.Update(Idle)`, `UpdateUi` | start of views | `_ready` dropped; overlay/preview/timers belong to the view; `UpdateUi` → initial `Refresh()` without raising `Changed` (no subscriber yet) |
| `:128-135` `InitialTargetIndex` | All / monitor number / primary / 0 | kept |
| `:137-150` `DeviceNames`, `InitialDeviceIndex` | NONE + names; first run → default, else saved; unknown → 0 | kept |
| `:152-158` `RebuildTargets` | monitors, ALL, region | kept; uses the construction-time snapshot (Decision Record) |
| `:160-166` `OnSourceChanged` | target, preview target, overlay Idle, save | target + save kept; preview and overlay are views of `Target`/`Overlay` |
| `:168-178` `ApplyMic/Speaker` | index 0 → `null` else id | kept |
| `:180-193` `SaveSettings` | mapping | kept (table below) |
| `:195-201` `TickMeters` | smoothing | kept; `MicMeter.SetLevel` dropped |
| `:203-220` `RegionButton_Click` | selector UI, `ForRegion`, `RebuildTargets(count+1)`, `OnSourceChanged`, `UpdateUi` | selector UI and `RegionButton.IsEnabled=false` dropped (the view disables its button while its selector runs and calls `SetRegion` with the result); the rest is `SetRegion` |
| `:222-242` `RecButton_Click` | start | kept, plus the pre-start refresh (AC4) |
| `:244-263` `PauseButton_Click` | pause/resume | kept |
| `:265-268,305-333` stop | `SAVING...`, `SAVED: `, errors | kept |
| `:270-303` cancel | confirm, re-check, `CANCELING...`, result | kept; the `MessageBox` is the `confirm` delegate |
| `:335-353` `OpenButton_Click` | explorer | process start dropped; `LastFile`, `ReportError` |
| `:355-382` `UpdateUi` | enablement, pause label, overlay state, status | kept (`ControlState`, `Overlay`, `Status`) |
| `:384-413` `UpdateStatus` | text and brush | text kept; brush → `StatusSeverity` |
| `:415-419` `OnStateChanged` | hide overlays when minimized | dropped (view → overlay manager, 3-4) |
| `:421-437` `OnClosing` | stop on close | `PrepareCloseAsync` |
| `:439-448` `OnClosed` | stop timers, dispose preview, overlay, session, audio | dropped; the composition root disposes what it created; `Dispose()` of the controller only unsubscribes |

### Contract Guardrail

Public shape (namespace `MyScreenRecorder.Controller` unless noted; members are the contract):

```csharp
// MyScreenRecorder.Platform
public interface IScreenCatalog
{
    IReadOnlyList<MonitorInfo> GetMonitors();   // ordered by Left, then Top; numbered from 1 (architecture §4)
}

// MyScreenRecorder.Recording
public interface IRecordingSession
{
    event Action? StateChanged;
    event Action<string>? Faulted;
    RecordingState State { get; }
    string? LastError { get; }
    SessionStats Stats { get; }
    Task StartAsync(CaptureTarget target, VideoQuality vq, AudioQuality aq);
    Task PauseAsync();
    Task ResumeAsync();
    Task<string?> StopAsync();
    Task CancelAsync();
}

public enum OverlayMode { Idle, Recording, Paused }
public enum StatusSeverity { Normal, Recording, Warning, Success, Error }
public sealed record StatusLine(string Text, StatusSeverity Severity);
public sealed record ControlState(
    bool SourceEnabled, bool VideoQualityEnabled, bool MicEnabled, bool SpeakerEnabled,
    bool AudioQualityEnabled, bool RegionEnabled, bool RecEnabled,
    bool PauseEnabled, string PauseLabel, bool StopEnabled, bool CancelEnabled);

public sealed class RecorderController : IDisposable
{
    public const int MeterIntervalMs = 50;
    public const int StatusIntervalMs = 500;
    public const string NoneLabel = "NONE";

    public RecorderController(
        IRecordingSession session, AudioEngine audio, IAudioDeviceCatalog devices, IScreenCatalog screens,
        FfmpegLocator ffmpeg, Settings settings, bool firstRun, Action saveSettings, Action<Action> marshal);

    public event Action? Changed;

    public IReadOnlyList<CaptureTarget> Targets { get; }
    public IReadOnlyList<string> SourceLabels { get; }       // Targets[i].Label
    public int SourceIndex { get; }
    public CaptureTarget Target { get; }                      // Targets[SourceIndex]
    public IReadOnlyList<string> VideoQualityLabels { get; }  // Quality.Label of P480, P720, P1080
    public int VideoQualityIndex { get; }
    public IReadOnlyList<string> AudioQualityLabels { get; }  // Aac128, Aac256, Wav
    public int AudioQualityIndex { get; }
    public IReadOnlyList<string> MicNames { get; }            // NONE, then device names
    public int MicIndex { get; }
    public IReadOnlyList<string> SpeakerNames { get; }
    public int SpeakerIndex { get; }
    public ControlState Controls { get; }
    public StatusLine Status { get; }
    public OverlayMode Overlay { get; }                       // drawn for Target
    public float MicLevel { get; }
    public float SpeakerLevel { get; }
    public string? LastFile { get; }                          // last saved file if it still exists, else null

    public void SelectSource(int index);
    public void SelectVideoQuality(int index);
    public void SelectAudioQuality(int index);
    public void SelectMic(int index);
    public void SelectSpeaker(int index);
    public void SetRegion(System.Drawing.Rectangle bounds, int monitorNumber);

    public Task StartAsync();
    public Task PauseOrResumeAsync();
    public Task StopAsync();
    public Task CancelAsync(Func<Task<bool>> confirm);
    public Task<bool> PrepareCloseAsync();

    public void UpdateStatus();
    public void TickMeters();
    public void ReportError(string message);
    public void Dispose();
}
```

- `ControlState` is `MainWindow.xaml.cs:362-372`; there is no member for OPEN (never touched by `UpdateUi`, always enabled) and none for the preview. `PauseLabel` is `"> RESUME"` when the session state is `Paused`, else `"|| PAUSE"` (`:370`).
- Expected values: status strings and severities from `:396-408`, `:229-235`, `:286-296`, `:309-326`, `:350`, `:112` and `AudioEngine`/`RecordingSession` texts; control values from `:362-372`; indices from `:128-150`; settings from `:180-193`; labels from `CaptureTarget.cs` and `Quality.cs`; the geometry of the fixture from the epic (1-1 AC3, L1). The decisions of this story are the source for everything marked "decision" in the tables.
- Probes run during planning (scratchpad `/tmp/claude-1000/-home-timmy-Desktop-apps-MyScreenRecorder/69c422eb-cdba-4c87-99b5-256991baa92f/scratchpad/plan-1-4/probe`, .NET SDK 10.0.403, Core built at `3d2d000`): `bytes / 1048576.0` formatted `F1` gave `0 → 0.0`, `52428 → 0.0`, `52429 → 0.1`, `1048576 → 1.0`, `1310720 → 1.2`, `1572864 → 1.5`, `1835008 → 1.8`, `1074266112 → 1024.5` (note `1310720` = 1.25 MB prints `1.2`, `1835008` = 1.75 MB prints `1.8`: exact ties round to even); `TimeSpan` `hh\:mm\:ss` gave `0:00:00 → 00:00:00`, `1 h 2 m 3.999 s → 01:02:03`, `25 h 1 s → 01:00:01`, `0.999 s → 00:00:00`; a writable clone of `CultureInfo.InvariantCulture` is not read-only (`IsReadOnly == False`) and with `NumberDecimalSeparator = ","` as `CurrentCulture` `1572864 / 1048576.0` formats `F1` as `1,5` while the `TimeSpan` text is unaffected; `Settings` JSON deserialization with `"VideoQuality": 99` succeeds (value `99`), with `"AudioQuality": "Bogus"` throws `JsonException` (caught by `Settings.Load`, which then returns defaults). Reproduce the probe if in doubt.
- Hand-off to story 3-1: catch the zero-monitor `InvalidOperationException` at startup; create the controller after `AudioEngine`, session, locator, settings (`firstRun` computed before `Settings.Load()`); run `UpdateStatus` every 500 ms and `TickMeters` every 50 ms on the UI thread; draw `Overlay`/`Target` through the overlay manager; call `SetRegion` only when the selector returns a rectangle; implement `confirm` with the Yes/No dialog (default No); call `PrepareCloseAsync` from the window's closing handler.
- Out of scope (do not touch): `MainWindow.xaml(.cs)` and anything under `src/MyScreenRecorder/` (Windows adoption is backlog 5-1; a Windows `IScreenCatalog` is created there); Linux projects, Avalonia, `IScreenCatalog` implementations (2-1, 3-1); preview, region selector UI, overlay drawing (3-2, 3-3, 3-4); hot-plug refresh of monitors; showing `AudioEngine.LastError` (existing quirk 1); making `RecordingSession.LastError` texts red (existing quirk); `PlatformServices` (unchanged); `Quality`, `CaptureTarget`, `Settings`, `AppPaths` (unchanged). Deferred from 1-1/1-2/1-3 stays deferred: a region touching a monitor edge can yield a zero-width target; `RecordingSession` verification gaps; `AudioSource.OnStopped` redundancy.

### File Structure Requirements
- `src/MyScreenRecorder.Core/Platform/IScreenCatalog.cs` (CREATE)
- `src/MyScreenRecorder.Core/Recording/IRecordingSession.cs` (CREATE)
- `src/MyScreenRecorder.Core/Recording/RecordingSession.cs` (UPDATE: class declaration only)
- `src/MyScreenRecorder.Core/Controller/RecorderController.cs`, `ControlState.cs`, `StatusLine.cs`, `OverlayMode.cs`, `LevelMeter.cs` (CREATE)
- `tests/MyScreenRecorder.Core.Tests/FakeRecordingSession.cs`, `FakeScreenCatalog.cs`, `FakeAudioDeviceCatalog.cs`, `ControllerFixture.cs`, `RecorderControllerInitTests.cs`, `RecorderControllerSettingsTests.cs`, `RecorderControllerStatusTests.cs`, `RecorderControllerControlsTests.cs`, `RecorderControllerCommandTests.cs`, `RecorderControllerSessionTests.cs`, `LevelMeterTests.cs` (CREATE)
- `CLAUDE.md` (UPDATE)
- `src/MyScreenRecorder/**`, `README.md`, `Settings.cs`, `AppPaths.cs`, `Quality.cs`, `CaptureTarget.cs`, `PlatformServices.cs` are **not** changed.

### Testing Requirements

#### Test support (exact shapes)

- `FakeScreenCatalog : IScreenCatalog` — ctor `(params MonitorInfo[] monitors)`; `GetMonitors()` returns them; `Calls` counts invocations (the snapshot decision is held by `Calls == 1` after construction and after `SetRegion`). Static `Standard()` returns the fixture: `new MonitorInfo(1, new Rectangle(0, 116, 1920, 1080), false)`, `new MonitorInfo(2, new Rectangle(1920, 0, 2560, 1440), true)`, `new MonitorInfo(3, new Rectangle(4480, 143, 1920, 1080), false)`.
- `FakeAudioDeviceCatalog : IAudioDeviceCatalog` — settable `Inputs`, `Outputs`, `DefaultInput`, `DefaultOutput`. Standard fixture: inputs `(in-1, "Mic One")`, `(in-2, "Mic Two")`; outputs `(out-1, "Speakers")`, `(out-2, "Headset")`; default input `in-2`, default output `out-1`. (The existing `EmptyAudioDeviceCatalog` in `TestPlatform.cs` stays for the pipeline tests.)
- `FakeRecordingSession : IRecordingSession` — properties `State` (settable; setter does not raise), `LastError`, `Stats`; `AutoTransitions` (default true, Trap 5); `Log` (`List<string>` shared with the test; entries `session.Start`, `session.Pause`, `session.Resume`, `session.Stop`, `session.Cancel`); `StartArgs` (`CaptureTarget, VideoQuality, AudioQuality` of the last call); per method a `TaskCompletionSource?` gate (`StartGate`, `PauseGate`, `ResumeGate`, `StopGate`, `CancelGate`; when set the method awaits it before transitioning) and an `Exception?` (`StartThrow`, `PauseThrow`, `ResumeThrow`, `StopThrow`, `CancelThrow`; thrown after the gate); `StopResult` (`string?`); `RaiseStateChanged()`, `RaiseFaulted(string)`. Each method logs on entry, before awaiting the gate.
- `ControllerFixture` — builds a controller over the fakes and the real `AudioEngine(new FakeAudioCaptureFactory())`: parameters `Settings settings`, `bool firstRun`, `bool ffmpegFound` (true → `FfmpegLocator` over a temp dir containing an empty file `ffmpeg`, via the `internal` constructor `new FfmpegLocator(new FfmpegLocatorOptions("ffmpeg", null, null, <unused dir>), <temp dir>, _ => null)`; false → an empty temp dir, `ResourceAssembly = null`, environment lookup returns `null`, so `Resolve()` is `null`, 1-2 AC4), `Action<Action>? marshal` (default `a => a()` for tests on the fake session; a `bool queueing` option (no `marshal` parameter) instead installs a marshal over a `ConcurrentQueue<Action>` and exposes `Drain()`, which runs and empties the queue on the calling thread), monitors/devices overrides. Exposes `Saves` (a `List<SavedSettings>`; `SavedSettings(TargetKind, MonitorNumber, MicId, SpeakerId, VideoQuality, AudioQuality)` is a record of **copies** of the six fields taken inside the `saveSettings` delegate, because `Settings` is mutated in place), `CaptureCallsAtSave`, `ChangedCount` (every `Changed` raised by the controller), `Drain()`, `Log` (shared list), `Controller`, `Session`, `Audio`, `Captures`, and the shared helpers `static Gate()` (a `TaskCompletionSource` with `RunContinuationsAsynchronously`) and `SetState(RecordingState)` (sets `Session.State` and raises `StateChanged`). `Dispose` disposes the controller, the engine and deletes the temp dirs.
- Culture and temp-file helpers as in Trap 6/7.

#### Group A — initial selection (`RecorderControllerInitTests`; fixture monitors and devices unless stated; `Settings` defaults are `TargetKind "Monitor"`, `MonitorNumber 1`, `P720`, `Aac256`)

| # | Input | Expected (source) |
|---|---|---|
| A1 | `TargetKind "Monitor"`, `MonitorNumber 3` | `SourceIndex 2`; `Target.Label` `MON 3 1920x1080`; `SourceLabels` = `["MON 1 1920x1080","MON 2 2560x1440","MON 3 1920x1080","ALL 6400x1440"]` (`:130-132,154-156`; union per 1-1 AC3 / epic L1) |
| A2 | `TargetKind "All"`, `MonitorNumber 3` | `SourceIndex 3`; `Target.Kind == All`; `Target.Bounds == (0,0,6400,1440)` (`:130`) |
| A3 | `TargetKind "Monitor"`, `MonitorNumber 7` | `SourceIndex 1` (primary is monitor 2; `:133-134`) |
| A4 | as A3 with no monitor primary | `SourceIndex 0` (`:134` `Math.Max(-1, 0)`) |
| A5 | default settings | `SourceIndex 0` |
| A6 | `TargetKind "all"` (lower case), `MonitorNumber 2` | `SourceIndex 1` (ordinal compare, `:130`: not `All`, so the monitor path) |
| A7 | catalog returns no monitors | constructor throws `InvalidOperationException` with message `No monitors found.` (decision) |
| A8 | `firstRun true`, `MicId "in-1"`, `SpeakerId "out-2"` | `MicIndex 2`, `SpeakerIndex 1` (defaults `in-2`, `out-1`; saved ids ignored, `:146`); `MicNames` = `["NONE","Mic One","Mic Two"]`, `SpeakerNames` = `["NONE","Speakers","Headset"]`; `Captures.Calls` = `[("in-2", false), ("out-1", true)]` in that order (`:93-94`); `Audio.Mic` and `Audio.Speaker` not null |
| A9 | `firstRun false`, `MicId "in-1"`, `SpeakerId "out-2"` | `MicIndex 1`, `SpeakerIndex 2`; `Captures.Calls` = `[("in-1", false), ("out-2", true)]` |
| A10 | `firstRun false`, `MicId "gone"`, `SpeakerId "out-1"` | `MicIndex 0`; `Audio.Mic` null; `Captures.Calls` = `[("out-1", true)]` (`:148-149`) |
| A11 | `firstRun false`, both ids `null` | `MicIndex 0`, `SpeakerIndex 0`; `Captures.Calls` empty (`:147`) |
| A12 | `firstRun true`, catalog defaults `null` / `"unknown"` | both indices 0 (`:147,149`); `Captures.Calls` empty |
| A13 | no devices, `firstRun true` | `MicNames` = `["NONE"]`, index 0 |
| A14 | `P1080`, `Wav` | `VideoQualityLabels` = `["480P","720P","1080P"]`, `AudioQualityLabels` = `["AAC 128K","AAC 256K","WAV"]`, indices 2 and 2; defaults → 1 and 1 (`:40-41,86-87`, `Quality.Label`) |
| A15 | `(VideoQuality)99`, `(AudioQuality)99` | indices 0 and 0 (cycler clamp, `Cycler.cs` `SetItems`; probe: such a value loads) |
| A16 | ffmpeg not found / found | not found: `Status` = `("ERROR: ffmpeg not found", Error)`, `Controls.RecEnabled false`; found: `Status` = `("READY", Normal)`, `RecEnabled true` (`:110-113,368,406`) |
| A17 | any of the above | `Saves.Count == 0` (no save during construction, `:96-108` subscribe after init); `Overlay == Idle`; `Controller.Target` is `Targets[SourceIndex]`; `FakeScreenCatalog.Calls == 1` |

#### Group B — settings save mapping and selections (`RecorderControllerSettingsTests`)

Mapping (`:180-193`): `TargetKind = Target.Kind == All ? "All" : "Monitor"`; `MonitorNumber = Target.MonitorNumbers[0]` when `Kind != All`, otherwise unchanged (the `MonitorNumbers.Length > 0` condition of `:184` is dropped as unreachable, see Decision Record); `MicId = MicIndex == 0 ? null : inputs[MicIndex − 1].Id`; `SpeakerId` likewise; `VideoQuality`, `AudioQuality` from the indices. Saved when and only when: `SelectSource`, `SetRegion`, `SelectVideoQuality`, `SelectAudioQuality`, `SelectMic`, `SelectSpeaker` complete (`:97-108,165,213`). All six fields are written on every save. Start state for B: `firstRun false`, settings `Monitor`/`1`/`P720`/`Aac256`, ids `null`.

| # | Action | Expected `Saves` (count and last copy) |
|---|---|---|
| B1 | `SelectSource(2)` | 1 save: `("Monitor", 3, null, null, P720, Aac256)`; `Target.Label` `MON 3 1920x1080`; one `Changed` |
| B2 | start state `MonitorNumber 2` (initial `SourceIndex 1`), then `SelectSource(3)` (ALL) | 1 save: `("All", 2, …)` — `MonitorNumber` keeps 2 (`:184`); from monitor 1 the `Kind != All` guard would be invisible, because ALL lists monitor 1 first |
| B3 | `SetRegion(new Rectangle(2000, 100, 801, 601), 2)` | `Targets.Count == 5`; `SourceIndex 4`; `Target.Label` `REGION 800x600 @MON 2`; `Target.Kind == Region`; 1 save: `("Monitor", 2, …)` (`ForRegion` clamps to monitor 2 `(1920,0,2560,1440)`, even size; `:211-213,183-187`); `FakeScreenCatalog.Calls == 1` |
| B4 | B3 then `SetRegion(new Rectangle(0, 116, 400, 300), 1)` | `Targets.Count == 5` (one region slot); `Target.Label` `REGION 400x300 @MON 1`; second save `("Monitor", 1, …)` |
| B5 | B3 then `SelectSource(0)` then `SelectSource(4)` | `Target` is the same region object as after B3 (region stays in the list); saves 3 |
| B6 | `SelectMic(2)` | `Captures.Calls` last `("in-2", false)`; then 1 save with `MicId "in-2"`; the engine call is logged **before** the save in the shared `Log` (Trap 2); then `SelectMic(0)` → `Audio.Mic` null, the first capture's `Disposed == 1`, save `MicId null` |
| B7 | `SelectSpeaker(2)` | `Changed` raised exactly once; `Captures.Calls` last `("out-2", true)`; save `SpeakerId "out-2"` |
| B8 | `SelectVideoQuality(2)`; `SelectAudioQuality(0)` | saves: `P1080` with `Aac256`, then `P1080` with `Aac128`; `VideoQualityIndex 2`, `AudioQualityIndex 0`; `ChangedCount` 1 after the video call and 2 after the audio call |
| B9 | `StartAsync`, `PauseOrResumeAsync`, `PauseOrResumeAsync`, `StopAsync`, then another start and `CancelAsync(() => true)` | `Saves.Count` unchanged |
| B10 | `SelectMic(1)` while `Session.State == Recording`; and while a gated `StartAsync` is pending (state still `Idle`, busy) | no new capture opened, `MicIndex` unchanged, `Saves.Count` unchanged, `Changed` raised once for each call; same for `SelectSource`, `SelectVideoQuality`, `SelectAudioQuality`, `SelectSpeaker`, `SetRegion` (decision) |
| B10b | **Live guard.** Queueing `marshal`. Set `Session.State = Recording`, `Session.RaiseStateChanged()`, do **not** drain; then `SelectMic(1)` (and `SelectSource(2)`) | no capture opened (`Captures.Calls` unchanged), `MicIndex`/`SourceIndex` unchanged, `Saves.Count` unchanged (guard reads `Session.State`, not the stale `Controls`) |
| B11 | `SelectSource(-1)`, `SelectSource(4)`, `SelectMic(3)`, `SelectVideoQuality(3)`, `SelectAudioQuality(3)`, `SelectSpeaker(3)` on the standard fixture | `ArgumentOutOfRangeException` each; no save |
| B12 | **Same index.** On the standard fixture (initial `SourceIndex 0`, `MicIndex 0` with `firstRun false`, ids null, `VideoQualityIndex 1`, `AudioQualityIndex 1`): `SelectSource(0)`, `SelectVideoQuality(1)`, `SelectAudioQuality(1)`, `SelectMic(0)`, `SelectSpeaker(0)`; then `SelectMic(2)` followed by `SelectMic(2)` | the first five: `Captures.Calls` empty, `Saves.Count 0`, `Changed` count 0 (`Cycler.cs:47-52`: the cycler never raises without moving; decision); the repeated `SelectMic(2)`: one capture opened, one save, one `Changed` in total |

#### Group C — status text (`RecorderControllerStatusTests`; culture set to `InvariantCulture`, Trap 6). Rows C1–C9 set `Session.State`, `Session.Stats = new SessionStats(duration, bytes, label)`, call `UpdateStatus()` and read `Status`.

| # | State, duration, bytes, quality label | Expected `Text` | Severity (source) |
|---|---|---|---|
| C1 | `Idle`, no message | `READY` | Normal (`:406`) |
| C2 | `Recording`, 0:00:00, 0, `720P` | `* REC  00:00:00  0.0 MB  720P` | Recording (`:396-398`) |
| C3 | `Recording`, 1 h 2 m 3.999 s, 1 572 864, `720P / AAC 256K` | `* REC  01:02:03  1.5 MB  720P / AAC 256K` | Recording |
| C4 | `Paused`, same stats as C3 | `\|\| PAUSED  01:02:03  1.5 MB  720P / AAC 256K` | Warning (`:396,398`) |
| C5 | `Recording`, bytes 52 428 / 52 429 / 1 310 720 / 1 835 008 / 1 074 266 112 | `0.0 MB` / `0.1 MB` / `1.2 MB` / `1.8 MB` / `1024.5 MB` in the text | Recording (probe values) |
| C6 | `Recording`, duration 25 h 1 s; duration 0.999 s | `01:00:01`; `00:00:00` in the text | Recording (probe: hours wrap, fractions truncate) |
| C7 | `Finalizing`, with a pending message in the controller | `SAVING...` | Warning (`:401-404`) |
| C8 | `Recording` after a completed stop produced `SAVED: …` earlier, then a new start | the `* REC` text (message ignored in non-idle states, `:392-400`) | Recording |
| C9 | `Idle` after `StartAsync` throws `InvalidOperationException("ffmpeg not found")` | `ERROR: ffmpeg not found` | Error (`:235,407`) |

Flow rows (drive the controller, session `AutoTransitions` true, `Session.StopResult` etc. set per row; the status is read after the command completed and `State == Idle`):

| # | Flow | Expected `Status` |
|---|---|---|
| C10 | `StopAsync` with `StopResult = <dir>/REC_2026-11-03_10-00-00.mp4` | `("SAVED: REC_2026-11-03_10-00-00.mp4", Success)` (`:317,408`); `LastFile` = that path when the file exists, `null` when it does not (`:339`) |
| C11 | `StopAsync`, `StopResult = null`, `LastError = "Finalize failed: boom Parts kept in /p"` | `("Finalize failed: boom Parts kept in /p", Normal)` (`:321`; no `ERROR` prefix) |
| C12 | `StopAsync`, `StopResult = null`, `LastError = null` | `("ERROR: save failed", Error)` (`:321`) |
| C13 | `StopAsync`, session throws `InvalidOperationException("boom")` | `("ERROR: boom", Error)` (`:326`) |
| C14 | `CancelAsync(() => Task.FromResult(true))`, `LastError = null` | `("CANCELED: recording discarded", Normal)` (`:292`); `LastFile` null |
| C15 | `CancelAsync`, `LastError = "Could not delete /p"` | `("Could not delete /p", Normal)` (`:292`); cancel throwing `("boom")` → `("ERROR: boom", Error)` (`:296`); pause throwing → `("ERROR: boom", Error)` (`:256`) |
| C16 | culture clone with `,`; row C3 | text `* REC  01:02:03  1,5 MB  720P / AAC 256K` (`:397` has no culture argument; probe) |
| C17 | `RaiseFaulted("ERROR: see ffmpeg.log")` after the session went `Idle`; `ReportError("denied")` | `("ERROR: see ffmpeg.log", Error)` (`:67-70`); `("ERROR: denied", Error)` (`:350`) |
| C21 | `Paused`; `ResumeThrow = InvalidOperationException("boom")`, `ResumeGate` pending; the test moves the session to `Idle` while the resume is pending; gate completed | `("ERROR: boom", Error)` (while the session stays `Paused` the line shows `|| PAUSED`, the message is kept until the state leaves `Paused`) |

Intermediate messages (gated session, state set by the test): while `StopAsync` is pending and `State == Finalizing` → `SAVING...` Warning; while `CancelAsync` is pending and `State == Finalizing` → `SAVING...` (quirk 3); while `CancelAsync` is pending and `State` still `Recording` → the `* REC` line (quirk 3: `CANCELING...` shows only in `Idle`; held by one row with `State` forced to `Idle` → `("CANCELING...", Normal)`, `:286`).
Message clearing: after C10, `StartAsync()` makes `Status` `READY` and `LastFile` `null` before the session call completes (`:229-230`).
`UpdateStatus` event rule: from `Idle`, two calls → 0 `Changed`; `Recording` with `Stats` changed from 1 s to 2 s → 1 `Changed` per call that changed it, 0 when the stats are equal.

#### Group D — enablement (`RecorderControllerControlsTests`): `Controls` per combination (`:358-372`: `idle = State == Idle`, `active = Recording || Paused`)

Selectors = Source, VideoQuality, Mic, Speaker, AudioQuality, Region (all equal `idle`). Busy is produced by a gated command (start pending → state `Idle`; pause pending → `Recording`; resume pending → `Paused`; stop pending → `Finalizing` set by the fake); the ffmpeg-missing row uses `ffmpegFound false`.

| # | State | busy | ffmpeg | Selectors | Rec | Pause | PauseLabel | Stop | Cancel |
|---|---|---|---|---|---|---|---|---|---|
| D1 | Idle | no | found | true | **true** | false | `\|\| PAUSE` | false | false |
| D2 | Idle | yes (start pending) | found | true | false | false | `\|\| PAUSE` | false | false |
| D3 | Idle | no | missing | true | false | false | `\|\| PAUSE` | false | false |
| D4 | Recording | no | found | false | false | **true** | `\|\| PAUSE` | **true** | **true** |
| D5 | Recording | yes (pause pending) | found | false | false | false | `\|\| PAUSE` | false | false |
| D6 | Paused | no | found | false | false | **true** | `> RESUME` | true | true |
| D7 | Paused | yes (resume pending) | found | false | false | false | `> RESUME` | false | false |
| D8 | Finalizing | no | found | false | false | false | `\|\| PAUSE` | false | false |
| D9 | Finalizing | yes (stop pending) | found | false | false | false | `\|\| PAUSE` | false | false |

Source of every cell: `:362-372`. `Controls` is re-evaluated on every `Refresh()`: D-rows read it after the session state changed and `StateChanged` was raised (inline marshal).

#### Group E — commands, overlay, threading (`RecorderControllerCommandTests`)

| # | Scenario | Expected |
|---|---|---|
| E1 | `StartAsync()` with `SelectSource(3)` (ALL), `SelectVideoQuality(2)`, `SelectAudioQuality(2)` | `Session.StartArgs` = (`Targets[3]`, `P1080`, `Wav`); `Session.Log` = `["session.Start"]` (`:231`) |
| E1b | `E1b_StartKeepsTheVideoAndAudioIndicesApart` (added by QA): `SelectVideoQuality(0)`, `SelectAudioQuality(2)` (indices that differ from each other and from the defaults), then `StartAsync()` | `Session.StartArgs` quality = (`P480`, `Wav`); a swap of the two indices in `StartAsync` gives (`P1080`, `Aac128`)/another pair and fails |
| E2 | **AC4 order.** `Session.Start` gated. Subscribe `Changed` → `Log.Add("changed:" + Overlay)`. Call `var t = Controller.StartAsync();` and assert **before** completing the gate | `Log` = `["changed:Recording", "session.Start"]` exactly (first `Changed` with overlay already `Recording`, then the session call); `Overlay == Recording`; `Controls.RecEnabled == false`; `Status.Text == "READY"`. Complete the gate, `await t`: `Overlay == Recording` (session now `Recording`). Under the wrong order (overlay changed after the session call returns) `Log` starts with `session.Start` and `Overlay` is `Idle` at the assertion |
| E3 | as E2 but the gate fails with `InvalidOperationException("boom")` | after `await t`: `Overlay == Idle`, `Status` = `("ERROR: boom", Error)`, `Controls.RecEnabled == true`, last `Changed` log entry `changed:Idle` |
| E3b | overlay by state: set `State` + `RaiseStateChanged()` | `Idle → Idle`, `Recording → Recording`, `Paused → Paused`, `Finalizing → Idle` (`:374-379`) |
| E4 | second `StartAsync()` while the first is pending | `Log` has one `session.Start` (`:224`); second returns a completed task |
| E5 | `StartAsync()` with ffmpeg missing, and while `State == Recording` | no `session.Start` (guard = `Controls.RecEnabled`) |
| E5b | **Live guard.** Queueing `marshal` (a `ConcurrentQueue<Action>`). Set `Session.State = Recording`, `Session.RaiseStateChanged()`, do **not** drain; then `StartAsync()` | `Session.Log` has no `session.Start`; the returned task is completed; `Status` unchanged (guard reads `Session.State`, not the stale `Controls.RecEnabled`) |
| E6 | `PauseOrResumeAsync()` in `Recording`, `Paused`, `Idle`, `Finalizing` | `session.Pause`; `session.Resume`; in `Idle` and `Finalizing` it returns before setting busy: no session call and no `Changed` (`:251-252`); in `Paused` while resume is pending a second call is ignored (busy, `:245`); `ChangedCount` is unchanged by the Idle call and by the Finalizing call; resume overlay: while pending `Overlay == Paused` (decision: no pre-change); after completion `Recording` |
| E7 | `StopAsync()` in `Recording`/`Paused`; in `Idle` | `session.Stop`; in `Idle` no call, `Status` unchanged, `Changed` not raised (`:307`) |
| E8 | `CancelAsync(confirm)` with `confirm` → false; → true; busy; `State == Idle` | false: no `session.Cancel`, busy cleared, `Status` unchanged (`:282`); true: one `session.Cancel`; busy: no call, `confirm` not invoked (`:272`); `Idle`: `confirm` not invoked (`:273`) |
| E9 | `CancelAsync` where `confirm` sets the fake `State = Idle` before returning true | no `session.Cancel` (re-check, `:284`); a following `StartAsync()` works (busy was never set) |
| E10 | `PrepareCloseAsync()` in `Idle`; `Recording`; `Paused`; `Finalizing` | `true` without session calls; `true` after `session.Stop` (status `SAVED…` by the stop flow); `true` after `session.Stop`; `false` without session calls (`:426-436`); plus: while a gated `StartAsync` is pending (state `Idle`, busy) `PrepareCloseAsync()` returns `true` without a session call (quirk 7, `:425-436`) |
| E11 | `LastFile` | after C10 with an existing file → path; after `File.Delete` → `null`; before any stop → `null` |
| E12 | `ReportError("denied")` | `Status` = `("ERROR: denied", Error)`; one `Changed` |
| E13 | **Marshal.** `marshal` enqueues into a `ConcurrentQueue<Action>` (it is enqueued from `Task.Run`, so a plain `Queue` or `List` is a data race) and the test drains it on its own thread. `Task.Run(() => Session.RaiseStateChanged())` after setting `State = Recording` on a pool thread | before draining: `Changed` count 0 and `Controls.PauseEnabled` still false; set `State = Paused` before draining; after draining the queue (on the test thread): exactly 1 `Changed`, `Controls.PauseLabel == "> RESUME"`, `Overlay == Paused` (Trap 3) |
| E14 | `RaiseFaulted("ERROR: see ffmpeg.log")` from a pool thread with a queueing marshal | nothing before draining; after: `Status.Text` `ERROR: see ffmpeg.log`, 1 `Changed` |
| E15 | `Dispose()` then `RaiseStateChanged()` and `RaiseFaulted("x")` | marshal never called; `Changed` count 0 |
| E15b | **Queued before Dispose.** Queueing marshal; `Session.State = Recording`; `Session.RaiseStateChanged()` (one action queued); `Dispose()`; then drain | `Changed` count 0 and `Status`/`Controls`/`Overlay` equal their values before the event (the posted action checks `_disposed`); same with `RaiseFaulted("ERROR: x")` |
| E16 | `TickMeters()` with a mic capture raising a float32 buffer whose maximum is `0.5`, immediately before | `MicLevel` = 0.5 (±1e-4), `SpeakerLevel` 0; no `Changed` raised |
| E16b | Mic and speaker captures; mic raises 0.5, speaker raises 0.25, `TickMeters()`; mic raises 0.1, `TickMeters()` (no sleeps, inside the 300 ms peak window) | `MicLevel` = 0.5 × 0.85 (±1e-4), `SpeakerLevel` = 0.25 (±1e-4) |
| R1 (×4) | A `Changed` handler throws on its first call only; `StartAsync`, `PauseOrResumeAsync` (Recording), `CancelAsync` (confirm true, Recording), `StopAsync` (Recording) | the exception is caught by the command's `catch` (no throw out of the await; start: `Status` `ERROR: view`, `Overlay` Idle, `RecEnabled`; others: `PauseEnabled` / `CancelEnabled` / `StopEnabled` true again); a second call of the same command reaches the session |
| R2 (a, ×2) | `[Theory]` `AutoTransitions` true and false: `CancelAsync(confirm)` twice while the first `confirm` is pending | the second call completes at once; `confirm` invoked once; `Controls.CancelEnabled` stays true during the dialog; after the answer true exactly one `session.Cancel` |
| R2 (b) | `CancelAsync(confirm)` pending; `PauseOrResumeAsync()` with a gated pause; `confirm` answers true | no `session.Cancel`; after the pause gate completes `State == Paused` and still no cancel |
| R7 | `StartGate` pending; `Dispose()`; gate completed; task awaited | `ChangedCount` equals its value at `Dispose()` |
| R9 | `Targets`, `SourceLabels` read twice; `SetRegion` | `Targets` is not a `List<CaptureTarget>` and its `IList` `Add` throws `NotSupportedException`; `SourceLabels` is the same instance until `SetRegion`, then a new one with 5 labels |

#### Group M — `LevelMeter` (`LevelMeterTests`), source `:197-198`

| # | `Next(peak, previous)` | Expected |
|---|---|---|
| M1 | `(0.5, 0.2)` | `0.5` |
| M2 | `(0, 1)` | `0.85` (±1e-6) |
| M3 | `(0.1, 1)`; then 20 steps of `(0, previous)` from 1 | `0.85`; `0.85^20 ≈ 0.03876` (±1e-5) |

#### Group W — real session wiring (`RecorderControllerSessionTests`, `[Collection(typeof(ProcessEnvironmentCollection))]`, Linux only like `RecordingSessionTests`: `Assert.SkipWhen(OperatingSystem.IsWindows(), …)`; `HOME`/`XDG_CONFIG_HOME` point at temp dirs and are restored; setup copied from `RecordingSessionTests` constructor: `FakeFfmpeg`, `FakeChildProcessLauncher`, `FakeAudioPipeFactory`, `RecordingSession(audio, TestPlatform.Create(...), locator)`)

Group W fixture rule: the real `RecordingSession` raises `StateChanged` on pool threads, so the fixture's `marshal` enqueues into a `ConcurrentQueue<Action>`; the test drains it on the test thread after every awaited command and before every assertion (a `Drain()` helper in `ControllerFixture`). Never use the inline `a => a()` marshal with the real session: controller state would be mutated on pool threads concurrently with the command continuations, contradicting the UI-thread-only contract. Because the session may raise `StateChanged` slightly after `StopAsync`/`CancelAsync`/`StartAsync` returned (the event comes from the same call, but the posted action runs only on drain), `Drain()` must run once more before asserting on `Controls`, `Overlay` or `Status`.

| # | Scenario | Expected |
|---|---|---|
| W1 | `StartAsync()` then `UpdateStatus()` | `Overlay == Recording`; `Status.Severity == Recording`; `Status.Text` matches `^\* REC  \d{2}:\d{2}:\d{2}  \d+\.\d MB  720P$` under `InvariantCulture` (no audio: label is `Quality.Label(P720)` only, `RecordingSession.cs:100-102`); `Controls.StopEnabled` true |
| W2 | W1 then `PauseOrResumeAsync()` then `PauseOrResumeAsync()` then `StopAsync()` | states seen through `Controls.PauseLabel` after each step: `> RESUME`, `\|\| PAUSE`; final `Status` severity `Success`, text `SAVED: REC_<stamp>.mp4` with `<stamp>` matching `^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$`; `LastFile` exists in `AppPaths.OutputFolder`; `Overlay == Idle` |
| W3 | `StartAsync()` then `CancelAsync(() => Task.FromResult(true))` | `Status` = `("CANCELED: recording discarded", Normal)`; no `REC_*` file; `.parts/<stamp>` removed; `Overlay == Idle` |

#### Guard checks (run each once on a copy; each must fail the named test, for that reason; record the actual messages)

(m) audio/video index swap in `StartAsync` → E1b; (a) pre-start `Refresh()` removed (or moved after `await session.StartAsync`) → E2 (`Log` order and `Overlay` while pending), AC4; (b) `starting = false` removed from `finally` → E3 (`Overlay` stays `Recording`); (c) `busy` guard removed from `StartAsync` → E4; the live-state guard reduced to `busy` only → E5; the live read of `_session.State` in the start guard replaced by the cached `Controls.RecEnabled` → E5b; (d) `marshal` bypassed in the `StateChanged` handler → E13; in the `Faulted` handler → E14; `Dispose` not unsubscribing → E15; the `_disposed` check in the posted action removed → E15b; (e) each `Select*`/`SetRegion` idle-and-not-busy guard removed → B10 (named per method); the live read of `_session.State` in the selection guards replaced by the cached `Controls` → B10b; the same-index check removed from `SelectMic` → B12 (and from each other `Select*`, one at a time → B12 names the failing call); (f) `SaveSettings` mapping: `Kind != All` condition removed → B2; `"Monitor"` for Region replaced by `Kind.ToString()` → B3; one field dropped from the write → B1/B6/B7/B8; save moved before `ApplyMic` → B6; a save added to `StartAsync` → B9; (g) initial selection: `_firstRun ? default : saved` swapped → A8/A9; unknown-id fallback `index < 0 ? 0 : index + 1` and the `if (wanted == null) return 0;` line of `InitialDeviceIndex` are **equivalent mutants** (`FindIndex` gives −1 for a null or unknown id, and −1 + 1 = 0), so no test can kill them; A10 and A11 still hold the behaviour (unknown id and null id → index 0, nothing opened); `Ordinal` compare of `"All"` replaced by `OrdinalIgnoreCase` → A6; primary fallback removed → A3; `Math.Max(index, 0)` removed → A4; quality clamp removed → A15; (h) status: each of the four strings and the two-space separators changed → C2/C3/C4/C7; `F1` removed → C5; `hh\:mm\:ss` changed → C3/C6; `StartsWith("ERROR")` / `StartsWith("SAVED")` → C9/C10/C11; `LastError ??` fallbacks → C11/C12/C14/C15; message clearing in `StartAsync` → C10 ("READY after start"); `LastFile` existence check removed → E11; (i) `Controls`: each of the nine expressions flipped one at a time (`idle`, `!busy`, `Resolve() != null`, `active`, `Paused` label) → D1–D9 (name the failing row per flip); overlay switch arms changed → E3b; (j) cancel re-check after `confirm` removed → E9; busy check removed → E8; `PrepareCloseAsync` `Finalizing` arm → E10; a `busy` check added to `PrepareCloseAsync` (quirk 7) → E10 pending-start row; (k) `UpdateStatus` raising unconditionally → C-rule "0 Changed when idle"; `TickMeters` raising `Changed` → E16; `LevelMeter` factor changed → M2/M3; (l) snapshot: `GetMonitors()` called again in `SetRegion` → `FakeScreenCatalog.Calls` assertions in A17/B3.
(n) review guards (all on a copy, see the Dev Agent Record table): `Refresh()` before `try` in each of the four commands -> R1 (×4); `_confirming` check removed -> R2 (a); `_busy` re-check after the answer removed -> R2 (b); `Changed = null` removed -> R7; `Targets` returns the list -> R9; `SourceLabels` rebuilt on every read -> R9; `Refresh()` removed from `SelectVideoQuality`, `SelectAudioQuality`, `SelectSpeaker` -> B8, B8, B7; `MicLevel = _audio.MicPeak;` and `SpeakerLevel = LevelMeter.Next(0f, SpeakerLevel);` -> E16b; `Changed` raised before the early return of `PauseOrResumeAsync` -> E6; resume catch without the message -> C21.
A guard that no test reaches is either dead or untested: build an input that reaches it before deleting it; stop and ask if none exists.

### Previous Story Intelligence (1-1, 1-2, 1-3, done)
- Test-first worked: write the test, see it fail to compile, then add the types.
- The 1-2 planner once planned to read `MemoryStream.Length` after dispose (throws). The mechanics of every test here were checked against the real API: no stream is read after dispose; `Settings` is mutated in place so the fixture copies the fields at each save; `FakeAudioCaptureFactory.Calls` / `Created[i].Disposed` are existing members (`FakeAudioCapture.cs`); the internal `FfmpegLocator` constructor is reachable through `InternalsVisibleTo("MyScreenRecorder.Core.Tests")` (`MyScreenRecorder.Core.csproj`); `Assert.SkipWhen` exists in `xunit.v3.assert` 4.0.2 (used in `RecordingSessionTests.cs`).
- Reviews found untested branches every time (even size, swallowed save errors, pump clamp): the guard list above names a test for every branch of the controller; a branch without one is a finding.
- Guard checks were run on a copy with `--no-incremental` and a full test run per mutation.
- `dotnet test` needs the repo-root `global.json` (MTP); `TestResults/` is ignored; tests that change the process environment belong in `ProcessEnvironmentCollection` (only group W does here).
- Reviewer subagents hit an API rate limit in 1-3; if that happens, say so in Review Findings.
- Deferred from 1-1/1-2/1-3: see Contract Guardrail (not fixed here).

### Git Intelligence Summary
- `3d2d000` tracker commit: 1-2 done (on top of `df93a35`) · `df93a35` merge PR #3 (1-2) · `4e0a5d3` pipeline into Core · `75765fa` merge PR #2 (1-3) · `cd0adf6` audio capture seam · `0077a81` Core split. `MainWindow.xaml.cs` last changed in 1-2 (composition root, lines 26-32, 60-64); its logic below line 80 is unchanged since `b6259ee`.

### Latest Stack Information
- .NET SDK 10.0.403 (user install in `~/.dotnet`), `net10.0` Core and tests; `NAudio.Core` 2.2.1; `xunit.v3` 4.0.2 on Microsoft Testing Platform. No new package.

### References
- [Source: `docs/planning/linux-build-architecture.md` - §2 D3, D7, D9; §3.2, §3.3; §4 (`IScreenCatalog`); §5.8 (badges before capture); §5.12 (cancel dialog, OPEN); §7 (two UIs drift)]
- [Source: `docs/planning/epics-linux-build.md` - §2 Working rules; §3 DoD; §6 story 1-4; §7 Sequencing; 5-1 in Epic 5]
- [Source: `CLAUDE.md` - Architecture, Conventions]
- [Source: `src/MyScreenRecorder/MainWindow.xaml.cs`, `MainWindow.xaml`, `Cycler.cs` (`SetItems` clamp), `Overlay/OverlayManager.cs` (`OverlayState`, `showBadge`), `Capture/ScreenInfo.cs` at `3d2d000`]
- [Source: `src/MyScreenRecorder.Core/Recording/RecordingSession.cs` (states, `Stats`, `LastError`, event threads), `Audio/AudioEngine.cs` (`SetMic/SetSpeaker`, `Open`), `Audio/AudioSource.cs` (`Peak`), `Settings.cs`, `AppPaths.cs`, `Capture/CaptureTarget.cs`, `Recording/Quality.cs`, `Platform/IAudioDeviceCatalog.cs` at `3d2d000`]
- [Source: `docs/implementation/1-2-recording-pipeline-into-core.md`, `1-3-audio-capture-seam.md` - Decision Record, Traps, Review Findings]

## Dev Agent Record

### Agent Model Used

claude-sonnet-5-5 (Sonnet 5.5). Baseline revision `3d2d000` (branch `feat/linux-1-4-recorder-controller`).

### Debug Log References

- Baseline: `dotnet build MyScreenRecorder.sln -c Debug` 0 warnings, 0 errors; `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` total 172, failed 0.
- Red: the test project did not compile before the types existed (CS0103/CS0246 for `RecorderController`, `OverlayMode`, `IRecordingSession`, `IScreenCatalog`).
- First green run: 285 total, 1 failed (C15, own test bug, fixed). After additions (C14b, C18b, E8c, E8d): 289; with E1b (QA): 290.
- Final: build `--no-incremental` 0 warnings, 0 errors; three test runs in a row each `total: 289, failed: 0, succeeded: 289` (before the review fixes; the final count after the full review is 301, see the Full review block).
- `git diff --stat 3d2d000 -- src/MyScreenRecorder/` prints nothing. In Core only `RecordingSession.cs` changed among existing files (one line).
- Guard checks: 130 mutations on copies of the tree in the session scratchpad `dev-1-4/w0..w3`, each `dotnet build --no-incremental` followed by the full test run of the test project (`dotnet test --project ... --no-build`; `dotnet test --no-incremental` fails the MTP handshake in a copy, so build and test were split). First pass: 125 killed by the expected test, 1 killed by other tests (j13), 4 not killed (below); all fixed or explained. These 130 logs exist only in the session scratchpad (`.../scratchpad/dev-1-4/`), and the failing messages were not recorded for them. The earlier claim that all guards were killed was wrong: the full review found three survivors (R3: `Refresh()` removed from `SelectVideoQuality`, `SelectAudioQuality`, `SelectSpeaker`; R4: `MicLevel = _audio.MicPeak;` and `SpeakerLevel = LevelMeter.Next(0f, SpeakerLevel);`; R5: `Changed` raised before the early return of `PauseOrResumeAsync`). They are now killed; the table below records the mutation, the failing test and the message (copy of the tree, `dotnet build --no-incremental`, then `dotnet test --project ... --no-build`; every mutation fails exactly the test named, nothing else).

| Mutation | Failing test | Assertion message |
|---|---|---|
| R3: `Refresh()` removed from `SelectVideoQuality` | `B8_QualityChangesAreSaved` | `Assert.Equal() Failure: Values differ`, Expected `1`, Actual `0` |
| R3: `Refresh()` removed from `SelectAudioQuality` | `B8_QualityChangesAreSaved` | Expected `2`, Actual `1` |
| R3: `Refresh()` removed from `SelectSpeaker` | `B7_SelectingASpeakerOpensItInLoopbackAndSavesIt` | Expected `1`, Actual `0` |
| R4: `MicLevel = _audio.MicPeak;` | `E16b_TickMetersFollowsTheMicWithDecayAndTheSpeakerWithItsPeak` | `Values are not within tolerance 9.99999975E-05`, Expected `0.425000012`, Actual `0.100000001` |
| R4: `SpeakerLevel = LevelMeter.Next(0f, SpeakerLevel);` | `E16b_...` | Expected `0.25`, Actual `0` |
| R5: `Changed` raised before the early return of `PauseOrResumeAsync` | `E6_PauseOrResumeFollowsTheState` | `Values differ`, Expected `0`, Actual `1` (the `ChangedCount` after the Idle call) |
| R6: the resume `catch` does not set the message | `C21_AFailedResumeShowsTheException` (and `C15_...`, which shares the catch) | Expected `StatusLine { Text = ERROR: boom, Severity = Error }`, Actual `StatusLine { Text = READY, Severity = Normal }` |
| R1: first `Refresh()` before `try` in `StartAsync` | `R1_AThrowingSubscriberDoesNotLeaveAStartBusy` | `System.InvalidOperationException : view` (the subscriber exception escapes the command) |
| R1: same in `PauseOrResumeAsync` | `R1_AThrowingSubscriberDoesNotLeaveAPauseBusy` | `System.InvalidOperationException : view` |
| R1: same in `CancelAsync` | `R1_AThrowingSubscriberDoesNotLeaveACancelBusy` | `System.InvalidOperationException : view` |
| R1: same in `StopRecordingAsync` | `R1_AThrowingSubscriberDoesNotLeaveAStopBusy` | `System.InvalidOperationException : view` |
| R2: `_confirming` check removed | `R2_ASecondCancelWhileTheDialogIsOpenIsIgnored` (both theory rows) | `Assert.True() Failure`, Expected `True`, Actual `False` (the second call did not complete: it opened a second dialog) |
| R2: `_busy` re-check after the answer removed | `R2_ACancelAnsweredYesAfterAPauseStartedDoesNotCancel` | `Assert.DoesNotContain() Failure: Item found in collection`, Collection `["session.Pause", "session.Cancel"]` |
| R7: `Changed = null;` removed from `Dispose` | `R7_ChangedIsNotRaisedAfterDisposeWhenACommandCompletes` | `Values differ`, Expected `1`, Actual `2` |
| R9: `Targets` returns the list | `R9_TheTargetListIsReadOnlyAndTheLabelsAreCachedUntilTheRegionChanges` | `Assert.False() Failure`, Expected `False`, Actual `True` |
| R9: `SourceLabels` rebuilt on every read | `R9_...` | `Assert.Same() Failure: Values are not the same instance` |

R8 (`ComputeControls` calls `CanStart()` and `IsActive()`) and R14 (shared helpers) change no behaviour; D1-D9 and all other tests hold them. Logs of this table: `.../scratchpad/fix-1-4/raw-*.txt`.

### Completion Notes List

- Guard checks (mutation -> failing test): all of (a)-(l) ran. Pre-start `Refresh()` removed or moved after the await -> E2; `_starting` not cleared -> E3; busy removed from `CanStart` -> E4; state removed -> E5; cached `Controls.RecEnabled` -> E5b; marshal bypassed -> E13 / E14; Dispose not unsubscribing -> E15; `_disposed` check removed (each handler) -> E15b; each of the six selection guards removed -> B10 (per method); cached `Controls` in `CanSelect` -> B10b; each same-index check -> B12; `Kind != All` removed -> B2; Region saved as `Kind` -> B3; each settings field dropped -> B1/B2/B6/B7/B8; save before apply (mic, speaker) -> B6 / B7; save in start -> B9; first-run swap -> A8/A9; `OrdinalIgnoreCase` -> A6; primary fallback -> A3; `Math.Max` -> A4; clamp -> A15; status strings, separators, `F1`, duration format, prefixes, `LastError ??` fallbacks, message clearing, `LastFile` check -> C-rows and E11; each `ControlState` expression flipped -> D1-D7 (E8c/E8d for stop and cancel `!busy`); overlay arms -> E3b; cancel recheck -> E9; busy check -> E8; close arms -> E10; `UpdateStatus` unconditional -> C20; `TickMeters` raising -> E16; factor -> M2/M3; second `GetMonitors()` call -> A17/B3.
- Guards no test reached and what was done: (1) the `MonitorNumbers.Length > 0` condition of the settings mapping is unreachable (every `CaptureTarget` factory sets at least one number) and was removed from `RecorderController.SaveSettings`; (2) `index < 0 ? 0 : index + 1` in `InitialDeviceIndex` is arithmetically equivalent to `index + 1`, so story mutation (g) "unknown-id fallback removed" cannot fail; the ported ternary is kept; (3) story B2 started from `MonitorNumber 1`, where the `Kind != All` guard cannot be seen (ALL lists monitor 1 first); B2 now starts from monitor 2 and expects `("All", 2)`.
- Survivors of the first pass, fixed by new or stronger tests and re-run killed: speaker save-before-apply (B7 asserts `CaptureCallsAtSave`), `SAVING...` message of the stop flow (new C18b), pause busy check hung the run (E6b asserts `second.IsCompleted`), cancel and stop pre-refresh and `!busy` (new E8c, E8d), `LastFile` cleared by cancel (new C14b), B11 also asserts the indices after the throws. Mutation j13 (busy not cleared after start) fails D6, D7, C15, C19 instead of the named E4/D1/C8, for the same reason.
- Deviations: (1) `MonitorNumbers.Length > 0` dropped; (2) `PauseOrResumeAsync` returns before setting busy when the state is neither Recording nor Paused, so no `Changed` in Idle/Finalizing (the story says "nothing"); (3) selections check the index range before the idle guard (a programming error always throws); (4) B2, B7, B11, E6b strengthened, tests C14b, C18b, E8c, E8d added; (5) no reviewer subagents were run, the mutation pass took their place.
- Definition of Done (`docs/planning/epics-linux-build.md` section 3): 1 ACs with tests: AC1 A1-A17, AC2 C1-C21, AC3 D1-D9, AC4 E2/E3, AC5 B1-B12/E1-E12, AC6 E13-E16/M1-M3, AC7 W1-W3 plus the empty diff; 2 build 0 warnings 0 errors; 3 tests pass three times (301 after the full review); 4 guard checks done, results above; 5 comments: only the `///` of `IScreenCatalog` added; 6 `CLAUDE.md` updated; 7 not applicable, `git diff --stat 3d2d000 -- src/MyScreenRecorder/` is empty; 8 not applicable (no Linux smoke item); 9 nothing committed, staged or pushed.
- Error log: entries 162-166; full review: entries 174-188.

### Review Findings

- [Patch] done: the survivors listed above.
- [Defer] `StopAsync` checks only the session state; `PrepareCloseAsync` while a start is pending returns true (quirks 6 and 7, copied on purpose).
- [Dismiss] Equivalent mutant `index < 0 ? 0 : index + 1`.
- No reviewer subagents were run by the developer.

#### QA review (2026-11-03): PASS WITH CONCERNS

Scope: working tree against `3d2d000`. Done by the QA engineer directly (no reviewer subagents; the diff was read line by line against `MainWindow.xaml.cs`).

- Parity: every status string, guard, side-effect order and settings field of `RecorderController.cs` matches the reference map; quirks 1-7 are copied and tested. No defect in production code.
- Deviations: (a) correct, all three `CaptureTarget` factories set at least one monitor number, and the controller builds targets only through them; (b) correct and consistent with E7 (no `Changed` when nothing happens); (c) correct, a programming error throws in every state; (d) correct, from monitor 1 the `Kind != All` guard is invisible; (e) correct, the mutant is equivalent (so is `if (wanted == null) return 0;`).
- [Patch] done: mutation `AudioQualities[AudioQualityIndex]` -> `AudioQualities[VideoQualityIndex]` survived (E1 uses index 2 for both). Added `E1b_StartKeepsTheVideoAndAudioIndicesApart`; both index swaps now fail it. Error log 167.
- [Doc] closed by the full review (R10): the normative text was brought in line (see the Change Log line of the full review). Original item: normative text not updated for the deviations (error log 168): the mapping text and the B2 row (`MonitorNumbers.Length > 0`, `("All", 1)`), guard check (g) naming A10 for an equivalent mutant, the Decision Record order "idle guard first, then range" (code: range first), E6 "nothing" (code: no `Changed`).
- Checks: 22 own mutations (21 killed, 1 equivalent survivor `if (wanted == null) return 0;`, 1 real survivor fixed above); AC4 mutation (pre-start `Refresh()` after the await) fails E2, D2 and C10b; 6 full runs of 289 tests, then 290 with E1b: all green; no real-time sleep in the new tests; no `ConfigureAwait` in `Controller/`.

#### Full review (2026-11-03): findings R1-R14 fixed, X1-X3 refuted

- R1 [Patch] done: a throwing `Changed` subscriber left the controller busy; the first `Refresh()` moved inside `try` in the four commands; 4 tests.
- R2 [Patch] done: `CancelAsync` re-entry during the dialog and the missing busy re-check after the answer; `_confirming` and the re-check; 3 test rows (a ×2, b).
- R3 [Patch] done: B7 and B8 assert exactly one `Changed` per changing selector; the three `Refresh()` mutations are killed.
- R4 [Patch] done: new test E16b over both meters; the two meter mutations are killed.
- R5 [Patch] done: E6 asserts `ChangedCount` for the Idle and Finalizing calls; the mutation is killed.
- R6 [Patch] done: C21 covers `ResumeThrow`. Deviation from the request: the test moves the fake session to `Idle` while the resume is pending, because with the session still `Paused` the status line is `|| PAUSED` and the message is not visible (the real `RecordingSession.ResumeAsync` also stays `Paused` after a failed resume, so the error stays hidden until the state changes; not changed here, outside the scope of the list).
- R7 [Patch] done: `Dispose` clears `Changed`; test R7.
- R8 [Patch] done: `ComputeControls` uses `CanStart()` and `IsActive()`; no behaviour change.
- R9 [Patch] done: `Targets` is a cached `ReadOnlyCollection`, `SourceLabels` a cached list, both rebuilt in `RebuildTargets`; test R9.
- R10 [Doc] done: this file (counts, ranges, test-support shapes, test tables, guard list, Decision Record).
- R11 [Doc] done: `docs/planning/linux-build-architecture.md` (delegation wording, `IRecordingSession` and `OverlayMode` in section 4).
- R12 [Doc] done: mutation table and the statement about the 130 earlier logs in the Dev Agent Record.
- R13 [Doc] done: the claim "all guards killed" corrected; survivors R3, R4, R5 named and killed.
- R14 [Patch] done: `Gate()` and `SetState` moved into `ControllerFixture`.
- X1 [Dismiss]: a missing class comment on `RecorderController` (`:43-52`) is not required; D7 limits docstrings to platform interfaces and the contract is in the Decision Record and the Contract Guardrail.
- X2 [Dismiss]: `Resolve()` in `ComputeControls` is parity with `MainWindow.xaml.cs:368`, which calls it in every `UpdateUi`; not a defect.
- X3 [Dismiss]: no constructor null-checks: `Nullable` is enabled and no Core constructor checks (`RecordingSession.cs:51`); `SetMic` throws only with an active segment, which `CanSelect` excludes.
- Final numbers: build `--no-incremental` 0 warnings, 0 errors; three runs `total: 301, failed: 0, succeeded: 301`; `git diff --stat 3d2d000 -- src/MyScreenRecorder/` empty.

### File List

Pre-existing planning changes (not part of this implementation): `docs/implementation/1-4-recorder-controller.md` (this file; edited only in Status, task ticks, Dev Agent Record, Change Log), `docs/implementation/sprint-status.yaml` (1-4 also set to `review`), `docs/planning/epics-linux-build.md`, `docs/planning/linux-build-architecture.md`.

- `src/MyScreenRecorder.Core/Platform/IScreenCatalog.cs` (CREATED)
- `src/MyScreenRecorder.Core/Recording/IRecordingSession.cs` (CREATED)
- `src/MyScreenRecorder.Core/Recording/RecordingSession.cs` (UPDATED: declaration implements `IRecordingSession`)
- `src/MyScreenRecorder.Core/Controller/RecorderController.cs`, `ControlState.cs`, `StatusLine.cs`, `OverlayMode.cs`, `LevelMeter.cs` (CREATED)
- `tests/MyScreenRecorder.Core.Tests/FakeRecordingSession.cs`, `FakeScreenCatalog.cs`, `FakeAudioDeviceCatalog.cs`, `ControllerFixture.cs`, `RecorderControllerInitTests.cs`, `RecorderControllerSettingsTests.cs`, `RecorderControllerStatusTests.cs`, `RecorderControllerControlsTests.cs`, `RecorderControllerCommandTests.cs`, `RecorderControllerSessionTests.cs`, `LevelMeterTests.cs` (CREATED)
- `CLAUDE.md` (UPDATED: Architecture section)

## Change Log

- 2026-11-03: Full review fixes R1-R14: `RecorderController` (busy-safe commands, `_confirming`, `Changed` cleared on `Dispose`, cached read-only targets and labels, `ComputeControls` helpers), 11 new tests (301 total), shared test helpers in `ControllerFixture`, mutation table, documents aligned (this story, `docs/planning/linux-build-architecture.md`). Status stays `review`.
- 2026-11-03: Text aligned with the implementation (QA-confirmed deviations, error log 168): range check runs first in `Select*`; mapping drops `MonitorNumbers.Length > 0` (new Decision Record line); row B2 starts from `MonitorNumber 2` and expects `("All", 2, …)`; row E6 says Idle/Finalizing return before busy (no call, no `Changed`); guard (g) notes the two equivalent mutants of `InitialDeviceIndex`; row E1b and guard (m) added.
- 2026-11-03: QA review (PASS WITH CONCERNS): one survivor fixed with test E1b (290 tests); documentation drift listed in Review Findings.
- 2026-11-03: Implemented (status `review`): controller, seams, 117 new tests (289 total), guard checks, `CLAUDE.md`.
- 2026-11-03: Revised after review (R1–R7): live-state guards (start, selections), `_disposed` check in posted actions, queueing `ConcurrentQueue` marshal for the real-session tests and for E13/E14, same-index selection is a no-op (`Cycler.cs:47-52`), quirk 7 (close while start is pending), `3d2d000` described as the tracker commit on top of `df93a35`; new tests B10b, B12, E5b, E15b and an E10 row; guard checks extended; architecture §5.8 and its amendment line updated.
- 2026-11-03: Story created (ready-for-dev). Epic AC1–AC4 sharpened with exact inputs (AC1: the monitor fixture and device ids; AC2: the strings listed from `MainWindow.xaml.cs`; AC3: the state table; AC4: the observable order), AC5–AC7 added. Doc edits made with the story: `docs/planning/linux-build-architecture.md` (§4 `PlatformServices` sentence: "1.4 adds the screen catalog" replaced by the decision that the controller takes `IScreenCatalog` directly; amendment line), `docs/planning/epics-linux-build.md` (story 1-4 bullet names `IRecordingSession`, `OverlayMode`, and "no Windows smoke").
