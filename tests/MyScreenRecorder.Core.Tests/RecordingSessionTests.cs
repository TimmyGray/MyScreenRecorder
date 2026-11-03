using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class RecordingSessionTests : IDisposable
{
    private readonly string? _home = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly DirectoryInfo _root;
    private readonly FakeFfmpeg _ffmpeg;
    private readonly FakeAudioCaptureFactory _captures = new();
    private readonly AudioEngine _audio;
    private readonly FakeChildProcessLauncher _launcher = new();
    private readonly FakeAudioPipeFactory _pipes = new();
    private readonly List<RecordingState> _states = new();
    private readonly RecordingSession _session;
    private readonly CaptureTarget _target = new(TargetKind.Region, new System.Drawing.Rectangle(0, 0, 1920, 1080), "x", new[] { 1 });

    public RecordingSessionTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake ffmpeg is a POSIX shell script");
        _root = Directory.CreateTempSubdirectory("srec-session-");
        _ffmpeg = new FakeFfmpeg();
        Environment.SetEnvironmentVariable("HOME", Path.Combine(_root.FullName, "home"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_root.FullName, "config"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "home"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "config"));
        _audio = new AudioEngine(_captures);
        _session = NewSession(_ffmpeg.DirectoryPath);
        _session.StateChanged += () => _states.Add(_session.State);
    }

    public void Dispose()
    {
        _session.Dispose();
        _audio.Dispose();
        foreach (int pid in _launcher.ProcessIds.ToArray())
        {
            try
            {
                using Process p = Process.GetProcessById(pid);
                p.Kill(true);
            }
            catch
            {
            }
        }

        _ffmpeg.Dispose();
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(Path.Combine(AppPaths.OutputFolder, ".parts"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            catch
            {
            }
        }

        _root.Delete(recursive: true);
        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _xdgConfigHome);
    }

    private RecordingSession NewSession(string ffmpegDirectory, ScreenGrab? grab = null)
    {
        var options = new FfmpegLocatorOptions("ffmpeg", null, null, Path.Combine(_root.FullName, "extract"));
        var locator = new FfmpegLocator(options, ffmpegDirectory, _ => null);
        return new RecordingSession(_audio, TestPlatform.Create(_captures, _launcher, _pipes, grab ?? new GdiGrab(), options), locator);
    }

    private static string PartsRoot => Path.Combine(AppPaths.OutputFolder, ".parts");

    private static string[] PartsDirs() => Directory.Exists(PartsRoot) ? Directory.GetDirectories(PartsRoot) : Array.Empty<string>();

    private static string[] FinalFiles() => Directory.GetFiles(AppPaths.OutputFolder, "REC_*");

    private async Task Start(AudioQuality aq = AudioQuality.Aac256) => await _session.StartAsync(_target, VideoQuality.P720, aq);

    private static async Task<bool> WaitUntil(Func<bool> condition, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            if (condition()) return true;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return condition();
    }

    private void AssertConcatStartInfo(ProcessStartInfo psi, string partsDir, string finalPath, bool faststart)
    {
        Assert.Equal(_ffmpeg.Path, psi.FileName);
        Assert.Equal(partsDir, psi.WorkingDirectory);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.Null(psi.StandardInputEncoding);
        Assert.Equal(new ProcessStartInfo().Environment.OrderBy(p => p.Key), psi.Environment.OrderBy(p => p.Key));
        var expected = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", "list.txt", "-c", "copy" };
        if (faststart)
        {
            expected.AddRange(new[] { "-movflags", "+faststart" });
        }

        expected.Add(finalPath);
        Assert.Equal(expected, psi.ArgumentList);
    }

    [Fact]
    public async Task R1_FfmpegNotFound_ThrowsAndStaysIdle()
    {
        using RecordingSession session = NewSession(Path.Combine(_root.FullName, "nowhere"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(_target, VideoQuality.P720, AudioQuality.Aac256));

        Assert.Equal("ffmpeg not found", ex.Message);
        Assert.Equal(RecordingState.Idle, session.State);
        Assert.Empty(PartsDirs());
        Assert.Equal(0, _launcher.Calls);
    }

    [Fact]
    public async Task R2_StartTwice_SecondThrows()
    {
        await Start();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start());

        Assert.Equal("A recording is already in progress", ex.Message);
        await _session.CancelAsync();
    }

    [Fact]
    public async Task R3_StartStop_AssemblesTheFinalMp4AndRemovesParts()
    {
        await Start();
        string partsDir = Assert.Single(PartsDirs());
        string stamp = Path.GetFileName(partsDir);

        string? result = await _session.StopAsync();

        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$"), stamp);
        string expectedPath = Path.Combine(AppPaths.OutputFolder, "REC_" + stamp + ".mp4");
        Assert.Equal(expectedPath, result);
        Assert.Equal("seg", File.ReadAllText(expectedPath));
        Assert.False(Directory.Exists(partsDir));
        Assert.Equal(new[] { RecordingState.Recording, RecordingState.Finalizing, RecordingState.Idle }, _states);
        Assert.Equal(2, _launcher.Calls);
        AssertConcatStartInfo(_launcher.StartInfos[1], partsDir, expectedPath, faststart: true);
        Assert.Equal("720P", _session.Stats.QualityLabel);
    }

    [Fact]
    public async Task R4_WithAudio_QualityLabelListsBoth()
    {
        _audio.SetMic("mic");

        await Start();

        Assert.Equal("720P / AAC 256K", _session.Stats.QualityLabel);
        await _session.CancelAsync();
    }

    [Fact]
    public async Task R5_PauseResumeTwice_ConcatenatesThreeSegments()
    {
        await Start();
        string partsDir = Assert.Single(PartsDirs());

        await _session.PauseAsync();
        await _session.ResumeAsync();
        await _session.PauseAsync();
        await _session.ResumeAsync();
        string? result = await _session.StopAsync();

        Assert.Equal(
            new[]
            {
                RecordingState.Recording, RecordingState.Paused, RecordingState.Recording, RecordingState.Paused,
                RecordingState.Recording, RecordingState.Finalizing, RecordingState.Idle
            },
            _states);
        Assert.Equal("segsegseg", File.ReadAllText(result!));
        Assert.Equal(4, _launcher.Calls);
        Assert.Equal(
            new[] { "seg_000.mkv", "seg_001.mkv", "seg_002.mkv" },
            _launcher.StartInfos.Take(3).Select(p => Path.GetFileName(p.ArgumentList.Last())));
        Assert.False(Directory.Exists(partsDir));
    }

    [Fact]
    public async Task R6_OperationsInTheWrongState_AreNoOps()
    {
        await _session.PauseAsync();
        await _session.CancelAsync();
        Assert.Null(await _session.StopAsync());
        await Start();
        await _session.ResumeAsync();

        Assert.Equal(new[] { RecordingState.Recording }, _states);
        Assert.Equal(1, _launcher.Calls);
        await _session.CancelAsync();
    }

    [Fact]
    public async Task R7_WavWithAudio_IsMkvWithoutFaststart()
    {
        _audio.SetMic("mic");
        await Start(AudioQuality.Wav);
        string partsDir = Assert.Single(PartsDirs());

        string? result = await _session.StopAsync();

        Assert.Equal(".mkv", Path.GetExtension(result));
        AssertConcatStartInfo(_launcher.StartInfos[^1], partsDir, result!, faststart: false);
    }

    [Fact]
    public async Task R7_WavWithoutAudio_IsMp4WithFaststart()
    {
        await Start(AudioQuality.Wav);
        string partsDir = Assert.Single(PartsDirs());

        string? result = await _session.StopAsync();

        Assert.Equal(".mp4", Path.GetExtension(result));
        AssertConcatStartInfo(_launcher.StartInfos[^1], partsDir, result!, faststart: true);
    }

    [Fact]
    public async Task R8_CancelWhileRecording_RemovesPartsAndLaunchesNoConcat()
    {
        await Start();
        string partsDir = Assert.Single(PartsDirs());

        await _session.CancelAsync();

        Assert.Equal(RecordingState.Idle, _session.State);
        Assert.False(Directory.Exists(partsDir));
        Assert.Empty(FinalFiles());
        Assert.Equal(1, _launcher.Calls);
    }

    [Fact]
    public async Task R8_CancelWhilePaused_RemovesPartsAndLaunchesNoConcat()
    {
        await Start();
        string partsDir = Assert.Single(PartsDirs());
        await _session.PauseAsync();

        await _session.CancelAsync();

        Assert.Equal(RecordingState.Idle, _session.State);
        Assert.False(Directory.Exists(partsDir));
        Assert.Empty(FinalFiles());
        Assert.Equal(1, _launcher.Calls);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task R9_ConcatFails_KeepsPartsLogsAndReportsError(int segments)
    {
        await Start();
        for (int i = 1; i < segments; i++)
        {
            await _session.PauseAsync();
            await _session.ResumeAsync();
        }

        _ffmpeg.Mode = "concat-fail";
        string partsDir = Assert.Single(PartsDirs());

        string? result = await _session.StopAsync();

        Assert.Null(result);
        Assert.Equal("Could not assemble the final file. Parts kept in " + partsDir, _session.LastError);
        Assert.Contains("[concat] concat boom", File.ReadAllText(Path.Combine(partsDir, "ffmpeg.log")));
        string expectedList = string.Concat(Enumerable.Range(0, segments).Select(i => $"file 'seg_{i:D3}.mkv'" + Environment.NewLine));
        Assert.Equal(new UTF8Encoding(false).GetBytes(expectedList), File.ReadAllBytes(Path.Combine(partsDir, "list.txt")));
        Assert.True(Directory.Exists(partsDir));
        Assert.Equal(RecordingState.Idle, _session.State);
    }

    [Fact]
    public async Task R10_EmptyOutput_ReportsNoVideoDataAndLaunchesNoConcat()
    {
        _ffmpeg.Mode = "empty-output";
        await Start();
        string partsDir = Assert.Single(PartsDirs());

        string? result = await _session.StopAsync();

        Assert.Null(result);
        Assert.Equal("No video data was recorded. Parts folder: " + partsDir, _session.LastError);
        Assert.Equal(1, _launcher.Calls);
    }

    [Fact]
    public async Task R11_LauncherThrowsOnConcat_ReportsFinalizeFailure()
    {
        await Start();
        string partsDir = Assert.Single(PartsDirs());
        _launcher.ThrowOnCall(2, new InvalidOperationException("boom"));

        string? result = await _session.StopAsync();

        Assert.Null(result);
        Assert.Equal("Finalize failed: boom Parts kept in " + partsDir, _session.LastError);
        Assert.Equal(RecordingState.Idle, _session.State);
    }

    [Fact]
    public async Task R12_FirstSegmentFailsToStart_PropagatesAndRemovesParts()
    {
        _launcher.ThrowOnCall(1, new InvalidOperationException("boom"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Start());

        Assert.Equal("boom", ex.Message);
        Assert.Empty(PartsDirs());
        Assert.Equal(RecordingState.Idle, _session.State);
    }

    [Fact]
    public async Task R13_ResumeSegmentFailsToStart_PropagatesKeepsPausedAndSetsLastError()
    {
        await Start();
        await _session.PauseAsync();
        _launcher.ThrowOnCall(2, new InvalidOperationException("resume boom"));

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _session.ResumeAsync());

        Assert.Equal("resume boom", ex.Message);
        Assert.Equal("resume boom", _session.LastError);
        Assert.Equal(RecordingState.Paused, _session.State);
        await _session.CancelAsync();
    }

    [Fact]
    public async Task R14_FfmpegDiesWhileRecording_RaisesFaultedAndReturnsToIdle()
    {
        _ffmpeg.Mode = "die-soon";
        var faults = new List<string>();
        _session.Faulted += faults.Add;

        await Start();
        bool raised = await WaitUntil(() => faults.Count > 0, 10000);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.True(raised);
        Assert.Equal("ERROR: see ffmpeg.log", Assert.Single(faults));
        Assert.Equal("ERROR: see ffmpeg.log", _session.LastError);
        Assert.Equal(RecordingState.Idle, _session.State);
    }

    [Fact]
    public async Task R15_DisposeWhileRecording_StopsTheSegment()
    {
        await Start();
        int pid = Assert.Single(_launcher.ProcessIds);

        _session.Dispose();
        _session.Dispose();

        Assert.False(FakeChildProcessLauncher.IsAlive(pid));
    }

    [Fact]
    public async Task R17_SegmentsUseThePlatformGrab()
    {
        using RecordingSession session = NewSession(_ffmpeg.DirectoryPath, new X11Grab(":0"));

        await session.StartAsync(_target, VideoQuality.P720, AudioQuality.Aac256);
        await session.CancelAsync();

        ProcessStartInfo segment = _launcher.StartInfos[0];
        Assert.Contains("x11grab", segment.ArgumentList);
        Assert.Contains("sliced-threads=1", segment.ArgumentList);
        Assert.DoesNotContain("gdigrab", segment.ArgumentList);
        Assert.False(segment.Environment.ContainsKey("__COMPAT_LAYER"));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task R16_PartsFolderCannotBeDeleted_ReportsLastErrorAfterRetries()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "needs POSIX permissions and an unprivileged process");
        await Start();
        string partsDir = Assert.Single(PartsDirs());
        File.SetUnixFileMode(PartsRoot, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var watch = Stopwatch.StartNew();
        await _session.CancelAsync();
        watch.Stop();

        Assert.Equal("Could not delete " + partsDir, _session.LastError);
        Assert.Equal(RecordingState.Idle, _session.State);
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1), "five attempts with 300 ms between them");
    }
}
