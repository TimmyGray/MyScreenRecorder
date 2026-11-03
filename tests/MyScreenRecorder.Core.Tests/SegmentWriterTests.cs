using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public sealed class SegmentWriterTests : IDisposable
{
    private readonly FakeFfmpeg _ffmpeg;
    private readonly DirectoryInfo _root;
    private readonly FakeAudioCaptureFactory _captures = new();
    private readonly AudioEngine _audio;
    private readonly FakeChildProcessLauncher _launcher = new();
    private readonly FakeAudioPipeFactory _pipes = new();
    private readonly ConcurrentQueue<string> _faults = new();
    private readonly string _partsDir;

    public SegmentWriterTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake ffmpeg is a POSIX shell script");
        _ffmpeg = new FakeFfmpeg();
        _root = Directory.CreateTempSubdirectory("srec-segment-");
        _audio = new AudioEngine(_captures);
        _partsDir = Path.Combine(_root.FullName, "parts");
    }

    public void Dispose()
    {
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
        _root.Delete(recursive: true);
    }

    private SegmentWriter Create(int index = 0, ScreenGrab? grab = null)
    {
        var target = new CaptureTarget(TargetKind.Region, new System.Drawing.Rectangle(0, 0, 1920, 1080), "x", new[] { 1 });
        var writer = new SegmentWriter(_ffmpeg.Path, _partsDir, index, target, VideoQuality.P720, AudioQuality.Aac256, _audio, Guid.NewGuid().ToString("N"), _launcher, _pipes, grab ?? new GdiGrab());
        writer.Faulted += _faults.Enqueue;
        return writer;
    }

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

    [Fact]
    public async Task S1_StartWithoutAudioThenStop_WritesSegmentAndStopsByQ()
    {
        SegmentWriter writer = Create();

        await writer.StartAsync();
        var watch = Stopwatch.StartNew();
        await writer.StopAsync();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "stop must use the q path, not the kill fallback");
        Assert.True(Directory.Exists(_partsDir));
        Assert.Equal(0, _pipes.CallCount);
        Assert.Equal(1, _launcher.Calls);
        ProcessStartInfo psi = _launcher.StartInfos[0];
        Assert.Equal(_ffmpeg.Path, psi.FileName);
        Assert.Equal(writer.FilePath, psi.ArgumentList.Last());
        Assert.Contains("0:v", psi.ArgumentList);
        Assert.Equal("HighDpiAware", psi.Environment["__COMPAT_LAYER"]);
        Assert.Equal(3, new FileInfo(writer.FilePath).Length);
        Assert.Equal(3, writer.FinalBytes);
        Assert.Equal(3, writer.CurrentBytes);
        Assert.Empty(_faults);
    }

    [Fact]
    public async Task S2_CurrentBytes_FollowsTotalSizeProgress()
    {
        SegmentWriter writer = Create();
        await writer.StartAsync();

        bool reached = await WaitUntil(() => writer.CurrentBytes == 1234, 5000);
        await writer.StopAsync();

        Assert.True(reached);
    }

    [Fact]
    public async Task S3_StartWithAudio_CreatesPipeBeforeProcessAndFeedsAudio()
    {
        _audio.SetMic("mic");
        int pipeCallsAtStart = -1;
        _launcher.BeforeStart = _ => pipeCallsAtStart = _pipes.CallCount;
        SegmentWriter writer = Create();

        await writer.StartAsync();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await writer.StopAsync();

        Assert.Equal(1, pipeCallsAtStart);
        Assert.Equal(0, _pipes.Calls[0].Index);
        FakeAudioPipe pipe = Assert.Single(_pipes.Pipes);
        System.Collections.Generic.IList<string> args = _launcher.StartInfos[0].ArgumentList;
        Assert.Equal("-i", args[args.IndexOf(pipe.FfmpegInput) - 1]);
        byte[] written = pipe.WrittenBytes;
        Assert.NotEmpty(written);
        Assert.Equal(0, written.Length % 8);
        Assert.All(written, b => Assert.Equal(0, b));
        Assert.True(pipe.StreamClosed);
        Assert.True(pipe.Disposed >= 1);
    }

    [Fact]
    public async Task S3_PipeIsCreatedWithTheSessionIdAndIndex()
    {
        _audio.SetMic("mic");
        SegmentWriter writer = Create(12);

        await writer.StartAsync();
        await writer.StopAsync();

        (string sessionId, int index) = Assert.Single(_pipes.Calls);
        Assert.Equal(12, index);
        Assert.Matches("^[0-9a-f]{32}$", sessionId);
    }

    [Fact]
    public async Task S4_StopTwiceConcurrently_StopsOnce()
    {
        SegmentWriter writer = Create();
        await writer.StartAsync();

        Task first = writer.StopAsync();
        Task second = writer.StopAsync();
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(1, _launcher.Calls);
    }

    [Fact]
    public async Task S5_StderrLines_GoToFfmpegLogWithSegmentPrefix()
    {
        SegmentWriter writer = Create(12);

        await writer.StartAsync();
        await writer.StopAsync();

        string log = File.ReadAllText(Path.Combine(_partsDir, "ffmpeg.log"));
        Assert.Contains("[seg_012] segment warning" + Environment.NewLine, log);
    }

    [Fact]
    public async Task S6_LauncherThrows_RethrowsDisposesPipeAndLeavesNoProcess()
    {
        _audio.SetMic("mic");
        var boom = new InvalidOperationException("boom");
        _launcher.ThrowOnCall(1, boom);
        SegmentWriter writer = Create();

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync());

        Assert.Same(boom, thrown);
        Assert.True(Assert.Single(_pipes.Pipes).Disposed >= 1);
        Assert.Empty(_launcher.ProcessIds);
        Assert.Empty(_faults);
    }

    [Fact]
    public async Task S15_GrabThrows_DisposesThePipeAndStartsNoProcess()
    {
        _audio.SetMic("mic");
        SegmentWriter writer = Create(grab: new ThrowingGrab());

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync());

        Assert.Equal("grab failed", thrown.Message);
        Assert.Equal(1, Assert.Single(_pipes.Pipes).Disposed);
        Assert.Equal(0, _launcher.Calls);
    }

    private sealed class ThrowingGrab : ScreenGrab
    {
        public override IReadOnlyList<string> InputArgs(System.Drawing.Rectangle bounds, int framerate) => throw new InvalidOperationException("grab failed");

        public override IReadOnlyList<string> EncoderArgs() => Array.Empty<string>();

        public override void ApplyEnvironment(IDictionary<string, string?> environment)
        {
        }
    }

    [Fact]
    public async Task S7_FfmpegExitsBeforePipeConnects_ThrowsAndReleases()
    {
        _ffmpeg.Mode = "exit-early";
        _audio.SetMic("mic");
        _pipes.Behavior = PipeBehavior.NeverConnect;
        SegmentWriter writer = Create();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync());

        Assert.Equal("ffmpeg exited before the audio pipe was connected. See ffmpeg.log in " + _partsDir, ex.Message);
        Assert.True(Assert.Single(_pipes.Pipes).Disposed >= 1);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Empty(_faults);
    }

    [Fact]
    public async Task S8_PipeConnectionFaults_WrapsKillsProcessAndDisposesPipe()
    {
        _audio.SetMic("mic");
        _pipes.Behavior = PipeBehavior.Fault;
        SegmentWriter writer = Create();

        var watch = Stopwatch.StartNew();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync());
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2.5), "the running process must be killed, not waited for until the 3 s limit");
        Assert.Equal("Could not connect the audio pipe to ffmpeg. See ffmpeg.log in " + _partsDir, ex.Message);
        Assert.IsType<IOException>(ex.InnerException);
        int pid = Assert.Single(_launcher.ProcessIds);
        Assert.True(await WaitUntil(() => !FakeChildProcessLauncher.IsAlive(pid), 3000));
        Assert.True(Assert.Single(_pipes.Pipes).Disposed >= 1);
        Assert.Empty(_faults);
    }

    [Fact]
    public async Task S9_PipeNeverConnects_TimesOutAfterAboutTenSeconds()
    {
        _audio.SetMic("mic");
        _pipes.Behavior = PipeBehavior.NeverConnect;
        SegmentWriter writer = Create();

        var watch = Stopwatch.StartNew();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StartAsync());
        watch.Stop();

        Assert.Equal("Could not connect the audio pipe to ffmpeg. See ffmpeg.log in " + _partsDir, ex.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        Assert.InRange(watch.Elapsed.TotalSeconds, 9, 14);
        int pid = Assert.Single(_launcher.ProcessIds);
        Assert.True(await WaitUntil(() => !FakeChildProcessLauncher.IsAlive(pid), 3000));
    }

    [Fact]
    public async Task S10_FfmpegExitsEarlyWithoutAudio_RaisesFaultedOnce()
    {
        _ffmpeg.Mode = "exit-early";
        SegmentWriter writer = Create();

        await writer.StartAsync();
        bool raised = await WaitUntil(() => !_faults.IsEmpty, 5000);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await writer.StopAsync();

        Assert.True(raised);
        Assert.Equal("ERROR: see ffmpeg.log", Assert.Single(_faults));
    }

    [Fact]
    public async Task S10_ExitBetweenPipeConnectAndActivation_RaisesFaultedFromStartAsync()
    {
        _ffmpeg.Mode = "die-soon";
        _audio.SetMic("mic");
        _pipes.StreamGetterDelay = TimeSpan.FromMilliseconds(900);
        SegmentWriter writer = Create();

        await writer.StartAsync();

        Assert.Equal("ERROR: see ffmpeg.log", Assert.Single(_faults));
        await writer.StopAsync();
        Assert.Single(_faults);
    }

    [Fact]
    public async Task S10_ExitedHandlerFiringAgainAfterTheFault_RaisesNoSecondFaulted()
    {
        _ffmpeg.Mode = "exit-early";
        SegmentWriter writer = Create();
        await writer.StartAsync();
        Assert.True(await WaitUntil(() => !_faults.IsEmpty, 5000));

        MethodInfo onExited = typeof(SegmentWriter).GetMethod("OnExited", BindingFlags.Instance | BindingFlags.NonPublic)!;
        onExited.Invoke(writer, new object?[] { null, EventArgs.Empty });
        await writer.StopAsync();

        Assert.Single(_faults);
    }

    [Fact]
    public async Task S11_FfmpegDiesWhileRunning_RaisesFaultedOnceAndStopRaisesNoSecond()
    {
        _ffmpeg.Mode = "die-soon";
        SegmentWriter writer = Create();

        await writer.StartAsync();
        bool raised = await WaitUntil(() => !_faults.IsEmpty, 5000);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await writer.StopAsync();

        Assert.True(raised);
        Assert.Single(_faults);
    }

    [Fact]
    public async Task S12_FfmpegIgnoresQ_IsKilledAfterTheTimeout()
    {
        _ffmpeg.Mode = "ignore-q";
        SegmentWriter writer = Create();
        await writer.StartAsync();

        await writer.StopAsync();

        int pid = Assert.Single(_launcher.ProcessIds);
        Assert.False(FakeChildProcessLauncher.IsAlive(pid));
    }

    [Fact]
    public async Task S13_StopOnNeverStartedWriter_Completes()
    {
        SegmentWriter writer = Create();

        await writer.StopAsync();

        Assert.Equal(0, writer.FinalBytes);
        Assert.Equal(0, _launcher.Calls);
    }

    [Fact]
    public async Task S14_EmptyOutput_ReportsZeroFinalBytes()
    {
        _ffmpeg.Mode = "empty-output";
        SegmentWriter writer = Create();

        await writer.StartAsync();
        await writer.StopAsync();

        Assert.Equal(0, writer.FinalBytes);
    }
}
