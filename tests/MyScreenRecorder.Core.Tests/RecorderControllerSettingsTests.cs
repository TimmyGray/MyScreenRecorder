using System;
using System.Drawing;
using System.Threading.Tasks;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class RecorderControllerSettingsTests
{
    private static ControllerFixture Standard(bool queueing = false) =>
        new(new Settings { TargetKind = "Monitor", MonitorNumber = 1, VideoQuality = VideoQuality.P720, AudioQuality = AudioQuality.Aac256 }, firstRun: false, queueing: queueing);

    private static void Invoke(RecorderController c, string method)
    {
        switch (method)
        {
            case nameof(RecorderController.SelectSource): c.SelectSource(2); break;
            case nameof(RecorderController.SelectVideoQuality): c.SelectVideoQuality(2); break;
            case nameof(RecorderController.SelectAudioQuality): c.SelectAudioQuality(0); break;
            case nameof(RecorderController.SelectMic): c.SelectMic(1); break;
            case nameof(RecorderController.SelectSpeaker): c.SelectSpeaker(1); break;
            case nameof(RecorderController.SetRegion): c.SetRegion(new Rectangle(2000, 100, 800, 600), 2); break;
            default: throw new ArgumentException(method);
        }
    }

    [Fact]
    public void B1_SelectingAMonitorSavesItsNumber()
    {
        using var f = Standard();

        f.Controller.SelectSource(2);

        Assert.Equal(new SavedSettings("Monitor", 3, null, null, VideoQuality.P720, AudioQuality.Aac256), Assert.Single(f.Saves));
        Assert.Equal("MON 3 1920x1080", f.Controller.Target.Label);
        Assert.Equal(1, f.ChangedCount);
    }

    [Fact]
    public void B2_SelectingAllSavesKindAllAndKeepsTheMonitorNumber()
    {
        using var f = new ControllerFixture(new Settings { TargetKind = "Monitor", MonitorNumber = 2 }, firstRun: false);

        f.Controller.SelectSource(3);

        Assert.Equal(new SavedSettings("All", 2, null, null, VideoQuality.P720, AudioQuality.Aac256), Assert.Single(f.Saves));
    }

    [Fact]
    public void B3_ARegionIsAppendedClampedAndSavedAsItsMonitor()
    {
        using var f = Standard();

        f.Controller.SetRegion(new Rectangle(2000, 100, 801, 601), 2);

        Assert.Equal(5, f.Controller.Targets.Count);
        Assert.Equal(4, f.Controller.SourceIndex);
        Assert.Equal("REGION 800x600 @MON 2", f.Controller.Target.Label);
        Assert.Equal(TargetKind.Region, f.Controller.Target.Kind);
        Assert.Equal(new SavedSettings("Monitor", 2, null, null, VideoQuality.P720, AudioQuality.Aac256), Assert.Single(f.Saves));
        Assert.Equal(1, f.Screens.Calls);
    }

    [Fact]
    public void B4_ASecondRegionReplacesTheFirst()
    {
        using var f = Standard();
        f.Controller.SetRegion(new Rectangle(2000, 100, 801, 601), 2);

        f.Controller.SetRegion(new Rectangle(0, 116, 400, 300), 1);

        Assert.Equal(5, f.Controller.Targets.Count);
        Assert.Equal("REGION 400x300 @MON 1", f.Controller.Target.Label);
        Assert.Equal(new SavedSettings("Monitor", 1, null, null, VideoQuality.P720, AudioQuality.Aac256), f.Saves[1]);
    }

    [Fact]
    public void B5_TheRegionStaysSelectableAfterAnotherSelection()
    {
        using var f = Standard();
        f.Controller.SetRegion(new Rectangle(2000, 100, 801, 601), 2);
        CaptureTarget region = f.Controller.Target;

        f.Controller.SelectSource(0);
        f.Controller.SelectSource(4);

        Assert.Same(region, f.Controller.Target);
        Assert.Equal(3, f.Saves.Count);
    }

    [Fact]
    public void B6_SelectingAMicAppliesItBeforeSavingAndNoneDisposesIt()
    {
        using var f = Standard();

        f.Controller.SelectMic(2);

        Assert.Equal(("in-2", false), f.Captures.Calls[^1]);
        Assert.Equal("in-2", Assert.Single(f.Saves).MicId);
        Assert.Equal(f.Captures.Calls.Count, Assert.Single(f.CaptureCallsAtSave));

        f.Controller.SelectMic(0);

        Assert.Null(f.Audio.Mic);
        Assert.Equal(1, f.Captures.Created[0].Disposed);
        Assert.Null(f.Saves[^1].MicId);
    }

    [Fact]
    public void B7_SelectingASpeakerOpensItInLoopbackAndSavesIt()
    {
        using var f = Standard();

        f.Controller.SelectSpeaker(2);

        Assert.Equal(1, f.ChangedCount);
        Assert.Equal(("out-2", true), f.Captures.Calls[^1]);
        Assert.Equal("out-2", Assert.Single(f.Saves).SpeakerId);
        Assert.Equal(f.Captures.Calls.Count, Assert.Single(f.CaptureCallsAtSave));
    }

    [Fact]
    public void B8_QualityChangesAreSaved()
    {
        using var f = Standard();

        f.Controller.SelectVideoQuality(2);
        Assert.Equal(1, f.ChangedCount);
        f.Controller.SelectAudioQuality(0);
        Assert.Equal(2, f.ChangedCount);

        Assert.Equal(new SavedSettings("Monitor", 1, null, null, VideoQuality.P1080, AudioQuality.Aac256), f.Saves[0]);
        Assert.Equal(new SavedSettings("Monitor", 1, null, null, VideoQuality.P1080, AudioQuality.Aac128), f.Saves[1]);
        Assert.Equal(2, f.Controller.VideoQualityIndex);
        Assert.Equal(0, f.Controller.AudioQualityIndex);
    }

    [Fact]
    public void R9_TheTargetListIsReadOnlyAndTheLabelsAreCachedUntilTheRegionChanges()
    {
        using var f = Standard();
        var labels = f.Controller.SourceLabels;

        Assert.False(f.Controller.Targets is System.Collections.Generic.List<CaptureTarget>);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<CaptureTarget>)f.Controller.Targets).Add(f.Controller.Target));
        Assert.Same(labels, f.Controller.SourceLabels);

        f.Controller.SetRegion(new Rectangle(2000, 100, 801, 601), 2);

        Assert.NotSame(labels, f.Controller.SourceLabels);
        Assert.Equal(5, f.Controller.SourceLabels.Count);
    }

    [Fact]
    public async Task B9_CommandsDoNotSave()
    {
        using var f = Standard();

        await f.Controller.StartAsync();
        await f.Controller.PauseOrResumeAsync();
        await f.Controller.PauseOrResumeAsync();
        await f.Controller.StopAsync();
        await f.Controller.StartAsync();
        await f.Controller.CancelAsync(() => Task.FromResult(true));

        Assert.Empty(f.Saves);
    }

    [Theory]
    [InlineData(nameof(RecorderController.SelectSource))]
    [InlineData(nameof(RecorderController.SelectVideoQuality))]
    [InlineData(nameof(RecorderController.SelectAudioQuality))]
    [InlineData(nameof(RecorderController.SelectMic))]
    [InlineData(nameof(RecorderController.SelectSpeaker))]
    [InlineData(nameof(RecorderController.SetRegion))]
    public void B10_SelectionsAreRefusedWhileRecording(string method)
    {
        using var f = Standard();
        f.Session.State = RecordingState.Recording;
        f.Session.RaiseStateChanged();

        AssertRefused(f, method);
    }

    [Theory]
    [InlineData(nameof(RecorderController.SelectSource))]
    [InlineData(nameof(RecorderController.SelectVideoQuality))]
    [InlineData(nameof(RecorderController.SelectAudioQuality))]
    [InlineData(nameof(RecorderController.SelectMic))]
    [InlineData(nameof(RecorderController.SelectSpeaker))]
    [InlineData(nameof(RecorderController.SetRegion))]
    public async Task B10_SelectionsAreRefusedWhileAStartIsPending(string method)
    {
        using var f = Standard();
        f.Session.StartGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task start = f.Controller.StartAsync();

        AssertRefused(f, method);

        f.Session.StartGate.SetResult();
        await start;
    }

    private static void AssertRefused(ControllerFixture f, string method)
    {
        int calls = f.Captures.Calls.Count;
        int targets = f.Controller.Targets.Count;
        int changed = f.ChangedCount;

        Invoke(f.Controller, method);

        Assert.Equal(calls, f.Captures.Calls.Count);
        Assert.Equal(targets, f.Controller.Targets.Count);
        Assert.Equal(0, f.Controller.SourceIndex);
        Assert.Equal(1, f.Controller.VideoQualityIndex);
        Assert.Equal(1, f.Controller.AudioQualityIndex);
        Assert.Equal(0, f.Controller.MicIndex);
        Assert.Equal(0, f.Controller.SpeakerIndex);
        Assert.Empty(f.Saves);
        Assert.Equal(changed + 1, f.ChangedCount);
    }

    [Fact]
    public void B10b_TheGuardReadsTheLiveSessionStateNotTheStaleControls()
    {
        using var f = Standard(queueing: true);
        f.Session.State = RecordingState.Recording;
        f.Session.RaiseStateChanged();

        f.Controller.SelectMic(1);
        f.Controller.SelectSource(2);

        Assert.Empty(f.Captures.Calls);
        Assert.Equal(0, f.Controller.MicIndex);
        Assert.Equal(0, f.Controller.SourceIndex);
        Assert.Empty(f.Saves);
    }

    [Fact]
    public void B11_IndicesOutsideTheListsThrow()
    {
        using var f = Standard();
        RecorderController c = f.Controller;

        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectSource(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectSource(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectMic(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectVideoQuality(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectAudioQuality(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.SelectSpeaker(3));
        Assert.Empty(f.Saves);
        Assert.Equal(0, c.SourceIndex);
        Assert.Equal(0, c.MicIndex);
        Assert.Equal(0, c.SpeakerIndex);
        Assert.Equal(1, c.VideoQualityIndex);
        Assert.Equal(1, c.AudioQualityIndex);
    }

    [Theory]
    [InlineData(nameof(RecorderController.SelectSource), 0)]
    [InlineData(nameof(RecorderController.SelectVideoQuality), 1)]
    [InlineData(nameof(RecorderController.SelectAudioQuality), 1)]
    [InlineData(nameof(RecorderController.SelectMic), 0)]
    [InlineData(nameof(RecorderController.SelectSpeaker), 0)]
    public void B12_SelectingTheCurrentIndexDoesNothing(string method, int index)
    {
        using var f = Standard();
        RecorderController c = f.Controller;

        switch (method)
        {
            case nameof(RecorderController.SelectSource): c.SelectSource(index); break;
            case nameof(RecorderController.SelectVideoQuality): c.SelectVideoQuality(index); break;
            case nameof(RecorderController.SelectAudioQuality): c.SelectAudioQuality(index); break;
            case nameof(RecorderController.SelectMic): c.SelectMic(index); break;
            case nameof(RecorderController.SelectSpeaker): c.SelectSpeaker(index); break;
        }

        Assert.Empty(f.Captures.Calls);
        Assert.Empty(f.Saves);
        Assert.Equal(0, f.ChangedCount);
    }

    [Fact]
    public void B12_SelectingTheSameMicTwiceOpensItOnce()
    {
        using var f = Standard();

        f.Controller.SelectMic(2);
        f.Controller.SelectMic(2);

        Assert.Single(f.Captures.Calls);
        Assert.Single(f.Saves);
        Assert.Equal(1, f.ChangedCount);
    }
}
