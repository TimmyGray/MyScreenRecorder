# Story 2.1: Linux projects skeleton and session check

Status: review

## Story

As the maintainer of MyScreenRecorder,
I want the three Linux projects (`MyScreenRecorder.Platform.Linux`, `MyScreenRecorder.Linux`, `MyScreenRecorder.Platform.Linux.Tests`) in the solution, a `SessionCheck` that classifies the desktop session, and an Avalonia window that opens on X11 (and shows the D1 message on Wayland) or exits with code 1 when `DISPLAY` is unset,
so that the Linux platform services (2-2, 2-3, 2-4) and the Linux UI (3-x) have a compiling, tested home with the right dependency direction, and FR9 holds from the first runnable Linux build.

<!-- Track: Linux — Covers: FR9 (NFR4 for the new project) — Decisions: D1, D3, D7, D9, D10 — Windows smoke: NOT required (no file under src/MyScreenRecorder/ or src/MyScreenRecorder.Core/ changes) — Linux smoke: none of L1-L13 applies (manual run MR1-MR5 below replaces it) -->

## Decision Record

Decisions D1–D11 of the architecture document are not reopened here. The entries below are the decisions this story adds; the reviewer may challenge any of them before development starts. Every probe named here was run during planning on the dev machine (Mint 22.3, X11 `DISPLAY=:0`, .NET SDK 10.0.403 in `~/.dotnet`, runtime 10.0.14) on a copy of the repository in the planner's scratchpad; the commands are in Dev Notes → Probe log.

