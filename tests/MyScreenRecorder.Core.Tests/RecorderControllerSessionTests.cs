using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class RecorderControllerSessionTests : IDisposable
{
    private readonly string? _home = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly DirectoryInfo _root;
    private readonly FakeFfmpeg _ffmpeg;
    private readonly FakeAudioCaptureFactory _captures = new();
    private readonly AudioEngine _audio;
    private readonly FakeChildProcessLauncher _launcher = new();
    private readonly RecordingSession _session;
    private readonly ControllerFixture _f;

    public RecorderControllerSessionTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake ffmpeg is a POSIX shell script");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _root = Directory.CreateTempSubdirectory("srec-ctlsession-");
        _ffmpeg = new FakeFfmpeg();
        Environment.SetEnvironmentVariable("HOME", Path.Combine(_root.FullName, "home"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(_root.FullName, "config"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "home"));
        Directory.CreateDirectory(Path.Combine(_root.FullName, "config"));
        _audio = new AudioEngine(_captures);
        var options = new FfmpegLocatorOptions("ffmpeg", null, null, Path.Combine(_root.FullName, "extract"));
        var locator = new FfmpegLocator(options, _ffmpeg.DirectoryPath, _ => null);
        _session = new RecordingSession(_audio, TestPlatform.Create(_captures, _launcher, new FakeAudioPipeFactory(), new GdiGrab(), options), locator);
        _f = new ControllerFixture(session: _session, audio: _audio, captures: _captures, locator: locator, queueing: true);
    }

    public void Dispose()
    {
        _f.Dispose();
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
        _root.Delete(recursive: true);
        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _xdgConfigHome);
        CultureInfo.CurrentCulture = _culture;
    }

    private async Task Settle(Task command)
    {
        await command;
        _f.Drain();
    }

    [Fact]
    public async Task W1_StartShowsTheRecLineAndTheRecordingOverlay()
    {
        await Settle(_f.Controller.StartAsync());
        _f.Controller.UpdateStatus();

        Assert.Equal(OverlayMode.Recording, _f.Controller.Overlay);
        Assert.Equal(StatusSeverity.Recording, _f.Controller.Status.Severity);
        Assert.Matches(new Regex(@"^\* REC  \d{2}:\d{2}:\d{2}  \d+\.\d MB  720P$"), _f.Controller.Status.Text);
        Assert.True(_f.Controller.Controls.StopEnabled);
    }

    [Fact]
    public async Task W2_PauseResumeStopSavesTheFile()
    {
        await Settle(_f.Controller.StartAsync());

        await Settle(_f.Controller.PauseOrResumeAsync());
        Assert.Equal("> RESUME", _f.Controller.Controls.PauseLabel);

        await Settle(_f.Controller.PauseOrResumeAsync());
        Assert.Equal("|| PAUSE", _f.Controller.Controls.PauseLabel);

        await Settle(_f.Controller.StopAsync());

        Assert.Equal(StatusSeverity.Success, _f.Controller.Status.Severity);
        Assert.Matches(new Regex(@"^SAVED: REC_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.mp4$"), _f.Controller.Status.Text);
        Assert.NotNull(_f.Controller.LastFile);
        Assert.True(File.Exists(_f.Controller.LastFile));
        Assert.Equal(AppPaths.OutputFolder, Path.GetDirectoryName(_f.Controller.LastFile));
        Assert.Equal(OverlayMode.Idle, _f.Controller.Overlay);
    }

    [Fact]
    public async Task W3_CancelDiscardsTheRecording()
    {
        await Settle(_f.Controller.StartAsync());

        await Settle(_f.Controller.CancelAsync(() => Task.FromResult(true)));

        Assert.Equal(new StatusLine("CANCELED: recording discarded", StatusSeverity.Normal), _f.Controller.Status);
        Assert.Empty(Directory.GetFiles(AppPaths.OutputFolder, "REC_*"));
        string parts = Path.Combine(AppPaths.OutputFolder, ".parts");
        Assert.Empty(Directory.Exists(parts) ? Directory.GetDirectories(parts) : Array.Empty<string>());
        Assert.Equal(OverlayMode.Idle, _f.Controller.Overlay);
    }
}
