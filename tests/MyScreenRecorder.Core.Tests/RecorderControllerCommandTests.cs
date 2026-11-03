using System;
using System.IO;
using System.Threading.Tasks;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class RecorderControllerCommandTests
{
    [Fact]
    public async Task E1_StartPassesTheSelectedTargetAndQualities()
    {
        using var f = new ControllerFixture();
        f.Controller.SelectSource(3);
        f.Controller.SelectVideoQuality(2);
        f.Controller.SelectAudioQuality(2);

        await f.Controller.StartAsync();

        Assert.Equal(new[] { "session.Start" }, f.Session.Log);
        Assert.Same(f.Controller.Targets[3], f.Session.StartArgs!.Value.Target);
        Assert.Equal(VideoQuality.P1080, f.Session.StartArgs!.Value.Video);
        Assert.Equal(AudioQuality.Wav, f.Session.StartArgs!.Value.Audio);
    }

    [Fact]
    public async Task E1b_StartKeepsTheVideoAndAudioIndicesApart()
    {
        using var f = new ControllerFixture();
        f.Controller.SelectVideoQuality(0);
        f.Controller.SelectAudioQuality(2);

        await f.Controller.StartAsync();

        Assert.Equal(VideoQuality.P480, f.Session.StartArgs!.Value.Video);
        Assert.Equal(AudioQuality.Wav, f.Session.StartArgs!.Value.Audio);
    }

    [Fact]
    public async Task E2_TheOverlayChangesBeforeTheSessionStartIsEntered()
    {
        using var f = new ControllerFixture();
        f.Controller.Changed += () => f.Log.Add("changed:" + f.Controller.Overlay);
        f.Session.StartGate = ControllerFixture.Gate();

        Task start = f.Controller.StartAsync();

        Assert.Equal(new[] { "changed:Recording", "session.Start" }, f.Log);
        Assert.Equal(OverlayMode.Recording, f.Controller.Overlay);
        Assert.False(f.Controller.Controls.RecEnabled);
        Assert.Equal("READY", f.Controller.Status.Text);

        f.Session.StartGate.SetResult();
        await start;
        Assert.Equal(OverlayMode.Recording, f.Controller.Overlay);
    }

    [Fact]
    public async Task E3_AFailedStartRestoresTheIdleOverlay()
    {
        using var f = new ControllerFixture();
        f.Controller.Changed += () => f.Log.Add("changed:" + f.Controller.Overlay);
        f.Session.StartGate = ControllerFixture.Gate();
        Task start = f.Controller.StartAsync();

        f.Session.StartGate.SetException(new InvalidOperationException("boom"));
        await start;

        Assert.Equal(OverlayMode.Idle, f.Controller.Overlay);
        Assert.Equal(new StatusLine("ERROR: boom", StatusSeverity.Error), f.Controller.Status);
        Assert.True(f.Controller.Controls.RecEnabled);
        Assert.Equal("changed:Idle", f.Log[^1]);
    }

    [Theory]
    [InlineData(RecordingState.Idle, OverlayMode.Idle)]
    [InlineData(RecordingState.Recording, OverlayMode.Recording)]
    [InlineData(RecordingState.Paused, OverlayMode.Paused)]
    [InlineData(RecordingState.Finalizing, OverlayMode.Idle)]
    public void E3b_TheOverlayFollowsTheSessionState(RecordingState state, OverlayMode expected)
    {
        using var f = new ControllerFixture();

        f.SetState(state);

        Assert.Equal(expected, f.Controller.Overlay);
    }

    [Fact]
    public async Task E4_ASecondStartWhileTheFirstIsPendingIsIgnored()
    {
        using var f = new ControllerFixture();
        f.Session.StartGate = ControllerFixture.Gate();
        Task first = f.Controller.StartAsync();

        Task second = f.Controller.StartAsync();

        Assert.True(second.IsCompleted);
        Assert.Single(f.Log, "session.Start");
        f.Session.StartGate.SetResult();
        await first;
    }

    [Fact]
    public async Task E5_StartIsRefusedWithoutFfmpegAndWhileRecording()
    {
        using var missing = new ControllerFixture(ffmpegFound: false);
        await missing.Controller.StartAsync();
        Assert.Empty(missing.Log);

        using var recording = new ControllerFixture();
        recording.SetState(RecordingState.Recording);
        await recording.Controller.StartAsync();
        Assert.Empty(recording.Log);
    }

    [Fact]
    public async Task E5b_StartReadsTheLiveSessionStateNotTheStaleControls()
    {
        using var f = new ControllerFixture(queueing: true);
        f.SetState(RecordingState.Recording);
        StatusLine before = f.Controller.Status;

        Task start = f.Controller.StartAsync();

        Assert.True(start.IsCompleted);
        Assert.Empty(f.Log);
        Assert.Equal(before, f.Controller.Status);
        await start;
    }

    [Fact]
    public async Task E6_PauseOrResumeFollowsTheState()
    {
        using var f = new ControllerFixture();

        await f.Controller.PauseOrResumeAsync();
        Assert.Equal(0, f.ChangedCount);
        f.SetState(RecordingState.Finalizing);
        int before = f.ChangedCount;
        await f.Controller.PauseOrResumeAsync();
        Assert.Empty(f.Log);
        Assert.Equal(before, f.ChangedCount);

        f.SetState(RecordingState.Recording);
        await f.Controller.PauseOrResumeAsync();
        Assert.Equal(new[] { "session.Pause" }, f.Log);

        await f.Controller.PauseOrResumeAsync();
        Assert.Equal(new[] { "session.Pause", "session.Resume" }, f.Log);
    }

    [Fact]
    public async Task E6b_AResumeInFlightKeepsThePausedOverlayAndIgnoresASecondCall()
    {
        using var f = new ControllerFixture();
        f.SetState(RecordingState.Paused);
        f.Session.ResumeGate = ControllerFixture.Gate();

        Task resume = f.Controller.PauseOrResumeAsync();
        Task second = f.Controller.PauseOrResumeAsync();

        Assert.True(second.IsCompleted);
        Assert.Single(f.Log, "session.Resume");
        Assert.Equal(OverlayMode.Paused, f.Controller.Overlay);
        f.Session.ResumeGate.SetResult();
        await resume;
        Assert.Equal(OverlayMode.Recording, f.Controller.Overlay);
    }

    [Theory]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Paused)]
    public async Task E7_StopCallsTheSessionWhenActive(RecordingState state)
    {
        using var f = new ControllerFixture();
        f.SetState(state);

        await f.Controller.StopAsync();

        Assert.Equal(new[] { "session.Stop" }, f.Log);
    }

    [Fact]
    public async Task E7_StopInIdleDoesNothing()
    {
        using var f = new ControllerFixture();

        await f.Controller.StopAsync();

        Assert.Empty(f.Log);
        Assert.Equal("READY", f.Controller.Status.Text);
        Assert.Equal(0, f.ChangedCount);
    }

    [Fact]
    public async Task E8_CancelAsksFirstAndOnlyCancelsOnYes()
    {
        using var f = new ControllerFixture();
        await f.Controller.StartAsync();
        f.Log.Clear();

        await f.Controller.CancelAsync(() => Task.FromResult(false));
        Assert.Empty(f.Log);
        Assert.True(f.Controller.Controls.CancelEnabled);

        await f.Controller.CancelAsync(() => Task.FromResult(true));
        Assert.Equal(new[] { "session.Cancel" }, f.Log);
    }

    [Fact]
    public async Task E8_CancelDoesNotAskWhileBusyOrIdle()
    {
        using var f = new ControllerFixture();
        int asked = 0;
        Func<Task<bool>> confirm = () =>
        {
            asked++;
            return Task.FromResult(true);
        };

        await f.Controller.CancelAsync(confirm);
        Assert.Equal(0, asked);

        await f.Controller.StartAsync();
        f.Session.PauseGate = ControllerFixture.Gate();
        Task pause = f.Controller.PauseOrResumeAsync();
        await f.Controller.CancelAsync(confirm);
        Assert.Equal(0, asked);
        Assert.DoesNotContain("session.Cancel", f.Log);
        f.Session.PauseGate.SetResult();
        await pause;
    }

    [Fact]
    public async Task E8c_CancelInFlightDisablesTheButtonsBeforeTheSessionTransitions()
    {
        using var f = new ControllerFixture();
        f.Session.AutoTransitions = false;
        f.Session.CancelGate = ControllerFixture.Gate();
        f.SetState(RecordingState.Recording);

        Task cancel = f.Controller.CancelAsync(() => Task.FromResult(true));

        Assert.False(f.Controller.Controls.CancelEnabled);
        Assert.False(f.Controller.Controls.StopEnabled);
        Assert.False(f.Controller.Controls.PauseEnabled);
        f.Session.CancelGate.SetResult();
        await cancel;
        Assert.True(f.Controller.Controls.CancelEnabled);
    }

    [Fact]
    public async Task E8d_StopInFlightDisablesTheButtonsBeforeTheSessionTransitions()
    {
        using var f = new ControllerFixture();
        f.Session.AutoTransitions = false;
        f.Session.StopGate = ControllerFixture.Gate();
        f.SetState(RecordingState.Recording);

        Task stop = f.Controller.StopAsync();

        Assert.False(f.Controller.Controls.CancelEnabled);
        Assert.False(f.Controller.Controls.StopEnabled);
        Assert.False(f.Controller.Controls.PauseEnabled);
        f.Session.StopGate.SetResult();
        await stop;
        Assert.True(f.Controller.Controls.StopEnabled);
    }

    [Fact]
    public async Task E9_CancelRechecksTheStateAfterTheAnswer()
    {
        using var f = new ControllerFixture();
        await f.Controller.StartAsync();
        f.Log.Clear();

        await f.Controller.CancelAsync(() =>
        {
            f.Session.State = RecordingState.Idle;
            return Task.FromResult(true);
        });

        Assert.DoesNotContain("session.Cancel", f.Log);
        await f.Controller.StartAsync();
        Assert.Contains("session.Start", f.Log);
    }

    [Fact]
    public async Task E10_CloseFollowsTheState()
    {
        using var idle = new ControllerFixture();
        Assert.True(await idle.Controller.PrepareCloseAsync());
        Assert.Empty(idle.Log);

        using var recording = new ControllerFixture();
        recording.SetState(RecordingState.Recording);
        Assert.True(await recording.Controller.PrepareCloseAsync());
        Assert.Equal(new[] { "session.Stop" }, recording.Log);

        using var paused = new ControllerFixture();
        paused.SetState(RecordingState.Paused);
        Assert.True(await paused.Controller.PrepareCloseAsync());
        Assert.Equal(new[] { "session.Stop" }, paused.Log);

        using var finalizing = new ControllerFixture();
        finalizing.SetState(RecordingState.Finalizing);
        Assert.False(await finalizing.Controller.PrepareCloseAsync());
        Assert.Empty(finalizing.Log);
    }

    [Fact]
    public async Task E10_CloseWhileAStartIsPendingAllowsTheClose()
    {
        using var f = new ControllerFixture();
        f.Session.StartGate = ControllerFixture.Gate();
        Task start = f.Controller.StartAsync();

        Assert.True(await f.Controller.PrepareCloseAsync());

        Assert.Equal(new[] { "session.Start" }, f.Log);
        f.Session.StartGate.SetResult();
        await start;
    }

    [Fact]
    public async Task E11_LastFileIsNullBeforeAnyStopAndAfterTheFileIsGone()
    {
        using var f = new ControllerFixture();
        Assert.Null(f.Controller.LastFile);
        string path = f.NewFile("REC_a.mp4");
        f.Session.StopResult = path;
        await f.Controller.StartAsync();

        await f.Controller.StopAsync();
        Assert.Equal(path, f.Controller.LastFile);

        File.Delete(path);
        Assert.Null(f.Controller.LastFile);
    }

    [Fact]
    public void E12_ReportErrorShowsTheMessageOnce()
    {
        using var f = new ControllerFixture();

        f.Controller.ReportError("denied");

        Assert.Equal(new StatusLine("ERROR: denied", StatusSeverity.Error), f.Controller.Status);
        Assert.Equal(1, f.ChangedCount);
    }

    [Fact]
    public async Task E13_SessionStateEventsAreMarshalledAndReadWhenTheActionRuns()
    {
        using var f = new ControllerFixture(queueing: true);
        f.Session.State = RecordingState.Recording;

        await Task.Run(() => f.Session.RaiseStateChanged(), TestContext.Current.CancellationToken);

        Assert.Equal(0, f.ChangedCount);
        Assert.False(f.Controller.Controls.PauseEnabled);
        f.Session.State = RecordingState.Paused;
        f.Drain();
        Assert.Equal(1, f.ChangedCount);
        Assert.Equal("> RESUME", f.Controller.Controls.PauseLabel);
        Assert.Equal(OverlayMode.Paused, f.Controller.Overlay);
    }

    [Fact]
    public async Task E14_FaultedIsMarshalled()
    {
        using var f = new ControllerFixture(queueing: true);

        await Task.Run(() => f.Session.RaiseFaulted("ERROR: see ffmpeg.log"), TestContext.Current.CancellationToken);

        Assert.Equal(0, f.ChangedCount);
        Assert.Equal("READY", f.Controller.Status.Text);
        f.Drain();
        Assert.Equal("ERROR: see ffmpeg.log", f.Controller.Status.Text);
        Assert.Equal(1, f.ChangedCount);
    }

    [Fact]
    public void E15_AfterDisposeSessionEventsDoNotReachTheController()
    {
        using var f = new ControllerFixture(queueing: true);
        f.Controller.Dispose();

        f.Session.RaiseStateChanged();
        f.Session.RaiseFaulted("x");

        Assert.Equal(0, f.Drain());
        Assert.Equal(0, f.ChangedCount);
    }

    [Fact]
    public void E15b_ActionsQueuedBeforeDisposeDoNothing()
    {
        using var f = new ControllerFixture(queueing: true);
        ControlState controls = f.Controller.Controls;
        StatusLine status = f.Controller.Status;
        f.Session.State = RecordingState.Recording;
        f.Session.RaiseStateChanged();
        f.Session.RaiseFaulted("ERROR: x");

        f.Controller.Dispose();
        f.Drain();

        Assert.Equal(0, f.ChangedCount);
        Assert.Equal(controls, f.Controller.Controls);
        Assert.Equal(status, f.Controller.Status);
        Assert.Equal(OverlayMode.Idle, f.Controller.Overlay);
    }

    [Fact]
    public void E16_TickMetersReadsThePeakWithoutRaisingChanged()
    {
        using var f = new ControllerFixture(new Settings { MicId = "in-1" });
        f.Captures.Created[0].Raise(AudioSourceTests.Floats(new[] { 0.5f, 0.25f, 0.1f, 0f }));

        f.Controller.TickMeters();

        Assert.Equal(0.5f, f.Controller.MicLevel, 1e-4f);
        Assert.Equal(0f, f.Controller.SpeakerLevel);
        Assert.Equal(0, f.ChangedCount);
    }

    [Fact]
    public void E16b_TickMetersFollowsTheMicWithDecayAndTheSpeakerWithItsPeak()
    {
        using var f = new ControllerFixture(new Settings { MicId = "in-1", SpeakerId = "out-1" });
        FakeAudioCapture mic = f.Captures.Created[0];
        FakeAudioCapture speaker = f.Captures.Created[1];

        mic.Raise(AudioSourceTests.Floats(new[] { 0.5f }));
        speaker.Raise(AudioSourceTests.Floats(new[] { 0.25f }));
        f.Controller.TickMeters();
        mic.Raise(AudioSourceTests.Floats(new[] { 0.1f }));
        f.Controller.TickMeters();

        Assert.Equal(0.5f * 0.85f, f.Controller.MicLevel, 1e-4f);
        Assert.Equal(0.25f, f.Controller.SpeakerLevel, 1e-4f);
    }

    private static Action ThrowOnce()
    {
        bool thrown = false;
        return () =>
        {
            if (thrown) return;
            thrown = true;
            throw new InvalidOperationException("view");
        };
    }

    [Fact]
    public async Task R1_AThrowingSubscriberDoesNotLeaveAStartBusy()
    {
        using var f = new ControllerFixture();
        f.Controller.Changed += ThrowOnce();

        await f.Controller.StartAsync();

        Assert.Empty(f.Log);
        Assert.Equal(new StatusLine("ERROR: view", StatusSeverity.Error), f.Controller.Status);
        Assert.Equal(OverlayMode.Idle, f.Controller.Overlay);
        Assert.True(f.Controller.Controls.RecEnabled);
        await f.Controller.StartAsync();
        Assert.Equal(new[] { "session.Start" }, f.Log);
    }

    [Fact]
    public async Task R1_AThrowingSubscriberDoesNotLeaveAPauseBusy()
    {
        using var f = new ControllerFixture();
        f.SetState(RecordingState.Recording);
        f.Controller.Changed += ThrowOnce();

        await f.Controller.PauseOrResumeAsync();

        Assert.Empty(f.Log);
        Assert.True(f.Controller.Controls.PauseEnabled);
        await f.Controller.PauseOrResumeAsync();
        Assert.Equal(new[] { "session.Pause" }, f.Log);
    }

    [Fact]
    public async Task R1_AThrowingSubscriberDoesNotLeaveACancelBusy()
    {
        using var f = new ControllerFixture();
        f.SetState(RecordingState.Recording);
        f.Controller.Changed += ThrowOnce();

        await f.Controller.CancelAsync(() => Task.FromResult(true));

        Assert.Empty(f.Log);
        Assert.True(f.Controller.Controls.CancelEnabled);
        await f.Controller.CancelAsync(() => Task.FromResult(true));
        Assert.Equal(new[] { "session.Cancel" }, f.Log);
    }

    [Fact]
    public async Task R1_AThrowingSubscriberDoesNotLeaveAStopBusy()
    {
        using var f = new ControllerFixture();
        f.SetState(RecordingState.Recording);
        f.Controller.Changed += ThrowOnce();

        await f.Controller.StopAsync();

        Assert.Empty(f.Log);
        Assert.True(f.Controller.Controls.StopEnabled);
        await f.Controller.StopAsync();
        Assert.Equal(new[] { "session.Stop" }, f.Log);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task R2_ASecondCancelWhileTheDialogIsOpenIsIgnored(bool autoTransitions)
    {
        using var f = new ControllerFixture();
        f.Session.AutoTransitions = autoTransitions;
        f.SetState(RecordingState.Recording);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int asked = 0;
        Func<Task<bool>> confirm = () =>
        {
            asked++;
            return answer.Task;
        };

        Task first = f.Controller.CancelAsync(confirm);
        Task second = f.Controller.CancelAsync(confirm);

        Assert.True(second.IsCompleted);
        Assert.Equal(1, asked);
        Assert.True(f.Controller.Controls.CancelEnabled);
        answer.SetResult(true);
        await first;
        Assert.Equal(1, asked);
        Assert.Single(f.Log, "session.Cancel");
    }

    [Fact]
    public async Task R2_ACancelAnsweredYesAfterAPauseStartedDoesNotCancel()
    {
        using var f = new ControllerFixture();
        f.SetState(RecordingState.Recording);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Session.PauseGate = ControllerFixture.Gate();
        Task cancel = f.Controller.CancelAsync(() => answer.Task);
        Task pause = f.Controller.PauseOrResumeAsync();

        answer.SetResult(true);
        await cancel;

        Assert.DoesNotContain("session.Cancel", f.Log);
        f.Session.PauseGate.SetResult();
        await pause;
        Assert.Equal(RecordingState.Paused, f.Session.State);
        Assert.DoesNotContain("session.Cancel", f.Log);
    }

    [Fact]
    public async Task R7_ChangedIsNotRaisedAfterDisposeWhenACommandCompletes()
    {
        using var f = new ControllerFixture();
        f.Session.StartGate = ControllerFixture.Gate();
        Task start = f.Controller.StartAsync();
        f.Controller.Dispose();
        int before = f.ChangedCount;

        f.Session.StartGate.SetResult();
        await start;

        Assert.Equal(before, f.ChangedCount);
    }
}