- 2026-11-16: 1-1 is done and 1-2, 1-3, 1-4 are merged (`d1750c1`, merge of PR #4), so 2-1 is unblocked (epic: needs 1-1). The branch `feat/linux-2-1-linux-projects-skeleton` exists and was created with `--no-track` from `origin/main` at `d1750c1`. Three uncommitted edits exist before this story starts and are not part of it (Task 1 lists them): `CLAUDE.md` (two new convention lines), `docs/implementation/1-4-recorder-controller.md` (status done), `docs/implementation/sprint-status.yaml` (1-4 and epic-1 done).
- 2026-11-16: **No Windows smoke and no WPF/Core change.** The story adds three projects and three solution entries. It changes no file under `src/MyScreenRecorder/`, `src/MyScreenRecorder.Core/` or `tests/MyScreenRecorder.Core.Tests/`. The WPF font file `src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf` is only read (linked into the Linux app). DoD items 7 and 8 therefore do not apply; they are replaced by a hard check: `git diff --stat -- src/MyScreenRecorder src/MyScreenRecorder.Core tests/MyScreenRecorder.Core.Tests` prints nothing. If something in Core must change, stop and ask.
- 2026-11-16: **Avalonia 12.1.4 exists and works with .NET 10.** NuGet flat-container lists 12.1.4 for `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Simple` (newer 12.1.5 and 12.1.6 also exist; D3 pins 12.1.4 and this story keeps it). A probe app with exactly these three packages builds with 0 warnings and opens a window on this machine's X11 session (title, colours and pixel font checked on a screenshot). `Avalonia.Fonts.Inter` is not added: the window text that is not the pixel font falls back to the system default font through fontconfig, which is a documented runtime dependency (4-1 README).
- 2026-11-16: **Project files** are pinned in Contract Guardrail (exact text, built and tested in the probe). Platform.Linux: `net10.0`, no RID, class library. Linux app: `net10.0` + `RuntimeIdentifier linux-x64` (architecture §3.2), `OutputType Exe`, `AssemblyName MyScreenRecorder`, **no** `SelfContained`/`PublishSingleFile` properties (4-1 passes them on the `dotnet publish` command line, architecture §5.13). Tests: `net10.0`, `OutputType Exe`, `xunit.v3` 4.0.2 like Core.Tests. All three: `Nullable enable`, `ImplicitUsings disable`. Rejected: `SelfContained`/single-file properties in the csproj (every Debug build would carry the runtime; 4-1 owns publishing); `Avalonia.Diagnostics`, `Avalonia.Fonts.Inter` (not needed by the skeleton).
- 2026-11-16: **Project references follow architecture §3.2/§3.3 from the start**: Platform.Linux → Core; Linux app → Core + Platform.Linux; tests → Platform.Linux (Core flows transitively; the tests need no Core type yet). Nothing references the WPF project; the WPF project and Core reference nothing new. Core and Platform.Linux use no Avalonia type; only the Linux app references Avalonia. The unused `Core` references in the two Linux projects are deliberate: the direction is part of the skeleton and 2-2/2-3/2-4 start using it. Rejected: adding the references in the stories that first use them (each later story would re-decide the direction; a wrong direction would only show up there).
- 2026-11-16: **The process-level tests need the built app without referencing its assembly.** The tests csproj carries `<ProjectReference ... MyScreenRecorder.Linux.csproj ReferenceOutputAssembly="false" />` (build order only, no compile reference, no Avalonia in the test assembly) and an `AssemblyMetadata` item `LinuxAppDirectory` = `<repo>/src/MyScreenRecorder.Linux/bin/$(Configuration)/net10.0/linux-x64/`. Probe: with `bin/` of the app deleted, `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` rebuilds the app first (Debug and `-c Release`), 0 warnings, no NETSDK error. §3.3 says "tests reference only the project they test (plus Core)"; this is a build-order edge to a second project, no code dependency. Rejected: a compile reference to the app (Avalonia flows into the test, violates §3.3); `dotnet run`/`dotnet build` inside a test (a nested MSBuild per test, 5-10 s each); requiring the user to build the app first (a stale or missing build would fail with an unclear message; the test asserts the file exists and names the path); putting the process tests in a fourth project (the epic lists three).
- 2026-11-16: **The child process is the real apphost, started with `DOTNET_ROOT`.** The apphost `bin/.../linux-x64/MyScreenRecorder` fails with "You must install .NET to run this application" when neither `DOTNET_ROOT` nor `DOTNET_ROOT_X64` is set and .NET is neither in `/usr/share/dotnet` nor registered in `/etc/dotnet/install_location` (probe; this machine has .NET only in `~/.dotnet`). The test host itself has no `DOTNET_ROOT` (probe: `ROOT=` empty inside the test), but `dotnet test` sets `DOTNET_ROOT_X64`, which the apphost reads before `DOTNET_ROOT`; the helper removes `DOTNET_ROOT_X64` from the child so the derived `DOTNET_ROOT` is the only root variable it sees (a root directory that does not exist is skipped and the apphost falls back to `/etc/dotnet/install_location` and `/usr/share/dotnet`, which this machine does not have; probe `COREHOST_TRACE=1`) (QA finding 2). The helper derives the root from the running test runtime: `Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."))` (`/home/timmy/.dotnet` here, probe) and sets it in the child's `ProcessStartInfo.Environment`. Rejected: `dotnet MyScreenRecorder.dll` (needs `dotnet` on `PATH`, which the test host does not have; and it does not run the apphost that users run).
- 2026-11-16: **`SessionCheck` lives in `MyScreenRecorder.Platform.Linux`** (architecture §3.2 and §5.6), namespace `MyScreenRecorder.Platform.Linux` (child of Core's `MyScreenRecorder.Platform`, like the WPF `MyScreenRecorder.Platform.Windows`). It is `public` because the Linux app is another assembly. It is **not** marked `[SupportedOSPlatform("linux")]`: architecture §3.3 asks for the attribute on platform classes, but `SessionCheck` is pure BCL logic and the probe shows the attribute gives 10 `CA1416` warnings ("This call site is reachable on all platforms") at its call sites in `Program.cs` and the tests, which breaks DoD item 2 ("no new warnings"). The marker is decided per class when a class calls a native API (2-2, 2-4); whether it becomes an assembly-level attribute is not probed here and belongs to 2-2.
- 2026-11-16: **`SessionCheck` API.** `Evaluate(IReadOnlyDictionary<string, string?> environment)` is pure: it reads only the dictionary it is given (test `N3` sets all three real variables and passes an empty dictionary). A key that is absent or has the value `null` is "unset"; the empty string is "empty"; both mean "no value" for the three variables (§5.6 says "non-empty"/"empty"; an absent key is the normal way a dictionary says "unset", so this is not an unknown-input case). Variable names are matched exactly (case-sensitive: environment variable names are case-sensitive on Linux; test `N2`). `ReadProcessEnvironment()` builds the dictionary from `Environment.GetEnvironmentVariable` for exactly the three names and always contains all three keys (value `null` when unset). Result type `SessionCheckResult(SessionKind Kind, string? Message)`; `SessionKind` = `X11`, `Wayland`, `NoDisplay`. `Message` is `null` for `X11`. Rejected: two booleans (`IsWayland`, `HasDisplay`) — they allow the state "Wayland and no display" in two spellings and every caller would re-derive precedence; a `Dictionary<string,string>` of all environment variables (the check needs three names; the caller should not hand over the whole environment); `Func<string,string?>` lookup (the epic and §5.6 say dictionary).
- 2026-11-16: **Decision table and precedence (§5.6 plus two story decisions).** (1) `DISPLAY` null/empty → `NoDisplay`, checked **first**: D1 says "without `DISPLAY` ... exits with code 1 before the UI starts", and Avalonia 12.1.4 cannot start without an X display (probe: no Wayland backend in `UsePlatformDetect`), so a Wayland session without XWayland cannot show the message in a window either. (2) Otherwise `Wayland` when `XDG_SESSION_TYPE` equals `wayland` (ordinal, case-insensitive) or `WAYLAND_DISPLAY` is non-empty; (3) otherwise `X11`. XWayland (both `WAYLAND_DISPLAY` and `DISPLAY` set) is `Wayland`: the window opens through XWayland and shows the D1 message (rows S6, S9–S11). No trimming: `" wayland"`, `"wayland "`, `"waylandx"`, `"xwayland"` are not `wayland` (rows S12–S15), and `" "` counts as non-empty for `WAYLAND_DISPLAY` and `DISPLAY` (rows S16, S17; §5.6 says "non-empty" and a whitespace value is not produced by any session manager, so no further rule is invented). Rejected: `Wayland` before `NoDisplay` (a Wayland session without `DISPLAY` would try to open a window that cannot exist); trimming (a rule §5.6 does not state); `IsNullOrWhiteSpace` (same).
- 2026-11-16: **Message texts.** Wayland: D1 verbatim, architecture §2 D1 (line 25 of `docs/planning/linux-build-architecture.md`): `ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.` No `DISPLAY`: the epic and D1 only say "a message of the same kind"; this story fixes the text: `ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.` It is also the text for a Wayland session without `DISPLAY` (one text for "cannot open a window"). Both start with `ERROR:` like every error line of the Windows UI (`MainWindow.xaml.cs:407` classifies by that prefix). The stderr output is the text plus one `\n` (`Console.Error.WriteLine`), stdout stays empty.
- 2026-11-16: **Where the exit path sits.** `Program.Main` computes `SessionCheck.Evaluate(SessionCheck.ReadProcessEnvironment())` as its first statement, before any Avalonia code runs. `Main` itself references no Avalonia type: the `AppBuilder` chain sits in a separate method `StartAvalonia`, because the JIT loads the assemblies a method references when it compiles that method, and with the chain inside `Main` a missing `Avalonia.Controls.dll` ended the NoDisplay path with exit 134 instead of 1 (QA finding 1). For `NoDisplay` it writes the message to `Console.Error` and `return 1` (not `Environment.Exit`). For `X11` and `Wayland` it calls `StartAvalonia(session, args)`, which builds the app with `AppBuilder.Configure(() => new App { Session = session }).UsePlatformDetect().StartWithClassicDesktopLifetime(args)` and returns its result (0 when the window is closed). Probes: `env -u DISPLAY`, `DISPLAY=` (empty) and Wayland variables without `DISPLAY` all exit 1 with the NoDisplay text and no window; `dotnet run --project src/MyScreenRecorder.Linux` passes the exit code through.
- 2026-11-16: **`App` has a public parameterless constructor and an `init` property `Session`**, not a constructor parameter: probe with a `App(SessionCheckResult)` constructor gives warning `AVLN3001` ("XAML resource ... won't be reachable via runtime loader, as no public constructor was found"), which breaks "no new warnings".
- 2026-11-16: **The window.** Title `MY SCREEN RECORDER`, `Width 640`, `SizeToContent Height`, `CanResize False`, `WindowStartupLocation CenterScreen`, content margin `10,6,10,10`, background black, foreground `#F0F0F0`, `FontSize 10`, pixel font; on `X11` one centred green `FontSize 12` text `MY SCREEN RECORDER` (the same as `MainWindow.xaml:8-9`). On `Wayland` the window shows **only** the D1 message (the title block is hidden) in red, wrapped, `LineHeight 16` (probe: without `LineHeight` the second line of the wrapped message overlaps the first in Press Start 2P). The window title stays `MY SCREEN RECORDER` in both cases. There is no REC button in this story, so "disables REC" (D1) is 3-1's job; 3-1 must disable REC when `SessionKind` is not `X11` (listed in Contract Guardrail). Colours, with the lines of `src/MyScreenRecorder/Theme.xaml`: `BgColor #000000` (`:4`), `FgColor #F0F0F0` (`:5`), `GreenColor #00FF66` (`:6`), `RedColor #FF2244` (`:7`), `YellowColor #FFCC00` (`:8`); window `Background`/`Foreground`/`FontSize 10` from the `AppWindow` style (`:19,20,22`). Yellow is defined (the full theme is needed by 3-1) but unused in 2-1. Theme: `Avalonia.Themes.Simple` (architecture §5.12) with `RequestedThemeVariant="Dark"`.
- 2026-11-16: **The pixel font is the tracked WPF file, linked, with a build-time condition.** `src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf` is committed (`git ls-files` lists it, `.gitignore` does not exclude it; added in `8763bc0`); `build.ps1:24-32` only downloads it when it is missing. The Linux csproj includes it as `<AvaloniaResource Include="..\MyScreenRecorder\Assets\PressStart2P-Regular.ttf" Link="Assets\Fonts\PressStart2P-Regular.ttf" Condition="Exists(...)" />`, so `avares://MyScreenRecorder/Assets/Fonts#Press Start 2P` resolves (probe: rendered, screenshot checked) and a checkout without the file still builds (same `Exists` pattern as the WPF csproj `:34`). No second copy of the file, no download in 2-1: `build.sh` and its font download belong to 4-1 (architecture §5.13). Rejected: copying the 118 KB file into the Linux project (two copies drift); a download target in MSBuild (network at build time).
- 2026-11-16: **Font fallback deviates from architecture §5.12 and the doc is amended.** §5.12 says the fallback is `monospace`. Probe: with the font resource absent, `FontFamily="avares://MyScreenRecorder/Assets/Fonts#Press Start 2P, monospace"` renders in the system default sans font, not in a monospace font; `FontFamily="NoSuchFont, monospace"` also renders sans, while `monospace` alone renders DejaVu Sans Mono. So a missing family in a comma list falls to the default font, not to the next entry. The window still opens and the text is readable. The story keeps the §5.12 family string (it costs nothing and is the right text if a later Avalonia version honours the list) and states the real behaviour: **fallback = Avalonia default font**. Architecture §5.12 was edited during planning to say so (listed in the File List). Rejected: a different family string per probe result (the only working form, `monospace` alone, drops the pixel font).
- 2026-11-16: **Known gap, out of scope (listed so nobody mistakes it for covered): a `DISPLAY` that is set but unreachable.** Probe: `DISPLAY=:99` and `DISPLAY=" "` both end in an unhandled `System.Exception: XOpenDisplay failed` (at `Avalonia.X11.AvaloniaX11Platform.Initialize`) and exit code 134 (SIGABRT). FR9 and AC2 cover only an unset or empty `DISPLAY`. The crash handler of §5.12 and the status messages belong to 3-1; 3-1 must decide whether this case gets a message and exit code 1. Not handled here to keep `Program.Main` to the specified path.
- 2026-11-16: **Tests.** Unit tests (`SessionCheckTests`, `SessionCheckProcessEnvironmentTests`) run everywhere. Process-level tests (`LinuxAppProcessTests`) carry `[Trait("Category", "Integration")]` (D10; first use of the trait in the repo: `grep -rn Integration tests --include=*.cs` was empty at `d1750c1`) because they need the built app. The no-`DISPLAY` rows need no X server and no extra tool; the two window rows skip (`Assert.SkipUnless`) when `DISPLAY` is empty, `xwininfo` is not on `PATH`, or `xwininfo -root` does not exit 0 within 10 s (unreachable X server; added 2026-11-18, QA finding 4), and use `xwininfo -name "MY SCREEN RECORDER"` (exact title match, probe: a title with an extra character is not found). All tests that change the process environment are in the non-parallel collection `ProcessEnvironmentCollection` (a copy of the Core.Tests definition, new namespace); the process tests set the child's environment through `ProcessStartInfo.Environment` and never change their own.
- 2026-11-16: **What is not automated, and why.** The text and colour of the Wayland window (AC1's visible part) and the pixel font are checked by the manual run MR2/MR1 only: the dev machine has no tool the tests may rely on to read window pixels (`xwd`, `import`, `ffmpeg` are absent; ffmpeg arrives with 4-1/2-3), and Avalonia exposes no text dump on X11. The automated part of AC1 is the decision (`SessionCheck` rows) plus the window-opens test with `XDG_SESSION_TYPE=wayland`. The survivors this leaves in `App`/`MainWindow` are listed in Guard checks as "killed by manual step".
- 2026-11-16: **Windows/CI consequence.** `.github/workflows/release.yml` runs only `./build.ps1` (`release.yml:23`), and `build.ps1:34` publishes only `src\MyScreenRecorder`; neither builds the solution nor runs tests. The new projects therefore do not touch the Windows release. No workflow runs any test today (NFR4 says "CI runs them"; no story in the epic adds that, see risk below). `dotnet build MyScreenRecorder.sln` on Windows would also build the three Linux projects; the Avalonia packages are cross-platform and the `linux-x64` apphost pack is a NuGet download, but this was **not** verified (no Windows machine); DoD item 2 is checked on Linux only.
- 2026-11-16: Acceptance-criteria numbering: story AC1–AC3 = epic AC1–AC3 (exact inputs added); AC4 (decision table), AC5 (projects, solution, dependency direction, build and tests) and AC6 (Windows and Core unchanged) are added.

## Acceptance Criteria

1. **Wayland: the window shows the D1 message**
   **Given** the app is started on this X11 machine with `XDG_SESSION_TYPE=wayland` (or with `WAYLAND_DISPLAY=wayland-0`) and `DISPLAY` set
   **When** the main window opens
   **Then** its title is `MY SCREEN RECORDER` and its only content is the red text `ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.` (no green title text); `SessionCheck` returns `Wayland` for every input of rows S6–S11 with that message; the window-opens test W2 passes; the text is confirmed by manual step MR2.

2. **No `DISPLAY`: message on stderr, exit code 1**
   **Given** the app is started with `DISPLAY` unset or empty (also with Wayland variables set)
   **When** the process runs
   **Then** it exits with code 1, stderr is exactly `ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.` plus a newline, stdout is empty, and no window is created and no Avalonia code runs (tests P1–P4, manual step MR3).

3. **X11: the window opens**
   **Given** an X11 session (`DISPLAY` set, `XDG_SESSION_TYPE` `x11`, no `WAYLAND_DISPLAY`)
   **When** the app starts
   **Then** a window titled `MY SCREEN RECORDER` appears (test W1: `xwininfo -name` finds it within 30 s), 640 px wide, black, with the green pixel-font text `MY SCREEN RECORDER` (manual step MR1); closing the window ends the process with exit code 0.

4. **`SessionCheck` decision table**
   **Given** each environment of Testing Requirements group S (rows S1–S22), the message rows and the dictionary rows N1–N3
   **Then** `Evaluate` returns the kind and message of the table; `ReadProcessEnvironment` returns all three keys with the real values or `null` (rows E1, E2).

5. **Projects, solution, direction, build**
   **Given** the three new projects exactly as in Contract Guardrail
   **Then** `MyScreenRecorder.sln` lists them (`src/` and `tests/` solution folders, same configuration mappings as the existing projects), `dotnet build MyScreenRecorder.sln -c Debug` has 0 errors and 0 warnings, `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` passes with 333 tests (301 existing + 32 new), and `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` passes with 39 tests (7 Integration; the two window tests are skipped when there is no reachable X11 display). Counts after the QA fixes of 2026-11-18; the first implementation had 331 and 36 (6 Integration).

6. **Windows and Core unchanged**
   `git diff --stat -- src/MyScreenRecorder src/MyScreenRecorder.Core tests/MyScreenRecorder.Core.Tests` prints nothing; `.github/workflows/release.yml` and `build.ps1` are not changed.

## Tasks / Subtasks

- [x] Task 1: Branch and prerequisites (AC: all)
  - [x] `~/.dotnet/dotnet --version` (or `dotnet --version`) → `10.0.x`. If missing, stop and ask.
  - [x] `git branch --show-current` → `feat/linux-2-1-linux-projects-skeleton`; `git log --oneline -1` shows `d1750c1` or a descendant that contains it; `git config branch.feat/linux-2-1-linux-projects-skeleton.merge` prints nothing; `git branch -vv` shows no upstream. If not, stop and ask.
  - [x] `git status --short` shows only the pre-existing edits ` M CLAUDE.md`, ` M docs/implementation/1-4-recorder-controller.md`, ` M docs/implementation/sprint-status.yaml` and the files of this planning step (this story file, `docs/planning/linux-build-architecture.md`). Leave them; the File List names them as pre-existing.
  - [x] `echo "DISPLAY=$DISPLAY XDG_SESSION_TYPE=$XDG_SESSION_TYPE"` shows an X11 session; `which xwininfo` finds it and `xwininfo -root` exits 0. Without them the two window tests skip and the manual steps cannot run: stop and tell the user.
  - [x] `git diff d1750c1 -- src/MyScreenRecorder.Core src/MyScreenRecorder tests/MyScreenRecorder.Core.Tests` prints nothing (the baseline for AC6).
  - [x] `dotnet build MyScreenRecorder.sln -c Debug` → record the warning count (planning probe: 0); `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` passes with 301 tests before any change.
- [x] Task 2: Projects and solution (AC: 5, 6)
  - [x] CREATE `src/MyScreenRecorder.Platform.Linux/MyScreenRecorder.Platform.Linux.csproj`, `src/MyScreenRecorder.Linux/MyScreenRecorder.Linux.csproj`, `tests/MyScreenRecorder.Platform.Linux.Tests/MyScreenRecorder.Platform.Linux.Tests.csproj` with the exact text of Contract Guardrail.
  - [x] `dotnet sln MyScreenRecorder.sln add --solution-folder src src/MyScreenRecorder.Platform.Linux src/MyScreenRecorder.Linux` and `dotnet sln MyScreenRecorder.sln add --solution-folder tests tests/MyScreenRecorder.Platform.Linux.Tests`. `git diff MyScreenRecorder.sln` shows only three `Project`/`EndProject` pairs, 36 configuration lines and three `NestedProjects` lines; the BOM and the existing entries are unchanged. (Planning probe: exactly that.)
  - [x] First `dotnet restore` downloads the Avalonia packages (about 30 s, network needed); it does not fail with `NU1xxx`. If a package or version cannot be restored, stop and ask.
- [x] Task 3: Tests first (AC: 2–4) — write them before Tasks 4 and 5; they do not compile until the types exist.
  - [x] `tests/MyScreenRecorder.Platform.Linux.Tests/ProcessEnvironmentCollection.cs`, `SessionCheckTests.cs` (S1–S22, MSG1–MSG3, N1–N3, E1–E2, `EnvironmentScope`; ES1–ES2 added 2026-11-18), `LinuxAppProcessTests.cs` (P1–P4, W1–W2; P5 added 2026-11-18). Reference texts of all three are in Contract Guardrail (they were compiled and run during planning: 36 tests pass against the reference implementation).
- [x] Task 4: `SessionCheck` (AC: 1, 2, 4) — `src/MyScreenRecorder.Platform.Linux/SessionCheck.cs` (CREATE): the contract and text of Contract Guardrail. No `[SupportedOSPlatform]`, no docstrings, no comments (D7).
- [x] Task 5: Linux app (AC: 1–3) — CREATE `Program.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs` in `src/MyScreenRecorder.Linux/` (text in Contract Guardrail). `Program.Main` evaluates the session first and returns 1 before any Avalonia code runs; the Avalonia start is the separate method `StartAvalonia`, so `Main` references no Avalonia type.
- [x] Task 6: Make the tests green, then the guard checks (AC: 1–5)
  - [x] `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` after each task; fix the code, never the expectation, unless the expectation contradicts its named source (then stop and ask).
  - [x] Guard checks (Testing Requirements → Guard checks) on a copy, one mutation at a time, each with the full test run of the project (Integration included); record the failing test and message of each in the Dev Agent Record.
- [x] Task 7: Manual run on this machine (AC: 1–3): MR1–MR5 of Testing Requirements; record the observations (and the screenshot command used) in the Dev Agent Record. After MR4 `git status` shows no change under `src/MyScreenRecorder/Assets/`.
- [x] Task 8: Docs (AC: 5)
  - [x] `CLAUDE.md` (UPDATE; it already has uncommitted edits: keep them): Commands: add `dotnet run --project src/MyScreenRecorder.Linux` (Linux, X11) and `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` (needs the app built; `Integration` tests open a window on the desktop); Architecture: one paragraph for the three Linux projects (what lives where, direction `Linux app → Platform.Linux → Core`, `SessionCheck`, exit code 1 without `DISPLAY`, the Wayland message in the window, the font linked from the WPF `Assets/`, fallback = Avalonia default font, the unreachable-`DISPLAY` gap). Keep the existing sentence that Core changes nothing for the Linux build.
  - [x] Do not touch `README.md` (nothing user-facing is released; the Linux section is 4-1) and `build.ps1`/`release.yml`.
- [x] Task 9: Verification and handover (AC: 5, 6)
  - [x] `dotnet build MyScreenRecorder.sln -c Debug --no-incremental` → 0 errors, 0 warnings.
  - [x] `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` three times in a row → `total: 331, failed: 0` (333 after the QA fixes of 2026-11-18).
  - [x] `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` three times → `total: 36, failed: 0, skipped: 0` on the dev machine (39 after the QA fixes of 2026-11-18).
  - [x] `git diff --stat -- src/MyScreenRecorder src/MyScreenRecorder.Core tests/MyScreenRecorder.Core.Tests .github build.ps1` prints nothing (AC6).
  - [x] Comment check: `git diff -U0 | grep -E '^\+\s*(#|//|--)'` plus the comments of all new files (`grep -rnE '^\s*(//|<!--)' src/MyScreenRecorder.Linux src/MyScreenRecorder.Platform.Linux tests/MyScreenRecorder.Platform.Linux.Tests --include=*.cs --include=*.axaml --include=*.csproj`); the expected result is none (D7). Exception added 2026-11-18: the one-line comment above `Program.StartAvalonia`, which gives a non-obvious reason (D7 allows that).
  - [x] Walk the Definition of Done (`docs/planning/epics-linux-build.md` §3) item by item in the handover: 1 ACs with test names and manual steps; 2 build (warning count before/after); 3 tests (both commands, counts); 4 guard checks; 5 comments; 6 `CLAUDE.md`; 7 not applicable (no WPF/Core file changed, proven by the empty diff stat); 8 not applicable (no §5 smoke item exists for 2-1; MR1–MR5 replace it); 9 nothing committed.
  - [x] Doc-claims: run the `doc-claims-check` skill on the sentences added to `CLAUDE.md`.
  - [x] Error log: every error found while implementing (own analysis, test, lint, review) becomes an entry via `python3 /home/timmy/.agents/skills/learn-from-review/scripts/error_log.py add '<JSON>'` (global rule).
  - [x] Nothing is staged, committed or pushed without the user's explicit permission.

## Dev Notes

### Boundaries (documented: architecture §3.2, §3.3, D9; nothing is derived, nothing moves)

| Boundary | Status | Holds for this story |
|---|---|---|
| `src/MyScreenRecorder.Core` (`net10.0`, BCL + `NAudio.Core`) | documented | **Unchanged.** Referenced by both Linux libraries. If a Core type is missing or a Core member must change: stop and report. |
| `src/MyScreenRecorder` (WPF, `net10.0-windows`) | documented | **Unchanged.** Only its font file is read by a link from the Linux app. |
| `src/MyScreenRecorder.Platform.Linux` (`net10.0`, no RID) | documented, new | `SessionKind`, `SessionCheckResult`, `SessionCheck` (public; reads the process environment only in `ReadProcessEnvironment`). Later: launcher (2-2), pipe factory and ffmpeg options (2-3), PulseAudio capture and catalog (2-4), Xlib interop (3-4). No Avalonia reference. |
| `src/MyScreenRecorder.Linux` (`net10.0`, `linux-x64`, `AssemblyName MyScreenRecorder`) | documented, new | `Program` (composition root start: session check, exit path, Avalonia start), `App`, `MainWindow`, the theme resources, the linked font. It is the only project that references Avalonia. Later: `PlatformServices`, `RecorderController` wiring, catalog, preview, region, overlays (3-x). |
| `tests/MyScreenRecorder.Platform.Linux.Tests` (`net10.0`) | documented, new | Unit tests for `SessionCheck`; `Integration` process tests that start the built Linux app as a black box. References Platform.Linux (compile) and the Linux app (build order only, `ReferenceOutputAssembly=false`). |

Dependency direction after the story: `Linux app → {Platform.Linux, Core, Avalonia}`; `Platform.Linux → Core`; `tests → Platform.Linux` (+ build order after the app). Nothing references the WPF project; the WPF project and Core gain no reference; no cycle. Core remains free of UI and OS APIs (§3.3). The one non-compile edge (tests → app, build order) is a decision of this story (Decision Record).

### Technical Requirements
- `net10.0`, `Nullable enable`, `ImplicitUsings disable` (explicit `using`s), in all three projects. Exact package versions: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Simple` 12.1.4; `xunit.v3` 4.0.2. No other package. Transitive packages seen in the probe (`dotnet list package --include-transitive`): `Avalonia.X11`, `Avalonia.Skia`, `Avalonia.FreeDesktop`, `Avalonia.Native`, `Avalonia.Win32` (all 12.1.4, cross-platform bundle of `Avalonia.Desktop`), `SkiaSharp` 3.119.4, `HarfBuzzSharp` 8.3.1.3, `Avalonia.BuildServices` 11.3.2, `Tmds.DBus.Protocol` 0.94.1, `MicroCom.Runtime` 0.11.6.
- Avalonia 12 specifics used: `AppBuilder.Configure(Func<App>)`, `.UsePlatformDetect()`, `StartWithClassicDesktopLifetime`, `<SimpleTheme />` from `Avalonia.Themes.Simple` in the default XAML namespace, `RequestedThemeVariant`, `SizeToContent`, `CanResize` (all verified by the probe build). Architecture §5.12: `SystemDecorations` is `WindowDecorations` in 12 (not used here).
- xUnit analyzer `xUnit1051` (warning) requires `TestContext.Current.CancellationToken` for every call that takes a `CancellationToken` (probe: 4 warnings without it). `Assert.SkipUnless` is used for the window tests (xunit.v3 4.0.2).
- No comments and no docstrings in new code (D7); `SessionCheck` is not a native wrapper and not a Core interface.

### ⚠️ Trap 1 — The Linux app's apphost needs `DOTNET_ROOT`
Running `bin/.../MyScreenRecorder` from a shell or from the test host without `DOTNET_ROOT` prints "You must install .NET to run this application" and the test sees a wrong exit code and an empty window list. `dotnet run` and the manual steps with `dotnet run` are not affected; the process tests are (Decision Record gives the helper). If a process test fails with that text, the helper's root is wrong.

### ⚠️ Trap 2 — Exit code 1 must come before Avalonia
A `DISPLAY` that is unset or empty does not make Avalonia fail politely: the same call ends in an unhandled `XOpenDisplay failed` and exit 134 (probe: guard removed, `DISPLAY` unset or empty; a set but unreachable `DISPLAY=:99` ends the same way). The guard `if (session.Kind == SessionKind.NoDisplay)` must therefore stay the first statement path in `Main`. Guard check P3m removes it and the tests must see 134 instead of 1. The `AppBuilder` chain must also stay out of `Main` (method `StartAvalonia`): compiling `Main` loads every assembly it references, so with the chain inline a build without `Avalonia*.dll` exits 134 on the NoDisplay path (P5, guard P6m). `[MethodImpl(MethodImplOptions.NoInlining)]` is not needed: probes with `AggressiveInlining`, `DOTNET_TieredCompilation=0`, Debug and Release all exit 1.

### ⚠️ Trap 3 — Stderr text and newline
`Console.Error.WriteLine` writes `\n` on Linux; the test compares `NoDisplayMessage + "\n"`. Reading stdout and stderr must start before `WaitForExit` or a full pipe can block the child (the reference test starts both reads first).

### ⚠️ Trap 4 — The window tests open real windows
`W1`/`W2` open a window on the user's desktop (Integration only). Run them one at a time (they are in one class, so xunit runs them sequentially) and always kill the child in `finally`. `xwininfo -name` also matches a window of another program with the same title; none exists on this machine.

### ⚠️ Trap 5 — Environment variable tests
`SessionCheckProcessEnvironmentTests` and `SessionCheckTests.N3` change the process environment (`XDG_SESSION_TYPE`, `WAYLAND_DISPLAY`, `DISPLAY`), and `EnvironmentScopeTests` (ES1, ES2) change two test-only variables `MSR_ENVIRONMENT_SCOPE_*`: they are in `ProcessEnvironmentCollection` (no parallelism), save the originals in the constructor body and restore them in `Dispose` (`EnvironmentScope`), per the `CLAUDE.md` rule for tests that redirect `HOME`/`XDG_*`. There is no `Assert.SkipWhen` in those classes, no temp directory and no permission change, so the rest of that rule has nothing to guard here; if a skip is ever added, create the scope after it. `Environment.SetEnvironmentVariable(name, "")` is not used in-process (empty-value semantics differ by platform); empty values are covered through the child process (P2, P4).

### ⚠️ Trap 6 — Busy-flag/resource rule for the process tests
The `CLAUDE.md` rule (a resource acquired and released in `finally`: every later call that can throw goes inside the `try`) applies to the child processes of `LinuxAppProcessTests`: every call after `Process.Start` that waits on or reads from a child that may still run (stream reads, `WaitForExit`, `xwininfo` polling) is inside a `try` whose `finally` kills a still-running child and waits for it; in the no-`DISPLAY` helper the assertions that read the streams run after the `finally`, once the child has exited; the same holds for the `xwininfo` children (`RunXwininfoAsync`, whose 10 s timeout is set up before `Process.Start`). The other parts of that rule (busy flags, awaits that yield to a user, `Dispose` clearing events) have no counterpart in this story: no command, no event, no dialog.

### ⚠️ Trap 7 — A missing font is not an error
With the font file absent the build still succeeds (the `Condition`) and the window opens in the default font (probe). Do not add a build error or a runtime check for it; MR4 shows the behaviour. After MR4 restore the file with `git checkout -- src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf` and rebuild with `--no-incremental`.

### ⚠️ Trap 8 — Do not widen the surface
No `PlatformServices`, no `RecorderController`, no launcher, no ffmpeg, no settings, no status line, no buttons, no overlay, no `Screens` usage in this story. `Yellow` and the red/green colours exist as resources only so 3-1 does not redefine them. The Linux app does not use any Core type in 2-1 (the reference is the skeleton's dependency direction).

### Reference map: what the window takes from the WPF files (kept / dropped, line by line)

| WPF source | 2-1 | Reason |
|---|---|---|
| `Theme.xaml:4-8` five colours | kept (resources `BgColor`…`YellowColor`) | same values, FR parity |
| `Theme.xaml:10-14` five brushes | kept (`BgBrush`…`YellowBrush`) | 3-1 uses them |
| `Theme.xaml:16` `PixelFont` (`pack://application:,,,/Assets/#Press Start 2P, Consolas`) | kept as `avares://MyScreenRecorder/Assets/Fonts#Press Start 2P, monospace` | Consolas does not exist on Linux (§5.12); fallback behaviour per Decision Record |
| `Theme.xaml:18-26` `AppWindow` style: Background, Foreground, FontFamily, FontSize 10 | kept as attributes of `MainWindow` | |
| `Theme.xaml:23-25` `SnapsToDevicePixels`, `UseLayoutRounding`, `TextFormattingMode` | dropped | WPF properties; Avalonia lays out on device pixels by default |
| `Theme.xaml:28-122` styles for `TextBlock`, `ToolTip`, `Button`, `ToggleButton` | dropped | no such controls in 2-1; 3-1 |
| `MainWindow.xaml:6-7` Title, `Width 640`, `SizeToContent Height`, `ResizeMode NoResize`, `CenterScreen` | kept (`CanResize False`) | |
| `MainWindow.xaml:8` margin `10,6,10,10`, `:8-9` title text `FontSize 12`, green, centred, margin `0,0,0,8` | kept on X11; hidden on Wayland | |
| everything else in `MainWindow.xaml` (cyclers, preview, meters, buttons, status) | dropped | 3-1 |
| `MainWindow.xaml.cs:407` error text in `RedBrush` | kept for the Wayland message | |
| `App.xaml` `StartupUri`, merged `Theme.xaml` | replaced by `App.axaml` styles/resources and `OnFrameworkInitializationCompleted` | Avalonia has no `StartupUri` |
| `App.xaml.cs` dispatcher and `AppDomain` unhandled-exception message boxes | dropped | §5.12 crash handler is 3-1 |
| `app.manifest` (PerMonitorV2), `__COMPAT_LAYER` | dropped | Windows only |

### Contract Guardrail

Public shape (members and exact texts are the contract; a member that seems missing: stop and ask):

```csharp
// MyScreenRecorder.Platform.Linux (src/MyScreenRecorder.Platform.Linux/SessionCheck.cs)
public enum SessionKind { X11, Wayland, NoDisplay }
public sealed record SessionCheckResult(SessionKind Kind, string? Message);
public static class SessionCheck
{
    public const string SessionTypeVariable = "XDG_SESSION_TYPE";
    public const string WaylandDisplayVariable = "WAYLAND_DISPLAY";
    public const string DisplayVariable = "DISPLAY";
    public const string WaylandMessage = "ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.";
    public const string NoDisplayMessage = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";
    public static SessionCheckResult Evaluate(IReadOnlyDictionary<string, string?> environment);
    public static IReadOnlyDictionary<string, string?> ReadProcessEnvironment();
}
```

Reference texts (compiled and run during planning: build 0 warnings, 36 tests pass). Test code may be adapted; the guard checks still apply. The test files below (`SessionCheckTests.cs`, `LinuxAppProcessTests.cs`) are the starting texts: the repository files are authoritative, and the later test changes are in the test tables (W1/W2 skip, P5, ES1/ES2) and in the Dev Agent Record (QA fixes 2026-11-18).

`src/MyScreenRecorder.Platform.Linux/MyScreenRecorder.Platform.Linux.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>MyScreenRecorder.Platform.Linux</RootNamespace>
    <AssemblyName>MyScreenRecorder.Platform.Linux</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyScreenRecorder.Core\MyScreenRecorder.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="MyScreenRecorder.Platform.Linux.Tests" />
  </ItemGroup>

</Project>
```

`src/MyScreenRecorder.Platform.Linux/SessionCheck.cs`
```csharp
using System;
using System.Collections.Generic;

namespace MyScreenRecorder.Platform.Linux;

public enum SessionKind
{
    X11,
    Wayland,
    NoDisplay,
}

public sealed record SessionCheckResult(SessionKind Kind, string? Message);

public static class SessionCheck
{
    public const string SessionTypeVariable = "XDG_SESSION_TYPE";
    public const string WaylandDisplayVariable = "WAYLAND_DISPLAY";
    public const string DisplayVariable = "DISPLAY";
    public const string WaylandMessage = "ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.";
    public const string NoDisplayMessage = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";

    public static SessionCheckResult Evaluate(IReadOnlyDictionary<string, string?> environment)
    {
        if (string.IsNullOrEmpty(Get(environment, DisplayVariable)))
        {
            return new SessionCheckResult(SessionKind.NoDisplay, NoDisplayMessage);
        }

        var wayland = string.Equals(Get(environment, SessionTypeVariable), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Get(environment, WaylandDisplayVariable));
        return wayland
            ? new SessionCheckResult(SessionKind.Wayland, WaylandMessage)
            : new SessionCheckResult(SessionKind.X11, null);
    }

    public static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        return new Dictionary<string, string?>
        {
            [SessionTypeVariable] = Environment.GetEnvironmentVariable(SessionTypeVariable),
            [WaylandDisplayVariable] = Environment.GetEnvironmentVariable(WaylandDisplayVariable),
            [DisplayVariable] = Environment.GetEnvironmentVariable(DisplayVariable),
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string?> environment, string name)
    {
        return environment.TryGetValue(name, out var value) ? value : null;
    }
}
```

`src/MyScreenRecorder.Linux/MyScreenRecorder.Linux.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>MyScreenRecorder</AssemblyName>
    <RootNamespace>MyScreenRecorder.Linux</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.1.4" />
    <PackageReference Include="Avalonia.Desktop" Version="12.1.4" />
    <PackageReference Include="Avalonia.Themes.Simple" Version="12.1.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MyScreenRecorder.Core\MyScreenRecorder.Core.csproj" />
    <ProjectReference Include="..\MyScreenRecorder.Platform.Linux\MyScreenRecorder.Platform.Linux.csproj" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="..\MyScreenRecorder\Assets\PressStart2P-Regular.ttf" Link="Assets\Fonts\PressStart2P-Regular.ttf" Condition="Exists('..\MyScreenRecorder\Assets\PressStart2P-Regular.ttf')" />
  </ItemGroup>

</Project>
```

`src/MyScreenRecorder.Linux/Program.cs`
```csharp
using System;
using Avalonia;
using MyScreenRecorder.Platform.Linux;

namespace MyScreenRecorder.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var session = SessionCheck.Evaluate(SessionCheck.ReadProcessEnvironment());
        if (session.Kind == SessionKind.NoDisplay)
        {
            Console.Error.WriteLine(session.Message);
            return 1;
        }

        return StartAvalonia(session, args);
    }

    // Kept out of Main: compiling a method loads the assemblies it references, and the NoDisplay exit must not depend on Avalonia loading.
    private static int StartAvalonia(SessionCheckResult session, string[] args)
    {
        return AppBuilder.Configure(() => new App { Session = session })
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
```

`src/MyScreenRecorder.Linux/App.axaml`
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MyScreenRecorder.Linux.App"
             RequestedThemeVariant="Dark">
    <Application.Styles>
        <SimpleTheme />
    </Application.Styles>
    <Application.Resources>
        <ResourceDictionary>
            <Color x:Key="BgColor">#000000</Color>
            <Color x:Key="FgColor">#F0F0F0</Color>
            <Color x:Key="GreenColor">#00FF66</Color>
            <Color x:Key="RedColor">#FF2244</Color>
            <Color x:Key="YellowColor">#FFCC00</Color>
            <SolidColorBrush x:Key="BgBrush" Color="{StaticResource BgColor}" />
            <SolidColorBrush x:Key="FgBrush" Color="{StaticResource FgColor}" />
            <SolidColorBrush x:Key="GreenBrush" Color="{StaticResource GreenColor}" />
            <SolidColorBrush x:Key="RedBrush" Color="{StaticResource RedColor}" />
            <SolidColorBrush x:Key="YellowBrush" Color="{StaticResource YellowColor}" />
            <FontFamily x:Key="PixelFont">avares://MyScreenRecorder/Assets/Fonts#Press Start 2P, monospace</FontFamily>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`src/MyScreenRecorder.Linux/App.axaml.cs`
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MyScreenRecorder.Platform.Linux;

namespace MyScreenRecorder.Linux;

public partial class App : Application
{
    public SessionCheckResult Session { get; init; } = new(SessionKind.X11, null);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Session);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

`src/MyScreenRecorder.Linux/MainWindow.axaml`
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MyScreenRecorder.Linux.MainWindow"
        Title="MY SCREEN RECORDER" Width="640" SizeToContent="Height" CanResize="False"
        WindowStartupLocation="CenterScreen"
        Background="{StaticResource BgBrush}" Foreground="{StaticResource FgBrush}"
        FontFamily="{StaticResource PixelFont}" FontSize="10">
    <StackPanel Margin="10,6,10,10">
        <TextBlock x:Name="TitleText" Text="MY SCREEN RECORDER" FontSize="12" Foreground="{StaticResource GreenBrush}"
                   HorizontalAlignment="Center" Margin="0,0,0,8" />
        <TextBlock x:Name="StatusText" TextWrapping="Wrap" LineHeight="16" Foreground="{StaticResource RedBrush}" />
    </StackPanel>
</Window>
```

`src/MyScreenRecorder.Linux/MainWindow.axaml.cs`
```csharp
using Avalonia.Controls;
using MyScreenRecorder.Platform.Linux;

namespace MyScreenRecorder.Linux;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(SessionCheckResult session) : this()
    {
        if (session.Kind == SessionKind.Wayland)
        {
            TitleText.IsVisible = false;
            StatusText.Text = session.Message;
        }
    }
}
```

`tests/MyScreenRecorder.Platform.Linux.Tests/MyScreenRecorder.Platform.Linux.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" Version="4.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\MyScreenRecorder.Platform.Linux\MyScreenRecorder.Platform.Linux.csproj" />
    <ProjectReference Include="..\..\src\MyScreenRecorder.Linux\MyScreenRecorder.Linux.csproj" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyMetadata Include="LinuxAppDirectory" Value="$([MSBuild]::NormalizeDirectory('$(MSBuildThisFileDirectory)', '..', '..', 'src', 'MyScreenRecorder.Linux', 'bin', '$(Configuration)', '$(TargetFramework)', 'linux-x64'))" />
  </ItemGroup>

</Project>
```

`tests/MyScreenRecorder.Platform.Linux.Tests/ProcessEnvironmentCollection.cs`
```csharp
using Xunit;

namespace MyScreenRecorder.Platform.Linux.Tests;

[CollectionDefinition(DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
}
```

`tests/MyScreenRecorder.Platform.Linux.Tests/SessionCheckTests.cs`
```csharp
using System;
using System.Collections.Generic;
using Xunit;

namespace MyScreenRecorder.Platform.Linux.Tests;

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class SessionCheckTests
{
    private const string Wayland = "ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.";
    private const string NoDisplay = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";

    [Theory]
    [InlineData("x11", null, ":0", SessionKind.X11)]
    [InlineData("tty", null, ":0", SessionKind.X11)]
    [InlineData(null, null, ":0", SessionKind.X11)]
    [InlineData("", "", ":0", SessionKind.X11)]
    [InlineData("x11", "", ":0", SessionKind.X11)]
    [InlineData("wayland", null, ":0", SessionKind.Wayland)]
    [InlineData("Wayland", null, ":0", SessionKind.Wayland)]
    [InlineData("WAYLAND", null, ":0", SessionKind.Wayland)]
    [InlineData(null, "wayland-0", ":0", SessionKind.Wayland)]
    [InlineData("x11", "wayland-0", ":0", SessionKind.Wayland)]
    [InlineData("wayland", "wayland-0", ":0", SessionKind.Wayland)]
    [InlineData(" wayland", null, ":0", SessionKind.X11)]
    [InlineData("wayland ", null, ":0", SessionKind.X11)]
    [InlineData("waylandx", null, ":0", SessionKind.X11)]
    [InlineData("xwayland", null, ":0", SessionKind.X11)]
    [InlineData(null, " ", ":0", SessionKind.Wayland)]
    [InlineData("x11", null, " ", SessionKind.X11)]
    [InlineData("x11", null, null, SessionKind.NoDisplay)]
    [InlineData("x11", null, "", SessionKind.NoDisplay)]
    [InlineData(null, null, null, SessionKind.NoDisplay)]
    [InlineData("wayland", null, "", SessionKind.NoDisplay)]
    [InlineData("wayland", "wayland-0", null, SessionKind.NoDisplay)]
    public void Evaluate_classifies_the_session(string? sessionType, string? waylandDisplay, string? display, SessionKind expected)
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = sessionType,
            ["WAYLAND_DISPLAY"] = waylandDisplay,
            ["DISPLAY"] = display,
        };

        Assert.Equal(expected, SessionCheck.Evaluate(environment).Kind);
    }

    [Theory]
    [InlineData(SessionKind.X11, null)]
    [InlineData(SessionKind.Wayland, Wayland)]
    [InlineData(SessionKind.NoDisplay, NoDisplay)]
    public void Evaluate_returns_the_message_of_the_kind(SessionKind kind, string? message)
    {
        var environment = kind switch
        {
            SessionKind.X11 => new Dictionary<string, string?> { ["DISPLAY"] = ":0" },
            SessionKind.Wayland => new Dictionary<string, string?> { ["DISPLAY"] = ":0", ["XDG_SESSION_TYPE"] = "wayland" },
            _ => new Dictionary<string, string?>(),
        };

        var result = SessionCheck.Evaluate(environment);

        Assert.Equal(kind, result.Kind);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void Evaluate_treats_absent_keys_as_unset()
    {
        Assert.Equal(SessionKind.NoDisplay, SessionCheck.Evaluate(new Dictionary<string, string?>()).Kind);
        Assert.Equal(SessionKind.X11, SessionCheck.Evaluate(new Dictionary<string, string?> { ["DISPLAY"] = ":0" }).Kind);
    }

    [Fact]
    public void Evaluate_matches_variable_names_exactly()
    {
        var environment = new Dictionary<string, string?>
        {
            ["display"] = ":0",
            ["xdg_session_type"] = "wayland",
        };

        Assert.Equal(SessionKind.NoDisplay, SessionCheck.Evaluate(environment).Kind);
    }

    [Fact]
    public void Evaluate_does_not_read_the_process_environment()
    {
        using var scope = new EnvironmentScope(("DISPLAY", ":0"), ("WAYLAND_DISPLAY", "wayland-0"), ("XDG_SESSION_TYPE", "wayland"));

        Assert.Equal(SessionKind.NoDisplay, SessionCheck.Evaluate(new Dictionary<string, string?>()).Kind);
    }
}

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class SessionCheckProcessEnvironmentTests : IDisposable
{
    private readonly EnvironmentScope _scope;

    public SessionCheckProcessEnvironmentTests()
    {
        _scope = new EnvironmentScope(("XDG_SESSION_TYPE", null), ("WAYLAND_DISPLAY", null), ("DISPLAY", null));
    }

    public void Dispose() => _scope.Dispose();

    [Fact]
    public void ReadProcessEnvironment_maps_each_variable_to_its_own_key()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "type-value");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-value");
        Environment.SetEnvironmentVariable("DISPLAY", "display-value");

        var environment = SessionCheck.ReadProcessEnvironment();

        Assert.Equal("type-value", environment["XDG_SESSION_TYPE"]);
        Assert.Equal("wayland-value", environment["WAYLAND_DISPLAY"]);
        Assert.Equal("display-value", environment["DISPLAY"]);
    }

    [Fact]
    public void ReadProcessEnvironment_reports_unset_variables_as_null()
    {
        var environment = SessionCheck.ReadProcessEnvironment();

        Assert.True(environment.ContainsKey("XDG_SESSION_TYPE"));
        Assert.True(environment.ContainsKey("WAYLAND_DISPLAY"));
        Assert.True(environment.ContainsKey("DISPLAY"));
        Assert.Null(environment["XDG_SESSION_TYPE"]);
        Assert.Null(environment["WAYLAND_DISPLAY"]);
        Assert.Null(environment["DISPLAY"]);
    }
}

