using System;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class RecorderControllerInitTests
{
    private static readonly string[] StandardLabels = { "MON 1 1920x1080", "MON 2 2560x1440", "MON 3 1920x1080", "ALL 6400x1440" };

    private static void AssertConstructionInvariants(ControllerFixture f)
    {
        Assert.Empty(f.Saves);
        Assert.Equal(OverlayMode.Idle, f.Controller.Overlay);
        Assert.Same(f.Controller.Targets[f.Controller.SourceIndex], f.Controller.Target);
        Assert.Equal(1, f.Screens.Calls);
    }

    [Fact]
    public void A1_SavedMonitorNumberSelectsItsTargetAndLabelsListAllMonitorsAndTheUnion()
    {
        using var f = new ControllerFixture(new Settings { TargetKind = "Monitor", MonitorNumber = 3 });

        Assert.Equal(2, f.Controller.SourceIndex);
        Assert.Equal("MON 3 1920x1080", f.Controller.Target.Label);
        Assert.Equal(StandardLabels, f.Controller.SourceLabels);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A2_SavedKindAllSelectsTheUnionTarget()
    {
        using var f = new ControllerFixture(new Settings { TargetKind = "All", MonitorNumber = 3 });

        Assert.Equal(3, f.Controller.SourceIndex);
        Assert.Equal(TargetKind.All, f.Controller.Target.Kind);
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 6400, 1440), f.Controller.Target.Bounds);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A3_UnknownMonitorNumberFallsBackToThePrimaryMonitor()
    {
        using var f = new ControllerFixture(new Settings { TargetKind = "Monitor", MonitorNumber = 7 });

        Assert.Equal(1, f.Controller.SourceIndex);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A4_UnknownMonitorNumberWithoutAPrimaryFallsBackToIndexZero()
    {
        var screens = new FakeScreenCatalog(
            new MonitorInfo(1, new System.Drawing.Rectangle(0, 116, 1920, 1080), false),
            new MonitorInfo(2, new System.Drawing.Rectangle(1920, 0, 2560, 1440), false));
        using var f = new ControllerFixture(new Settings { MonitorNumber = 7 }, screens: screens);

        Assert.Equal(0, f.Controller.SourceIndex);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A5_DefaultSettingsSelectTheFirstTarget()
    {
        using var f = new ControllerFixture(new Settings());

        Assert.Equal(0, f.Controller.SourceIndex);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A6_KindAllIsComparedOrdinally()
    {
        using var f = new ControllerFixture(new Settings { TargetKind = "all", MonitorNumber = 2 });

        Assert.Equal(1, f.Controller.SourceIndex);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A7_NoMonitorsThrow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ControllerFixture(screens: new FakeScreenCatalog()));

        Assert.Equal("No monitors found.", ex.Message);
    }

    [Fact]
    public void A8_FirstRunSelectsTheDefaultDevicesAndIgnoresTheSavedIds()
    {
        using var f = new ControllerFixture(new Settings { MicId = "in-1", SpeakerId = "out-2" }, firstRun: true);

        Assert.Equal(2, f.Controller.MicIndex);
        Assert.Equal(1, f.Controller.SpeakerIndex);
        Assert.Equal(new[] { "NONE", "Mic One", "Mic Two" }, f.Controller.MicNames);
        Assert.Equal(new[] { "NONE", "Speakers", "Headset" }, f.Controller.SpeakerNames);
        Assert.Equal(new[] { ("in-2", false), ("out-1", true) }, f.Captures.Calls);
        Assert.NotNull(f.Audio.Mic);
        Assert.NotNull(f.Audio.Speaker);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A9_LaterRunsSelectTheSavedDevices()
    {
        using var f = new ControllerFixture(new Settings { MicId = "in-1", SpeakerId = "out-2" }, firstRun: false);

        Assert.Equal(1, f.Controller.MicIndex);
        Assert.Equal(2, f.Controller.SpeakerIndex);
        Assert.Equal(new[] { ("in-1", false), ("out-2", true) }, f.Captures.Calls);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A10_UnknownSavedIdSelectsNoneAndOpensNothing()
    {
        using var f = new ControllerFixture(new Settings { MicId = "gone", SpeakerId = "out-1" }, firstRun: false);

        Assert.Equal(0, f.Controller.MicIndex);
        Assert.Null(f.Audio.Mic);
        Assert.Equal(new[] { ("out-1", true) }, f.Captures.Calls);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A11_NullSavedIdsSelectNone()
    {
        using var f = new ControllerFixture(new Settings { MicId = null, SpeakerId = null }, firstRun: false);

        Assert.Equal(0, f.Controller.MicIndex);
        Assert.Equal(0, f.Controller.SpeakerIndex);
        Assert.Empty(f.Captures.Calls);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A12_FirstRunWithoutKnownDefaultsSelectsNone()
    {
        var devices = new FakeAudioDeviceCatalog { DefaultInput = null, DefaultOutput = "unknown" };
        using var f = new ControllerFixture(firstRun: true, devices: devices);

        Assert.Equal(0, f.Controller.MicIndex);
        Assert.Equal(0, f.Controller.SpeakerIndex);
        Assert.Empty(f.Captures.Calls);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A13_NoDevicesListOnlyNone()
    {
        var devices = new FakeAudioDeviceCatalog { InputList = new(), OutputList = new(), DefaultInput = null, DefaultOutput = null };
        using var f = new ControllerFixture(firstRun: true, devices: devices);

        Assert.Equal(new[] { "NONE" }, f.Controller.MicNames);
        Assert.Equal(new[] { "NONE" }, f.Controller.SpeakerNames);
        Assert.Equal(0, f.Controller.MicIndex);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A14_QualityIndicesAndLabelsComeFromTheSettings()
    {
        using var high = new ControllerFixture(new Settings { VideoQuality = VideoQuality.P1080, AudioQuality = AudioQuality.Wav });
        using var defaults = new ControllerFixture(new Settings());

        Assert.Equal(new[] { "480P", "720P", "1080P" }, high.Controller.VideoQualityLabels);
        Assert.Equal(new[] { "AAC 128K", "AAC 256K", "WAV" }, high.Controller.AudioQualityLabels);
        Assert.Equal(2, high.Controller.VideoQualityIndex);
        Assert.Equal(2, high.Controller.AudioQualityIndex);
        Assert.Equal(1, defaults.Controller.VideoQualityIndex);
        Assert.Equal(1, defaults.Controller.AudioQualityIndex);
        AssertConstructionInvariants(high);
    }

    [Fact]
    public void A15_QualityOutsideTheEnumIsClampedToTheFirstEntry()
    {
        using var f = new ControllerFixture(new Settings { VideoQuality = (VideoQuality)99, AudioQuality = (AudioQuality)99 });

        Assert.Equal(0, f.Controller.VideoQualityIndex);
        Assert.Equal(0, f.Controller.AudioQualityIndex);
        AssertConstructionInvariants(f);
    }

    [Fact]
    public void A16_FfmpegMissingShowsAnErrorAndDisablesRec()
    {
        using var missing = new ControllerFixture(ffmpegFound: false);
        using var found = new ControllerFixture(ffmpegFound: true);

        Assert.Equal(new StatusLine("ERROR: ffmpeg not found", StatusSeverity.Error), missing.Controller.Status);
        Assert.False(missing.Controller.Controls.RecEnabled);
        Assert.Equal(new StatusLine("READY", StatusSeverity.Normal), found.Controller.Status);
        Assert.True(found.Controller.Controls.RecEnabled);
        AssertConstructionInvariants(missing);
        AssertConstructionInvariants(found);
    }

    [Fact]
    public void A17_ConstructionDoesNotSaveAndReadsTheScreensOnce()
    {
        using var f = new ControllerFixture(new Settings { MicId = "in-1" });

        AssertConstructionInvariants(f);
        Assert.Equal(0, f.ChangedCount);
    }
}
