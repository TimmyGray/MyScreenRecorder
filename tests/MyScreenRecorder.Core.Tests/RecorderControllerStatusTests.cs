using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class RecorderControllerStatusTests : IDisposable
{
    private static readonly SessionStats Sample = new(new TimeSpan(1, 2, 3) + TimeSpan.FromMilliseconds(999), 1572864, "720P / AAC 256K");

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly ControllerFixture _f = new();

    public RecorderControllerStatusTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        _f.Dispose();
    }

    private StatusLine StatusFor(RecordingState state, SessionStats? stats = null)
    {
        _f.Session.State = state;
        if (stats != null) _f.Session.Stats = stats;
        _f.Controller.UpdateStatus();
        return _f.Controller.Status;
    }

    [Fact]
    public void C1_IdleWithoutAMessageIsReady()
    {
        Assert.Equal(new StatusLine("READY", StatusSeverity.Normal), StatusFor(RecordingState.Idle));
    }

    [Fact]
    public void C2_RecordingShowsTheRecLine()
    {
        Assert.Equal(
            new StatusLine("* REC  00:00:00  0.0 MB  720P", StatusSeverity.Recording),
            StatusFor(RecordingState.Recording, new SessionStats(TimeSpan.Zero, 0, "720P")));
    }

    [Fact]
    public void C3_RecordingFormatsDurationSizeAndQuality()
    {
        Assert.Equal(
            new StatusLine("* REC  01:02:03  1.5 MB  720P / AAC 256K", StatusSeverity.Recording),
            StatusFor(RecordingState.Recording, Sample));
    }

    [Fact]
    public void C4_PausedShowsThePausedLineAsAWarning()
    {
        Assert.Equal(
            new StatusLine("|| PAUSED  01:02:03  1.5 MB  720P / AAC 256K", StatusSeverity.Warning),
            StatusFor(RecordingState.Paused, Sample));
    }

    [Theory]
    [InlineData(52428L, "0.0 MB")]
    [InlineData(52429L, "0.1 MB")]
    [InlineData(1310720L, "1.2 MB")]
    [InlineData(1835008L, "1.8 MB")]
    [InlineData(1074266112L, "1024.5 MB")]
    public void C5_SizeIsFormattedWithOneDecimal(long bytes, string expected)
    {
        StatusLine status = StatusFor(RecordingState.Recording, new SessionStats(TimeSpan.Zero, bytes, "Q"));

        Assert.Equal("* REC  00:00:00  " + expected + "  Q", status.Text);
    }

    [Fact]
    public void C6_HoursWrapAtTwentyFourAndFractionsAreTruncated()
    {
        Assert.Equal("* REC  01:00:01  0.0 MB  Q", StatusFor(RecordingState.Recording, new SessionStats(new TimeSpan(25, 0, 1), 0, "Q")).Text);
        Assert.Equal("* REC  00:00:00  0.0 MB  Q", StatusFor(RecordingState.Recording, new SessionStats(TimeSpan.FromMilliseconds(999), 0, "Q")).Text);
    }

    [Fact]
    public void C7_FinalizingShowsSavingWhateverTheMessage()
    {
        _f.Controller.ReportError("old");

        Assert.Equal(new StatusLine("SAVING...", StatusSeverity.Warning), StatusFor(RecordingState.Finalizing));
    }

    [Fact]
    public async Task C8_ANewRecordingIgnoresTheEarlierSavedMessage()
    {
        _f.Session.StopResult = _f.NewFile("REC_a.mp4");
        await _f.Controller.StartAsync();
        await _f.Controller.StopAsync();
        Assert.Equal(StatusSeverity.Success, _f.Controller.Status.Severity);

        await _f.Controller.StartAsync();
        _f.Session.Stats = new SessionStats(TimeSpan.Zero, 0, "720P");
        _f.Controller.UpdateStatus();

        Assert.Equal(new StatusLine("* REC  00:00:00  0.0 MB  720P", StatusSeverity.Recording), _f.Controller.Status);
    }

    [Fact]
    public async Task C9_AFailedStartShowsTheErrorMessage()
    {
        _f.Session.StartThrow = new InvalidOperationException("ffmpeg not found");

        await _f.Controller.StartAsync();

        Assert.Equal(new StatusLine("ERROR: ffmpeg not found", StatusSeverity.Error), _f.Controller.Status);
    }

    [Fact]
    public async Task C10_StopShowsTheSavedFileNameAndKeepsTheLastFileWhileItExists()
    {
        string dir = _f.NewTempDir();
        string path = Path.Combine(dir, "REC_2026-11-03_10-00-00.mp4");
        File.WriteAllText(path, "x");
        _f.Session.StopResult = path;
        await _f.Controller.StartAsync();

        await _f.Controller.StopAsync();

        Assert.Equal(new StatusLine("SAVED: REC_2026-11-03_10-00-00.mp4", StatusSeverity.Success), _f.Controller.Status);
        Assert.Equal(path, _f.Controller.LastFile);

        File.Delete(path);
        Assert.Null(_f.Controller.LastFile);
    }

    [Fact]
    public async Task C10b_StartingClearsTheMessageAndTheLastFileBeforeTheSessionCallCompletes()
    {
        _f.Session.StopResult = _f.NewFile("REC_a.mp4");
        await _f.Controller.StartAsync();
        await _f.Controller.StopAsync();
        _f.Session.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task start = _f.Controller.StartAsync();

        Assert.Equal(new StatusLine("READY", StatusSeverity.Normal), _f.Controller.Status);
        Assert.Null(_f.Controller.LastFile);
        _f.Session.StartGate.SetResult();
        await start;
    }

    [Fact]
    public async Task C11_AFinalizeFailureShowsTheSessionErrorAsIs()
    {
        _f.Session.LastError = "Finalize failed: boom Parts kept in /p";
        await _f.Controller.StartAsync();

        await _f.Controller.StopAsync();

        Assert.Equal(new StatusLine("Finalize failed: boom Parts kept in /p", StatusSeverity.Normal), _f.Controller.Status);
    }

    [Fact]
    public async Task C12_StopWithoutFileAndWithoutErrorShowsSaveFailed()
    {
        await _f.Controller.StartAsync();

        await _f.Controller.StopAsync();

        Assert.Equal(new StatusLine("ERROR: save failed", StatusSeverity.Error), _f.Controller.Status);
    }

    [Fact]
    public async Task C13_AThrowingStopShowsTheException()
    {
        _f.Session.StopThrow = new InvalidOperationException("boom");
        await _f.Controller.StartAsync();

        await _f.Controller.StopAsync();

        Assert.Equal(new StatusLine("ERROR: boom", StatusSeverity.Error), _f.Controller.Status);
    }

    [Fact]
    public async Task C14_CancelShowsCanceledAndClearsTheLastFile()
    {
        _f.Session.StopResult = _f.NewFile("REC_a.mp4");
        await _f.Controller.StartAsync();
        await _f.Controller.StopAsync();
        await _f.Controller.StartAsync();

        await _f.Controller.CancelAsync(() => Task.FromResult(true));

        Assert.Equal(new StatusLine("CANCELED: recording discarded", StatusSeverity.Normal), _f.Controller.Status);
        Assert.Null(_f.Controller.LastFile);
    }

    [Fact]
    public async Task C14b_CancelClearsTheLastFileOfAnEarlierStop()
    {
        _f.Session.StopResult = _f.NewFile("REC_a.mp4");
        await _f.Controller.StartAsync();
        await _f.Controller.StopAsync();
        Assert.NotNull(_f.Controller.LastFile);
        _f.Session.State = RecordingState.Recording;

        await _f.Controller.CancelAsync(() => Task.FromResult(true));

        Assert.Null(_f.Controller.LastFile);
    }

    [Fact]
    public async Task C15_CancelShowsTheSessionErrorAndCommandsShowTheirExceptions()
    {
        _f.Session.LastError = "Could not delete /p";
        await _f.Controller.StartAsync();
        await _f.Controller.CancelAsync(() => Task.FromResult(true));
        Assert.Equal(new StatusLine("Could not delete /p", StatusSeverity.Normal), _f.Controller.Status);

        _f.Session.CancelThrow = new InvalidOperationException("boom");
        await _f.Controller.StartAsync();
        await _f.Controller.CancelAsync(() => Task.FromResult(true));
        Assert.Equal(new StatusLine("ERROR: boom", StatusSeverity.Error), _f.Controller.Status);

        _f.Session.PauseThrow = new InvalidOperationException("boom");
        await _f.Controller.StartAsync();
        await _f.Controller.PauseOrResumeAsync();
        _f.Session.State = RecordingState.Idle;
        _f.Controller.UpdateStatus();
        Assert.Equal(new StatusLine("ERROR: boom", StatusSeverity.Error), _f.Controller.Status);
    }

    [Fact]
    public void C16_TheNumberUsesTheCurrentCulture()
    {
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberDecimalSeparator = ",";
        CultureInfo.CurrentCulture = comma;

        Assert.Equal("* REC  01:02:03  1,5 MB  720P / AAC 256K", StatusFor(RecordingState.Recording, Sample).Text);
    }

    [Fact]
    public void C17_FaultedAndReportedErrorsAreShown()
    {
        _f.Session.RaiseFaulted("ERROR: see ffmpeg.log");
        Assert.Equal(new StatusLine("ERROR: see ffmpeg.log", StatusSeverity.Error), _f.Controller.Status);

        _f.Controller.ReportError("denied");
        Assert.Equal(new StatusLine("ERROR: denied", StatusSeverity.Error), _f.Controller.Status);
    }

    [Fact]
    public async Task C18_WhileStopIsPendingTheStatusIsSaving()
    {
        _f.Session.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _f.Controller.StartAsync();

        Task stop = _f.Controller.StopAsync();

        Assert.Equal(new StatusLine("SAVING...", StatusSeverity.Warning), _f.Controller.Status);
        _f.Session.StopGate.SetResult();
        await stop;
    }

    [Fact]
    public async Task C18b_BeforeTheSessionLeavesIdleTheStopMessageIsSaving()
    {
        _f.Session.AutoTransitions = false;
        _f.Session.StopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _f.Session.State = RecordingState.Recording;
        Task stop = _f.Controller.StopAsync();

        _f.Session.State = RecordingState.Idle;
        _f.Controller.UpdateStatus();

        Assert.Equal(new StatusLine("SAVING...", StatusSeverity.Normal), _f.Controller.Status);
        _f.Session.StopGate.SetResult();
        await stop;
    }

    [Fact]
    public async Task C19_CancelShowsSavingWhileFinalizingAndTheRecLineWhileStillRecording()
    {
        _f.Session.CancelGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _f.Controller.StartAsync();
        Task cancel = _f.Controller.CancelAsync(() => Task.FromResult(true));
        Assert.Equal(new StatusLine("SAVING...", StatusSeverity.Warning), _f.Controller.Status);
        _f.Session.CancelGate.SetResult();
        await cancel;

        using var g = new ControllerFixture();
        g.Session.AutoTransitions = false;
        g.Session.CancelGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        g.Session.State = RecordingState.Recording;
        g.Session.Stats = new SessionStats(TimeSpan.Zero, 0, "720P");
        g.Session.RaiseStateChanged();
        Task pending = g.Controller.CancelAsync(() => Task.FromResult(true));
        Assert.StartsWith("* REC", g.Controller.Status.Text, StringComparison.Ordinal);

        g.Session.State = RecordingState.Idle;
        g.Controller.UpdateStatus();
        Assert.Equal(new StatusLine("CANCELING...", StatusSeverity.Normal), g.Controller.Status);
        g.Session.CancelGate.SetResult();
        await pending;
    }

    [Fact]
    public void C20_UpdateStatusRaisesChangedOnlyWhenTheLineChanged()
    {
        StatusFor(RecordingState.Idle);
        StatusFor(RecordingState.Idle);
        Assert.Equal(0, _f.ChangedCount);

        StatusFor(RecordingState.Recording, new SessionStats(TimeSpan.FromSeconds(1), 0, "Q"));
        Assert.Equal(1, _f.ChangedCount);
        _f.Controller.UpdateStatus();
        Assert.Equal(1, _f.ChangedCount);
        StatusFor(RecordingState.Recording, new SessionStats(TimeSpan.FromSeconds(2), 0, "Q"));
        Assert.Equal(2, _f.ChangedCount);
    }

    [Fact]
    public async Task C21_AFailedResumeShowsTheException()
    {
        using var f = new ControllerFixture();
        f.SetState(RecordingState.Paused);
        f.Session.ResumeThrow = new InvalidOperationException("boom");
        f.Session.ResumeGate = ControllerFixture.Gate();
        Task resume = f.Controller.PauseOrResumeAsync();
        f.SetState(RecordingState.Idle);

        f.Session.ResumeGate.SetResult();
        await resume;

        Assert.Equal(new StatusLine("ERROR: boom", StatusSeverity.Error), f.Controller.Status);
    }
}