internal sealed class EnvironmentScope : IDisposable
{
    private readonly (string Name, string? Original)[] _saved;

    public EnvironmentScope(params (string Name, string? Value)[] values)
    {
        _saved = new (string, string?)[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            _saved[i] = (values[i].Name, Environment.GetEnvironmentVariable(values[i].Name));
        }

        foreach (var (name, value) in values)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in _saved)
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
```

`tests/MyScreenRecorder.Platform.Linux.Tests/LinuxAppProcessTests.cs`
```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyScreenRecorder.Platform.Linux.Tests;

[Trait("Category", "Integration")]
public sealed class LinuxAppProcessTests
{
    private const string NoDisplayMessage = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";
    private const string WindowTitle = "MY SCREEN RECORDER";

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData(null, "wayland", "wayland-0")]
    [InlineData("", "x11", "")]
    public async Task Without_DISPLAY_the_app_prints_the_message_to_stderr_and_exits_with_1(string? display, string? sessionType, string? waylandDisplay)
    {
        var start = CreateStartInfo(environment =>
        {
            Set(environment, "DISPLAY", display);
            Set(environment, "XDG_SESSION_TYPE", sessionType);
            Set(environment, "WAYLAND_DISPLAY", waylandDisplay);
        });

        using var process = Process.Start(start)!;
        bool exited;
        Task<string> stderr;
        Task<string> stdout;
        try
        {
            stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            exited = process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        Assert.True(exited, "the app did not exit within 30 s");
        Assert.Equal(1, process.ExitCode);
        Assert.Equal(NoDisplayMessage + "\n", await stderr);
        Assert.Equal(string.Empty, await stdout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wayland")]
    public async Task On_an_X11_display_the_app_opens_a_window_titled_MY_SCREEN_RECORDER(string? sessionType)
    {
        Assert.SkipUnless(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")), "needs an X11 session (DISPLAY)");
        Assert.SkipUnless(OnPath("xwininfo"), "needs xwininfo on PATH");

        var start = CreateStartInfo(environment => Set(environment, "XDG_SESSION_TYPE", sessionType));
        using var process = Process.Start(start)!;
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            var found = false;
            while (!found && DateTime.UtcNow < deadline && !process.HasExited)
            {
                found = await XwininfoFindsAsync(WindowTitle);
                if (!found)
                {
                    await Task.Delay(250, TestContext.Current.CancellationToken);
                }
            }

            Assert.True(found, "no window titled '" + WindowTitle + "' appeared; app exited=" + process.HasExited + " stderr=" + (process.HasExited ? await stderr : "<running>"));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit();
        }
    }

    private static ProcessStartInfo CreateStartInfo(Action<IDictionary<string, string?>> edit)
    {
        var directory = typeof(LinuxAppProcessTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "LinuxAppDirectory").Value!;
        var executable = Path.Combine(directory, "MyScreenRecorder");
        Assert.True(File.Exists(executable), "the Linux app is not built: " + executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        start.Environment["DOTNET_ROOT"] = dotnetRoot;
        edit(start.Environment);
        return start;
    }

    private static void Set(IDictionary<string, string?> environment, string name, string? value)
    {
        if (value is null)
        {
            environment.Remove(name);
        }
        else
        {
            environment[name] = value;
        }
    }

    private static bool OnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(d => File.Exists(Path.Combine(d, name)));
    }

    private static async Task<bool> XwininfoFindsAsync(string title)
    {
        var start = new ProcessStartInfo("xwininfo") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-name");
        start.ArgumentList.Add(title);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(output, error);
        return process.ExitCode == 0;
    }
}
```

Contract with later stories:
- 2-2, 2-3, 2-4 add classes to Platform.Linux (they already reference Core) and tests to the same test project; the assembly name, namespace `MyScreenRecorder.Platform.Linux` and `InternalsVisibleTo("MyScreenRecorder.Platform.Linux.Tests")` stay.
- 3-1 builds the `PlatformServices` and the controller in `Program`/`App` **after** `SessionCheck`; it must disable REC and show `SessionCheck.WaylandMessage` in the status line when `Kind == Wayland` (D1; this story's window shows the message as its only content), and decide the unreachable-`DISPLAY` case (Decision Record). `Program.Main` keeps returning the lifetime's exit code; exit code 1 stays reserved for the session check.
- 4-1 adds the font download to `build.sh` (the file is already tracked) and the publish properties; it must keep the `AvaloniaResource` link and the `Condition`.
- Whoever adds the first native wrapper (2-2) decides how `[SupportedOSPlatform("linux")]` is applied without `CA1416` warnings in the Linux app and the tests (Decision Record).

### File Structure Requirements
- `src/MyScreenRecorder.Platform.Linux/MyScreenRecorder.Platform.Linux.csproj`, `SessionCheck.cs` (CREATE)
- `src/MyScreenRecorder.Linux/MyScreenRecorder.Linux.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs` (CREATE)
- `tests/MyScreenRecorder.Platform.Linux.Tests/MyScreenRecorder.Platform.Linux.Tests.csproj`, `ProcessEnvironmentCollection.cs`, `SessionCheckTests.cs`, `LinuxAppProcessTests.cs` (CREATE)
- `MyScreenRecorder.sln` (UPDATE: three projects through `dotnet sln add`)
- `CLAUDE.md` (UPDATE: Commands, Architecture)
- `src/MyScreenRecorder/**`, `src/MyScreenRecorder.Core/**`, `tests/MyScreenRecorder.Core.Tests/**`, `README.md`, `build.ps1`, `.github/**`, `global.json`, `.gitignore` are **not** changed (`bin/`, `obj/` patterns already cover the new projects; `global.json` already enables Microsoft Testing Platform).

### Testing Requirements

Sources: **§5.6** = architecture §5.6 `SessionCheck` rule (Wayland if `XDG_SESSION_TYPE` equals `wayland` case-insensitively or `WAYLAND_DISPLAY` is non-empty; no X if `DISPLAY` is empty); **D1** = architecture §2 D1; **story** = a decision of this story's Decision Record (no external owner exists for the value).

#### Group S — `SessionCheckTests.Evaluate_classifies_the_session` (dictionary with all three keys; `null` = value `null`)

| # | `XDG_SESSION_TYPE` | `WAYLAND_DISPLAY` | `DISPLAY` | Kind | Source |
|---|---|---|---|---|---|
| S1 | `x11` | null | `:0` | X11 | §5.6 (neither Wayland condition) |
| S2 | `tty` | null | `:0` | X11 | §5.6 |
| S3 | null | null | `:0` | X11 | §5.6 |
| S4 | `` (empty) | `` | `:0` | X11 | §5.6 "non-empty" |
| S5 | `x11` | `` | `:0` | X11 | §5.6 |
| S6 | `wayland` | null | `:0` | Wayland (Wayland session with XWayland: `DISPLAY` set) | §5.6, D1 |
| S7 | `Wayland` | null | `:0` | Wayland | §5.6 "case-insensitive" |
| S8 | `WAYLAND` | null | `:0` | Wayland | §5.6 |
| S9 | null | `wayland-0` | `:0` | Wayland (both Wayland and X variables set) | §5.6 |
| S10 | `x11` | `wayland-0` | `:0` | Wayland (the two signals disagree: `WAYLAND_DISPLAY` wins) | §5.6 "or" |
| S11 | `wayland` | `wayland-0` | `:0` | Wayland | §5.6 |
| S12 | ` wayland` | null | `:0` | X11 (no trim) | story |
| S13 | `wayland ` | null | `:0` | X11 (no trim) | story |
| S14 | `waylandx` | null | `:0` | X11 (prefix inside a longer word) | §5.6 "equals" |
| S15 | `xwayland` | null | `:0` | X11 (suffix) | §5.6 "equals" |
| S16 | null | ` ` (one space) | `:0` | Wayland (non-empty is not trimmed) | story |
| S17 | `x11` | null | ` ` (one space) | X11 (non-empty is not trimmed) | story |
| S18 | `x11` | null | null | NoDisplay | §5.6, D1 |
| S19 | `x11` | null | `` | NoDisplay | §5.6, D1 |
| S20 | null | null | null | NoDisplay | §5.6, D1 |
| S21 | `wayland` | null | `` | NoDisplay (precedence over Wayland) | story, D1 |
| S22 | `wayland` | `wayland-0` | null | NoDisplay (Wayland without XWayland) | story, D1 |

#### Group MSG, N, E, ES (`SessionCheckTests`, `SessionCheckProcessEnvironmentTests`, `EnvironmentScopeTests`)

| # | Input | Expected | Source |
|---|---|---|---|
| MSG1 | `{DISPLAY=":0"}` | `Kind X11`, `Message null` | story |
| MSG2 | `{DISPLAY=":0", XDG_SESSION_TYPE="wayland"}` | `Kind Wayland`, `Message` equals the literal `ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.` | D1 (literal in the test, not the constant) |
| MSG3 | `{}` | `Kind NoDisplay`, `Message` equals the literal `ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.` | story |
| N1 | `{}`; `{DISPLAY=":0"}` | NoDisplay; X11 (absent key = unset) | story |
| N2 | `{display=":0", xdg_session_type="wayland"}` (lower-case keys) | NoDisplay (names are case-sensitive) | story; env names are case-sensitive on Linux |
| N3 | process environment set to `DISPLAY=:0`, `WAYLAND_DISPLAY=wayland-0`, `XDG_SESSION_TYPE=wayland`; `Evaluate({})` | NoDisplay (the method reads only its argument) | "injected, not read inside" (global Assumptions) |
| E1 | process environment `XDG_SESSION_TYPE=type-value`, `WAYLAND_DISPLAY=wayland-value`, `DISPLAY=display-value`; `ReadProcessEnvironment()` | each key maps to its own value (three distinct values catch a swap) | story |
| E2 | all three unset; `ReadProcessEnvironment()` | the dictionary contains all three keys (asserted first) and each value is `null` | story |
| ES1 | `EnvironmentScopeTests`: variable A = `before`, variable B unset; scope sets A = `inside`, B = `inside-too`; `Dispose` | inside the scope the new values; after `Dispose` A = `before`, B unset (`null`) | Trap 5 (the scope restores the originals) |
| ES2 | variable A = `before`; scope sets A to `null` (unset); `Dispose` | inside `null`; after `Dispose` A = `before` | Trap 5; the same shape as `SessionCheckProcessEnvironmentTests`, which unsets `DISPLAY` |

#### Group P/W — `LinuxAppProcessTests` (`[Trait("Category","Integration")]`; real apphost, child environment only)

| # | Child environment | Expected | Source |
|---|---|---|---|
| P1 | `DISPLAY` removed | exit code 1; stderr = `NoDisplayMessage + "\n"` (literal text of the Decision Record); stdout empty; exits within 30 s | epic AC2, D1 |
| P2 | `DISPLAY=""` | same as P1 | epic AC2 |
| P3 | `DISPLAY` removed, `XDG_SESSION_TYPE=wayland`, `WAYLAND_DISPLAY=wayland-0` | same as P1 (the NoDisplay text, not the Wayland text) | story (precedence) |
| P4 | `DISPLAY=""`, `XDG_SESSION_TYPE=x11`, `WAYLAND_DISPLAY=""` | same as P1 | epic AC2 |
| P5 | a copy of the app directory with every `Avalonia*.dll` deleted (the test asserts it deleted at least one); `DISPLAY` removed | same as P1 | Trap 2 (exit 1 must not depend on Avalonia; QA finding 1) |
| W1 | inherited environment, `XDG_SESSION_TYPE` and `WAYLAND_DISPLAY` both removed (definite X11 environment; needs `DISPLAY`, `xwininfo` on `PATH` and a reachable X server, that is `xwininfo -root` exits 0, else skipped; `DISPLAY=:99` skips both W rows, QA finding 4) | `xwininfo -name "MY SCREEN RECORDER"` succeeds with `Map State: IsViewable` within 30 s; the child is killed afterwards | epic AC3 |
| W2 | as W1 with `XDG_SESSION_TYPE=wayland` (`WAYLAND_DISPLAY` still removed) | same window title, viewable | epic AC1 (window opens; the text is MR2) |

Planning run (this machine): `total: 36, failed: 0` (Debug) and `total: 6` for `--filter-trait "Category=Integration"` in Debug and in Release.

#### Manual run (Task 7) — commands from the repository root; X11 session, no other program owning a window titled `MY SCREEN RECORDER`

| # | Command | Expected observation |
|---|---|---|
| MR1 | `dotnet run --project src/MyScreenRecorder.Linux` (then close the window with the title-bar button; `echo $?` in the same shell if run in the foreground) | a 640 px wide window titled `MY SCREEN RECORDER`; black body; the text `MY SCREEN RECORDER` centred, green, in the blocky pixel font; nothing else; exit code 0 after closing |
| MR2 | `XDG_SESSION_TYPE=wayland dotnet run --project src/MyScreenRecorder.Linux`; then the same with `env -u XDG_SESSION_TYPE WAYLAND_DISPLAY=wayland-0 dotnet run --project src/MyScreenRecorder.Linux` | the same window frame and title; the body shows only the red text `ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.` in the pixel font, wrapped on two lines without overlapping, no green title text |
| MR3 | `env -u DISPLAY dotnet run --project src/MyScreenRecorder.Linux; echo "exit=$?"`; then `DISPLAY= dotnet run --project src/MyScreenRecorder.Linux; echo "exit=$?"` | no window; stderr line `ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.`; `exit=1`, both times |
| MR4 | `mv src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf /tmp/psp.ttf && dotnet build src/MyScreenRecorder.Linux --no-incremental && XDG_SESSION_TYPE=wayland dotnet run --no-build --project src/MyScreenRecorder.Linux`; afterwards `git checkout -- src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf && dotnet build src/MyScreenRecorder.Linux --no-incremental` | build succeeds with 0 warnings; the window opens and the message is readable in a normal (non-pixel) system font; after the restore `git status --short src/MyScreenRecorder/Assets` prints nothing |
| MR5 | `pgrep -a MyScreenRecorder` after closing the windows of MR1/MR2 | prints nothing (no leftover process) |

Screenshot for the record (optional, planning used it): `python3 -I -c "from PIL import ImageGrab; ImageGrab.grab(xdisplay=':0').save('shot.png')"` while the window is open (needs Pillow).

#### Guard checks (run each once on a copy; each must fail the named test, for that reason; record the actual failure messages; "planning run" is what happened on the reference implementation in the planner's copy)

Every branch and statement with an observable effect of the new methods:

| # | Mutation (one at a time) | Must fail | Planning run |
|---|---|---|---|
| M1 | `Evaluate`: NoDisplay check `if (false)` | S18–S22 | killed: 9 tests (S18–S22, MSG3, N1, N2, N3) |
| M2 | `OrdinalIgnoreCase` → `Ordinal` | S7, S8 | killed: S7, S8 only |
| M3 | remove the `WAYLAND_DISPLAY` clause | S9, S10, S16 | killed: S9, S10, S16 |
| M4 | remove the `XDG_SESSION_TYPE` clause (`&& false`) | S6–S8, MSG2 | killed: S6, S7, S8, MSG2 |
| M5 | `||` → `&&` | S6–S10, S16, MSG2 | killed: 7 tests (S6–S10, S16, MSG2) |
| M6 | `.Trim()` on the `XDG_SESSION_TYPE` value | S12, S13 | killed: S12, S13 |
| M7 | `IsNullOrEmpty` → `IsNullOrWhiteSpace` for `DISPLAY` | S17 | killed: S17 only |
| M7b | the same for `WAYLAND_DISPLAY` | S16 | not run (symmetric to M7; run it) |
| M8 | `Get` falls back to `Environment.GetEnvironmentVariable(name)` for an absent key | N3 (also N1, N2, MSG3) | killed: N1, N2, N3, MSG3 |
| M9 | change a word of `WaylandMessage` | MSG2 | killed: MSG2 |
| M9b | change a word of `NoDisplayMessage` | MSG3 and P1–P4 | not run (run it) |
| M9c | `X11` result with a non-null message | MSG1 | not run (run it) |
| M10 | move the NoDisplay check after the Wayland check | S21, S22 | not run (run it) |
| M11 | `ReadProcessEnvironment`: `XDG_SESSION_TYPE` reads `WAYLAND_DISPLAY` | E1 | killed: E1 |
| M12 | `ReadProcessEnvironment`: drop the `WAYLAND_DISPLAY` entry | E1, E2 | killed: E1, E2 |
| M12b | drop or swap the `DISPLAY` entry | E1, E2 | not run (run it) |
| P1m | `Program.Main`: `return 1` → `return 0` | P1–P4 | killed: 4 tests, `Expected: 1 Actual: 0` |
| P2m | `Console.Error` → `Console.Out` | P1–P4 | killed: stderr `""`, stdout not empty |
| P3m | NoDisplay branch `if (false)` | P1–P4 (exit 134); P5 too since 2026-11-18 | killed: `Expected: 1 Actual: 134` |
| P4m | `Program.Main`: pass an empty dictionary to `Evaluate` instead of `ReadProcessEnvironment()` | W1, W2 (NoDisplay → no window), P-rows still pass | not run (run it) |
| P5m | `App.OnFrameworkInitializationCompleted`: do not assign `MainWindow` | W1, W2 | not run (run it) |
| W1m | `MainWindow` `Title` changed by one character | W1, W2 | killed: both, `no window titled 'MY SCREEN RECORDER' appeared` |
| P6m | `Program.Main`: the `AppBuilder` chain back inline instead of `return StartAvalonia(session, args)` | P5 (exit 134) | added 2026-11-18 (QA finding 1) |
| ES1m | `EnvironmentScope.Dispose`: restore statement removed | ES1, ES2 | added 2026-11-18 (QA finding 3) |
| ES2m | `EnvironmentScope` constructor saves the new value instead of the original | ES1, ES2 | added 2026-11-18 (QA finding 3) |
| W2m | window test: the `xwininfo -root` skip removed; run with `DISPLAY=:99` | W1, W2 fail instead of skipping | added 2026-11-18 (QA finding 4) |

Survivors that the automated suite cannot reach and the manual steps kill (list each as "out of reach" with its step in the handover): `App.Session` not passed in `Program.Main` (`new App { Session = session }` → `new App()`): Wayland window shows no message → MR2; `MainWindow` branch `session.Kind == SessionKind.Wayland` removed or negated → MR1 (title block missing) and MR2; `TitleText.IsVisible = false` removed → MR2 (green title still present); `StatusText.Text = session.Message` removed → MR2; `Foreground` of `StatusText` changed → MR2 (not red); `LineHeight` removed → MR2 (lines overlap); font family string changed → MR1/MR4; `Width`/`SizeToContent`/`CanResize` changed → MR1 (width not 640). Each of these is run once by hand on a copy and the observation recorded.
A guard that no test reaches is either dead or untested: build an input that reaches it before deleting it; stop and ask if none exists.

### Previous Story Intelligence (1-1 to 1-4, done)
- Test-first worked: write the tests, see them fail to compile, then add the types.
- Guard checks were run on a copy with `--no-incremental` and a full test run per mutation; reviews found untested branches every time, so the guard list names a test for every branch (the manual-only survivors above are the only exceptions and are named).
- `dotnet test` needs the repo-root `global.json` (Microsoft Testing Platform): `dotnet test --solution ... --filter-not-trait "Category=Integration"` and `dotnet test --project ...` are the forms that work (VSTest mode is refused on SDK 10). Do not add `Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`.
- Tests that change the process environment belong in `ProcessEnvironmentCollection` (new copy in the new test project; the Core.Tests one is not shared).
- Core.Tests uses `Assert.SkipWhen(...)` for platform-dependent tests (`RecordingSessionTests.cs:34`); the same family (`Assert.SkipUnless`) is used here for "needs X11 and xwininfo".
- Reviewer subagents hit an API rate limit in 1-3; if that happens, say so in Review Findings.
- Hand-offs to 2-1 from earlier stories: none. Hand-offs 2-1 gives to 3-1: Contract Guardrail ("Contract with later stories").

### Git Intelligence Summary
- `d1750c1` merge PR #4 (1-4, `RecorderController`) is HEAD of `origin/main` · `3d2d000` tracker commit for 1-2 · `df93a35` merge PR #3 (1-2) · `75765fa` merge PR #2 (1-3) · `60a23cc` merge PR #1 (1-1; created `global.json`, `MyScreenRecorder.sln`, Core and Core.Tests). `src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf` was added in `8763bc0` and never changed. No commit touches `release.yml`/`build.ps1` since the 1-x stories began.

### Latest Stack Information
- .NET SDK 10.0.403 (user install in `~/.dotnet`), runtime 10.0.14; `net10.0`. Avalonia 12.1.4 (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Simple`); `xunit.v3` 4.0.2 on Microsoft Testing Platform; `NAudio.Core` 2.2.1 (Core only). No `Directory.Build.props`/`Directory.Packages.props` exists: versions are written in each csproj.
- Runtime dependencies seen by the probe app on this machine: fontconfig (default font), libX11/libxcb (Avalonia.X11 via XWayland or Xorg). Documenting them for users is 4-1.

### Probe log (planner's scratchpad copy of the repository; the repository was not modified)
1. `curl -s https://api.nuget.org/v3-flatcontainer/<id>/index.json` for `avalonia`, `avalonia.desktop`, `avalonia.themes.simple`, `avalonia.themes.fluent`, `avalonia.fonts.inter`, `avalonia.x11`, `avalonia.skia`, `avalonia.headless`, `avalonia.headless.xunit` → all list `12.1.4` (and `12.1.5`, `12.1.6`).
2. Probe repo = `rsync` of the working tree without `.git`, `bin`, `obj` plus the three projects of Contract Guardrail; `dotnet build -c Debug` of the app: 0 warnings, 0 errors (first restore 33 s). A first version with `App(SessionCheckResult)` gave `AVLN3001`.
3. `DOTNET_ROOT=$HOME/.dotnet bin/.../linux-x64/MyScreenRecorder` on `DISPLAY=:0`: `xwininfo -name "MY SCREEN RECORDER"` found the window (640 wide); screenshot (Pillow `ImageGrab`, crop) shows black body and green pixel-font title. Without `DOTNET_ROOT`: "You must install .NET to run this application".
4. `XDG_SESSION_TYPE=wayland`: window shows the red message in the pixel font; without `LineHeight` the wrapped second line overlapped the first; with `LineHeight="16"` it did not.
5. `env -u DISPLAY`, `DISPLAY=`, and `env -u DISPLAY XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY=wayland-0`: exit 1, stderr = NoDisplay text, stdout empty. `env -u DISPLAY dotnet run --project src/MyScreenRecorder.Linux` → `exit=1`. `DISPLAY=:99` and `DISPLAY=" "`: `System.Exception: XOpenDisplay failed`, exit 134.
6. Font absent (renamed in the copy, `dotnet build -o` to another folder): build 0 warnings, window opens, text in the default sans font. Family probes with `iiiiii WWWWWW` lines: `monospace` and `Monospace` → DejaVu Sans Mono; `NoSuchFont, monospace` → default sans.
7. `dotnet sln add --solution-folder` ×2 on the copy: 3 projects, 36 configuration lines, 3 nested-project lines; `dotnet build MyScreenRecorder.sln -c Debug` 0 warnings, 0 errors (the WPF project builds on Linux through `EnableWindowsTargeting`); `dotnet test --solution MyScreenRecorder.sln --filter-not-trait "Category=Integration"` → `total: 331`.
8. `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` → 36 passed (Debug), integration 6 passed (Debug and Release); with the app's `bin/` deleted the test command rebuilds the app. Probe test inside the host: `DOTNET_ROOT` and `DOTNET_HOST_PATH` empty (`DOTNET_ROOT_X64` was not checked then; `dotnet test` sets it, QA finding 2), `RuntimeEnvironment.GetRuntimeDirectory()` = `/home/timmy/.dotnet/shared/Microsoft.NETCore.App/10.0.14/`.
9. `[SupportedOSPlatform("linux")]` on `SessionCheck`: 10 `CA1416` warnings (2 in `Program.cs`, 8 in the tests).
10. Mutants of the Guard checks table marked "killed" were run on the copy as described (full project test run per mutant).
11. `dotnet list src/MyScreenRecorder.Linux package --include-transitive`: see Technical Requirements.

### References
- [Source: `docs/planning/epics-linux-build.md` - §1 FR9, NFR4; §2 Working rules; §3 DoD; §5 Linux smoke checklist (no item applies to 2-1); §6 story 2-1 and 2-2, 2-4, 3-1, 4-1; §7 Sequencing]
- [Source: `docs/planning/linux-build-architecture.md` - §2 D1, D3, D7, D9, D10; §3.2 target projects; §3.3 dependency rules; §5.6 `SessionCheck`; §5.12 Linux UI (Avalonia 12.1.4, font, crash handling); §5.13 build and release; §6 verified facts]
- [Source: `CLAUDE.md` - Commands, Architecture, Conventions (the two lines added by the uncommitted edit: HOME/XDG test rule, resource/busy-flag rule)]
- [Source: `src/MyScreenRecorder/Theme.xaml:4-26`, `MainWindow.xaml:6-9`, `MainWindow.xaml.cs:407`, `App.xaml`, `App.xaml.cs`, `MyScreenRecorder.csproj:33-34`, `build.ps1:24-35`, `.github/workflows/release.yml:23-29` at `d1750c1`]
- [Source: `src/MyScreenRecorder.Core/MyScreenRecorder.Core.csproj`, `tests/MyScreenRecorder.Core.Tests/MyScreenRecorder.Core.Tests.csproj`, `ProcessEnvironmentCollection.cs`, `MyScreenRecorder.sln`, `global.json` at `d1750c1`]
- [Source: `docs/implementation/1-4-recorder-controller.md`, `1-2-recording-pipeline-into-core.md` - format, Decision Record, Traps, Guard checks]

## Dev Agent Record

### Agent Model Used

Claude Sonnet 5.5 (`claude-sonnet-5-5`), Claude Code. Baseline `d1750c17bc856a428f3af0325e068c062cc2d501` (branch `feat/linux-2-1-linux-projects-skeleton`).

### Debug Log References

- Before: `dotnet build MyScreenRecorder.sln -c Debug` 0 warnings, 0 errors; `dotnet test --solution ... --filter-not-trait "Category=Integration"` total 301, failed 0.
- Red step: tests written, `SessionCheck.cs` held back: `dotnet build tests/MyScreenRecorder.Platform.Linux.Tests` 5 errors (`CS0246`/`CS0234`, `SessionCheckResult` and `MyScreenRecorder.Platform.Linux` missing). Then `SessionCheck.cs` restored: `total: 36, failed: 0`.
- After: `dotnet build MyScreenRecorder.sln -c Debug --no-incremental` 0 warnings, 0 errors (also `-c Release`: 0/0). `git diff MyScreenRecorder.sln`: 45 insertions only (3 Project/EndProject pairs, 36 configuration lines, 3 nested-project lines).
- Non-integration solution run, three times: `total: 331, failed: 0, skipped: 0` each time. `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests` three times: `total: 36, failed: 0, skipped: 0` each time; `--filter-trait "Category=Integration"`: `total: 6, failed: 0`.
- AC6: `git diff --stat -- src/MyScreenRecorder src/MyScreenRecorder.Core tests/MyScreenRecorder.Core.Tests .github build.ps1` prints nothing; `git status --short src/MyScreenRecorder/Assets` prints nothing.
- Leftover processes after all runs: none (`pgrep -f` on the apphost path).
- Comment check: `git diff -U0 | grep -E '^\+\s*(#|//|--)'` hits only the pre-existing tracker header line `# generated: ...` (not added by this story); the grep over the new projects finds no comment.
- Guard checks ran on a copy under the session scratchpad (`dev-2-1/repo`, driver `dev-2-1/mut.py`), `dotnet build --no-incremental` then `dotnet test --no-build` per mutant; every run showed `total: 36` and compiled.

### Completion Notes List

- Implemented exactly the Contract Guardrail texts. One test change against the reference (W1/W2 and `XwininfoFindsAsync`, see Deviations).
- Manual steps run from the shell (apphost built in Debug; `dotnet run --no-build` for MR3):
  - MR1: `xwininfo -name` on the window: `Width: 640`, `Height: 52`, `Map State: IsViewable`; pixel grab of the window area (PIL `ImageGrab`): most common colour `(0,0,0)` 31906 px, `(0,255,102)` (= `#00FF66`) 690 px, screenshot shows the green blocky text `MY SCREEN RECORDER` centred; `wmctrl -c "MY SCREEN RECORDER"` closed it, process exit code 0.
  - MR2 (`XDG_SESSION_TYPE=wayland`, and `env -u XDG_SESSION_TYPE WAYLAND_DISPLAY=wayland-0`): `Width: 640`, `Height: 48`, viewable; grab: `(255,34,68)` (= `#FF2244`) 926 px, no `(0,255,102)` pixel; screenshot shows two wrapped lines `ERROR: Wayland session is not supported. Log in with an Xorg` / `(X11) session.` in the pixel font, no overlap; close exit code 0.
  - MR3: `env -u DISPLAY`, `DISPLAY=` and `env -u DISPLAY XDG_SESSION_TYPE=wayland WAYLAND_DISPLAY=wayland-0` with `dotnet run --no-build`: stderr `ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.`, `exit=1`, stdout 0 bytes (checked on the third), no window.
  - MR4 on the copy (not in the repo, so AC6 stays untouched): font file moved away, `dotnet build --no-incremental` 0 warnings, Wayland window opens at `Height: 32`, screenshot shows the message in a normal system sans font, one line; file restored and rebuilt.
  - MR5: no leftover `MyScreenRecorder` process.
  - Manual-only survivors, each run once on the copy with `dev-2-1/man.py` (build, start, `xwininfo` geometry, pixel counts; baselines: X11 640x52 with 690 green px; Wayland 640x48 with 926 red px): A `new App()` instead of `new App { Session = session }` -> Wayland window h52, 690 green, 0 red; B negated Wayland branch -> Wayland run h52/green, X11 run h32 with no green; C `IsVisible = false` removed -> h68, green and red; D `StatusText.Text` removed -> h32, 0 red; E Foreground Green -> 0 red, 1233 green; F `LineHeight` removed -> h36; G `FontFamily="monospace"` -> h32, 88 red px; H1 `Width="600"` -> w600; H2 `SizeToContent="Manual"` -> h200; H3 `CanResize="True"` -> pixel grab was unusable (window area covered), killed instead by `xprop -name ... WM_NORMAL_HINTS`: baseline min = max = 640 by 52, mutant has min 1 by 1 and no max; X1 title `IsVisible="False"` -> h32, 0 green.
- For the user (needs a human eye): confirm the text colour and the blocky Press Start 2P look of MR1/MR2 (pixel counts and screenshots above support it), and the system-font look of MR4: `XDG_SESSION_TYPE=wayland dotnet run --project src/MyScreenRecorder.Linux` and `dotnet run --project src/MyScreenRecorder.Linux`; for MR4 `git stash`-free route: copy the repo, move `src/MyScreenRecorder/Assets/PressStart2P-Regular.ttf`, `dotnet build src/MyScreenRecorder.Linux --no-incremental`, `XDG_SESSION_TYPE=wayland dotnet run --no-build --project src/MyScreenRecorder.Linux`.
- Definition of Done (`docs/planning/epics-linux-build.md` §3):
  1. ACs: AC1 `SessionCheckTests` S6-S11, MSG2, `On_an_X11_display_the_app_opens_a_window_titled_MY_SCREEN_RECORDER(sessionType: wayland)` (W2), MR2 pixel/screenshot; AC2 `Without_DISPLAY_...` P1-P4, MR3; AC3 W1, MR1, close exit 0; AC4 S1-S22, MSG1-3, N1-3, E1-2; AC5 build and counts below; AC6 empty diff stat.
  2. Build: before 0 warnings, 0 errors; after 0 warnings, 0 errors (Debug no-incremental, Release).
  3. Tests: 331/331 three times (301 + 30 new); Platform.Linux.Tests 36/36 three times (6 Integration), 0 skipped.
  4. Guard checks: table below, all mutants of the story run, one test gap found and fixed (P5m).
  5. Comments: none added in code (see Debug Log).
  6. `CLAUDE.md`: Commands, Tests bullet and an Architecture paragraph added; `doc-claims-check` run (verifier sonnet/medium): 8 claims hold, 3 partial (window test count and skip scope, dependency chain missing `Linux app -> Core`, Integration comment) and corrected; 3 external claims (`XOpenDisplay failed` exit 134, font fallback, `DOTNET_ROOT`) are unverifiable from code and rest on the story probes (Decision Record, probe log 3, 5, 6). The corrected sentences were not re-listed with `list-claims.mjs` after the fix.
  7. Not applicable: no WPF/Core file changed (empty diff stat).
  8. Not applicable: no section 5 smoke item exists for 2-1; MR1-MR5 replace it.
  9. Nothing committed, staged or pushed.
- Review: no reviewer subagents were spawned beyond the doc-claims verifier; self-review of the diff only (no Review Findings).

Guard check table (every run compiled, `total: 36`):

| # | Result | Killed by (message) |
|---|---|---|
| M1 | killed, 13 failures | S18-S22, MSG3, N1, N2, N3, P rows (`Assert.Equal() Failure: Expected: 1` for the process rows) |
| M2 | killed | S7, S8 (`Expected: Wayland`) |
| M3 | killed | S9, S10, S16 |
| M4 | killed | S6-S8, MSG2 |
| M5 | killed | S6-S10, S16, MSG2 (7) |
| M6 | killed | S12, S13 (`Expected: X11`) |
| M7 | killed | S17 only |
| M7b | killed | S16 only |
| M8 | killed | N1, N2, N3, MSG3 (`Expected: NoDisplay`) |
| M9 | killed | MSG2 (`Strings differ`) |
| M9b | killed, 5 failures | MSG3, P1-P4 (rows with `DISPLAY` null/empty) |
| M9c | killed | MSG1 (`Expected: null`) |
| M10 | killed | S21, S22, P3 |
| M11 | killed | E1 |
| M12 | killed | E1, E2 |
| M12b | killed | E1, E2, W1, W2 (`no window ... stderr=ERROR: DISPLAY is not set`) |
| M12c (`DISPLAY` reads `WAYLAND_DISPLAY`) | killed | E1, P rows, W1, W2 |
| P1m | killed, 4 | P1-P4 (`Expected: 1 Actual: 0`) |
| P2m | killed, 4 | P1-P4 (stderr empty) |
| P3m | killed, 4 | P1-P4 (exit 134 instead of 1); since 2026-11-18 also P5 (5 failures, doc-claims verifier run) |
| P4m | killed | W1, W2 (`no window titled 'MY SCREEN RECORDER' appeared; app exited=True stderr=ERROR: DISPLAY is not set...`) |
| P5m | survived the reference test (0 failures), killed after the fix | W1, W2 (`no window ... app exited=False stderr=<running>`) |
| W1m | killed | W1, W2 (`no window titled 'MY SCREEN RECORDER' appeared`) |

Survivors: none in the automated suite after the P5m fix. Out of reach of the suite, killed by the manual run on a copy: the `App`/`MainWindow` rows listed above (A-H3, X1). Not killed by anything: `FgBrush`/`BgBrush`/`FontSize` values were not mutated.

Deviations from the story:
- W1 removes `WAYLAND_DISPLAY` as well (orchestrator addition); `LinuxAppProcessTests.cs` sets `WAYLAND_DISPLAY` to removed in both window cases. The story's W1/W2 rows were updated.
- `XwininfoFindsAsync` also requires `Map State: IsViewable` (reference test let the P5m mutant survive; error-log entry 331).
- MR4 ran on a copy, not in the repo, to keep `src/MyScreenRecorder/Assets/` untouched.
- Task 8: the story says to keep an existing sentence that Core changes nothing for the Linux build; `CLAUDE.md` has no such sentence, so nothing was kept or added.
- Task 3 order: the app and `SessionCheck` files were created before the tests were written; the red step was reproduced by holding `SessionCheck.cs` back and building the tests.
- `dotnet build ... --no-incremental` reports an elapsed time of about 2 s on this machine; warnings and errors counts are from that run.

QA fixes 2026-11-18 (agile-dev, Claude Opus 5.5, branch `fix/linux-2-1-qa-findings` from `origin/main` `a55c082`, the merge of PR #5; baseline content equals `410423a`):
- Red: P5 against the unchanged `Program.cs`: `Expected: 1 Actual: 134` (`FileNotFoundException: Avalonia.Controls` at `Program.Main`). Green after moving the chain into `StartAvalonia`.
- Probes on copies without `Avalonia*.dll`, NoDisplay path: `StartAvalonia` without `NoInlining`, with `AggressiveInlining`, Debug and Release, `DOTNET_TieredCompilation` 1 and 0: exit 1 every time, so `NoInlining` was not added.
- `xwininfo -root`: `:0` exit 0; `:99`, `" "`, `nohost.invalid:0` exit 1; `192.0.2.1:0` blocks about 80 s (hence the 10 s timeout).
- `CopyDirectory` recursion dropped (`CopyFiles`): `find bin/{Debug,Release}/net10.0/linux-x64 -mindepth 1 -type d` is empty, so no input reached it.
- Guard checks (copy under the session scratchpad, driver `mut.sh`; each compiled, `total: 39`):

| # | Result | Killed by (message) |
|---|---|---|
| P6m | killed | P5 (`Expected: 1 Actual: 134`) |
| ES1m | killed | ES1 (`Expected: "before" Actual: "inside"`), ES2 (`Expected: "before" Actual: null`) |
| ES2m | killed | ES1, ES2 (same messages) |
| W2m (`DISPLAY=:99`) | killed | W1, W2 fail (child exits 134, no window) instead of skipping |
| W3m `== 0` → `!= 0` on the `-root` skip | killed | `DISPLAY=:99` run: W1, W2 fail (child exits 134, no window); on a live display it shows only as 2 skips |
| T1m `CancelAfter` removed (`DISPLAY=192.0.2.1:0`) | survived on timing | both W cases still skip, after 167 s instead of 22 s (out of reach of an assertion) |

- Verify: `dotnet build MyScreenRecorder.sln -c Debug --no-incremental` and `-c Release --no-incremental` 0 warnings, 0 errors; `dotnet test --solution MyScreenRecorder.sln` three times `total: 340, failed: 0, skipped: 0`; `--filter-not-trait "Category=Integration"` three times `total: 333, failed: 0`; `dotnet test --project tests/MyScreenRecorder.Platform.Linux.Tests --filter-trait "Category=Integration"` `total: 7, failed: 0`; `DISPLAY=:99` 37 passed, 2 skipped. AC6 diff stat empty; no leftover app or `xwininfo` process; no `msr-no-avalonia-*` directory left in `/tmp`.
- Comment check: one comment added, above `Program.StartAvalonia` (non-obvious reason, D7 allows it); the doc-claims verifier found it holds.
- Not re-run in this pass: MR1–MR5 (the window look; no UI file changed) and the Windows build of the solution with the three Linux projects.

### Review Findings

QA 2026-11-16 (agile-qa, report-only; no reviewer subagents; time-boxed): verdict PASS WITH CONCERNS. Build 0 warnings, 0 errors; new project 36/36 (3 runs, about 4.1 s), Integration 6/6 (7 runs, about 4.1 s), no flake, no leftover process; with `DISPLAY` unset or `xwininfo` off `PATH` 34 pass and 2 skip. AC1-AC6, S1-S22, message texts (D1 verbatim), csproj text, sln mappings and folders match the story; no Avalonia in Platform.Linux or the tests; nothing references WPF; empty diff stat for WPF, Core, Core.Tests, `.github`, `build.ps1`; tracker shows `review` with columns aligned; no code comments.

| # | Severity | Where | Finding | Status |
|---|---|---|---|---|
| 1 | Low | `src/MyScreenRecorder.Linux/Program.cs:11` | JIT compiling `Main` loads `Avalonia.Controls` before the `NoDisplay` return. With the Avalonia assemblies deleted from a copy of the build, `env -u DISPLAY` and `DISPLAY=` exit 134 (`FileNotFoundException` at `Program.Main`), not 1. No Avalonia code runs, but "no Avalonia type is touched" (Decision Record, Trap 2) is false at the JIT level. Harmless in a normal build; error log 333. | Fixed 2026-11-18: the chain is in the separate method `StartAvalonia`; `NoInlining` probed as not needed (`AggressiveInlining`, `DOTNET_TieredCompilation=0`, Debug and Release all exit 1); test P5; error log 369. |
| 2 | Low | `LinuxAppProcessTests.cs` `CreateStartInfo` | `dotnet test` sets `DOTNET_ROOT_X64` and the child inherited it, so removing the `DOTNET_ROOT` derivation, or one `..`, survived. The Decision Record says the host has no root variable, true for `DOTNET_ROOT` only. Killed when the test dll runs via `dotnet <dll>` (6 of 6 fail). | Fixed: the helper removes `DOTNET_ROOT_X64` from the child; both mutants now fail under `dotnet test`; error log 332. |
| 3 | Low | `SessionCheckTests.cs` `EnvironmentScope.Dispose` | Removing the restore survives (36/36). Safe today because collections with `DisableParallelization` run after the parallel ones, and `LinuxAppProcessTests` reads `DISPLAY` and builds the child environment. | Fixed 2026-11-18: `EnvironmentScopeTests` (ES1, ES2); mutants ES1m, ES2m killed; error log 370. |
| 4 | Low | `LinuxAppProcessTests.cs` window test | `Assert.SkipUnless` checks only that `DISPLAY` is non-empty. With `DISPLAY=:99` both window cases fail (`XOpenDisplay failed`) instead of skipping. Outside AC scope. | Fixed 2026-11-18: skip unless `xwininfo -root` exits 0 within 10 s; `DISPLAY=:99` and `DISPLAY=192.0.2.1:0` skip both W cases; error log 371. |
| 5 | Info | window tests | `xwininfo -name` also matches another program's window with that title; 15 of 15 probes showed no stale window right after a kill (W2 cannot pass on W1's window). `xwininfo` itself is not killed on cancellation. | Accepted; since 2026-11-18 `RunXwininfoAsync` kills and waits for `xwininfo` in `finally` and stops waiting after 10 s. |
| 6 | Info | docs | The `doc-claims-check` after the CLAUDE.md corrections was not re-run; the new CLAUDE.md sentences were read against the code and hold. Story text still says "ROOT empty" (finding 2) and "no Avalonia type touched" (finding 1). | Fixed 2026-11-18: sentences corrected; `doc-claims-check` re-run over CLAUDE.md, the story and the `Program.cs` comment (0 false, 6 partial corrected); error log 372. |

Mutants of QA (not in the developer's table; each compiled, `total: 36`, run in a copy): killed: `Console.Error.Write` (P1-P4), Wayland returns 1 (W2), no `UsePlatformDetect` (W1, W2), `WaitForExit` 1 ms (P rows), child `DISPLAY` not removed (P rows after 30 s each), `NoDisplayMessage + " "` (MSG3, P rows), `ReadProcessEnvironment` `?? ""` (E2), child `DOTNET_ROOT` removal and one `..` (after fix 2). A mutant removing the window-test `Kill` hangs: the 120 s `timeout` ended it, and the leftover child was killed by hand. Survivors: exit code of the app after the window closes (`+ 1`; MR1 only), `new MainWindow()` without the session (MR2 only), enum order (equivalent), `EnvironmentScope` restore (finding 3). Unmutated and only manual: `RequestedThemeVariant`, colour values, `FgBrush` (invisible in 2-1), `WindowStartupLocation`, margins, `FontSize`.

Fix pass 2026-11-18 (agile-dev; reviewer subagents: Acceptance and Adversarial, plus the `doc-claims-check` verifier):
- [x] [Review][Patch] AC5, Task 3 and Task 9 gave the old counts (331, 36/6) and omitted P5, ES1, ES2 — fixed (333, 39/7).
- [x] [Review][Patch] Decision Record (Tests) and Task 1 named only the old skip conditions — fixed.
- [x] [Review][Patch] New guard rows P6m, ES1m, ES2m, W2m had no recorded result — recorded in the Dev Agent Record.
- [x] [Review][Patch] `RunXwininfoAsync` had no timeout: `DISPLAY=192.0.2.1:0` made each window case wait about 80 s before skipping — 10 s timeout, then skip (22 s for the whole project; 167 s with the mutant T1m).
- [x] [Review][Patch] Trap 6 says the `finally` kills and waits; the P-row helper and `RunXwininfoAsync` killed without waiting — `WaitForExit` added after both kills.
- [x] [Review][Patch] `doc-claims-check` partials: `DOTNET_ROOT_X64` alone also lets the apphost start (CLAUDE.md, Decision Record); the apphost prefers `DOTNET_ROOT_X64` and falls back to global locations; Trap 2 cited the `:99` probe for an unset `DISPLAY` and named P3 instead of P3m; "the two test files" was ambiguous — all reworded.
- [x] [Review][Dismiss] `StartAvalonia` might still be inlined into `Main`: both reviewers and the verifier probed `AggressiveInlining`, `DOTNET_TieredCompilation=0`, `DOTNET_TC_QuickJit=0`, `STRESS_INLINE`, Debug and Release: always exit 1.
- [x] [Review][Dismiss] P5 might fail in the host before `Main` (deps.json still lists Avalonia): it starts; the copy keeps the executable bit; the app directory has no subdirectories.

For the user (needs a human eye): the Press Start 2P look, red/green colours and window centring (MR1, MR2); MR4 font fallback look. MR1-MR3 and MR5 were not re-run here beyond the window and exit-code tests.

### File List

(CREATED) `src/MyScreenRecorder.Platform.Linux/MyScreenRecorder.Platform.Linux.csproj`, `src/MyScreenRecorder.Platform.Linux/SessionCheck.cs`, `src/MyScreenRecorder.Linux/MyScreenRecorder.Linux.csproj`, `Program.cs`, `App.axaml`, `App.axaml.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs` (all in `src/MyScreenRecorder.Linux/`), `tests/MyScreenRecorder.Platform.Linux.Tests/MyScreenRecorder.Platform.Linux.Tests.csproj`, `ProcessEnvironmentCollection.cs`, `SessionCheckTests.cs`, `LinuxAppProcessTests.cs`.
(UPDATED, by this story) `MyScreenRecorder.sln` (three projects added through `dotnet sln add`), `CLAUDE.md` (Commands, Tests bullet, Architecture paragraph, on top of the pre-existing edits), `docs/implementation/2-1-linux-projects-skeleton.md` (this file: W1/W2 rows, tasks, Dev Agent Record, status), `docs/implementation/sprint-status.yaml` (2-1 `review`).

(UPDATED — QA fixes 2026-11-18, branch `fix/linux-2-1-qa-findings`) `src/MyScreenRecorder.Linux/Program.cs` (Avalonia start in `StartAvalonia`), `tests/MyScreenRecorder.Platform.Linux.Tests/LinuxAppProcessTests.cs` (P5, `xwininfo -root` skip with 10 s timeout, waits after kills), `tests/MyScreenRecorder.Platform.Linux.Tests/SessionCheckTests.cs` (`EnvironmentScopeTests`), `CLAUDE.md` (window-test skip, P5, `StartAvalonia`, `DOTNET_ROOT_X64`), `docs/implementation/sprint-status.yaml` (2-1 `in-progress`, then `review`), this file.

Pre-existing, not part of this implementation: `CLAUDE.md` (two uncommitted convention lines; Task 8 adds to the file), `docs/implementation/1-4-recorder-controller.md` (status done), `docs/implementation/sprint-status.yaml` (1-4 and epic-1 done; this planning step also set `2-1-linux-projects-skeleton` to `ready-for-dev` and `epic-2` to `in-progress`), this story file, `docs/planning/linux-build-architecture.md` (amended during planning: §5.12 font fallback sentence and an amendment note at the top).

### Change Log

- 2026-11-16: Story created (ready-for-dev). Epic AC1–AC3 sharpened with exact inputs; AC4–AC6 added. Doc edits made with the story: `docs/planning/linux-build-architecture.md` (§5.12: the pixel-font fallback is Avalonia's default font, probe-verified, instead of `monospace`; amendment note), `docs/implementation/sprint-status.yaml` (2-1 `ready-for-dev`, epic-2 `in-progress`).
- 2026-11-16: Implemented (status review). W1 row now removes `XDG_SESSION_TYPE` and `WAYLAND_DISPLAY` and both window rows require a viewable window (`Map State: IsViewable`); `CLAUDE.md` updated; guard checks and manual run recorded in the Dev Agent Record.
- 2026-11-16: QA review (PASS WITH CONCERNS): 6 findings in Review Findings; `LinuxAppProcessTests.CreateStartInfo` removes `DOTNET_ROOT_X64` from the child environment (finding 2). Status stays `review`.
- 2026-11-18: QA fixes (status in-progress, then review), on the new branch `fix/linux-2-1-qa-findings` because PR #5 was already merged: findings 1, 3, 4, 6 fixed (P5, ES1, ES2, `xwininfo -root` skip); test counts 333 / 39 (7 Integration); AC5, Tasks 1, 3, 9, Decision Record, Traps 2, 5, 6, test and guard tables, Contract Guardrail `Program.cs` text and `CLAUDE.md` corrected; `doc-claims-check` re-run.
