using System.Threading.Tasks;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class RecorderControllerControlsTests
{
    private const string Pause = "|| PAUSE";
    private const string Resume = "> RESUME";

    private static ControlState Expected(bool selectors, bool rec, bool pause, string label, bool stop, bool cancel) =>
        new(selectors, selectors, selectors, selectors, selectors, selectors, rec, pause, label, stop, cancel);

    [Fact]
    public void D1_IdleNotBusyFfmpegFound()
    {
        using var f = new ControllerFixture();

        Assert.Equal(Expected(true, true, false, Pause, false, false), f.Controller.Controls);
    }

    [Fact]
    public async Task D2_IdleWhileAStartIsPending()
    {
        using var f = new ControllerFixture();
        f.Session.StartGate = ControllerFixture.Gate();
        Task start = f.Controller.StartAsync();

        Assert.Equal(Expected(true, false, false, Pause, false, false), f.Controller.Controls);

        f.Session.StartGate.SetResult();
        await start;
    }

    [Fact]
    public void D3_IdleFfmpegMissing()
    {
        using var f = new ControllerFixture(ffmpegFound: false);

        Assert.Equal(Expected(true, false, false, Pause, false, false), f.Controller.Controls);
    }

    [Fact]
    public async Task D4_RecordingNotBusy()
    {
        using var f = new ControllerFixture();

        await f.Controller.StartAsync();

        Assert.Equal(Expected(false, false, true, Pause, true, true), f.Controller.Controls);
    }

    [Fact]
    public async Task D5_RecordingWhileAPauseIsPending()
    {
        using var f = new ControllerFixture();
        await f.Controller.StartAsync();
        f.Session.PauseGate = ControllerFixture.Gate();
        Task pause = f.Controller.PauseOrResumeAsync();

        Assert.Equal(Expected(false, false, false, Pause, false, false), f.Controller.Controls);

        f.Session.PauseGate.SetResult();
        await pause;
    }

    [Fact]
    public async Task D6_PausedNotBusy()
    {
        using var f = new ControllerFixture();
        await f.Controller.StartAsync();
        await f.Controller.PauseOrResumeAsync();

        Assert.Equal(Expected(false, false, true, Resume, true, true), f.Controller.Controls);
    }

    [Fact]
    public async Task D7_PausedWhileAResumeIsPending()
    {
        using var f = new ControllerFixture();
        await f.Controller.StartAsync();
        await f.Controller.PauseOrResumeAsync();
        f.Session.ResumeGate = ControllerFixture.Gate();
        Task resume = f.Controller.PauseOrResumeAsync();

        Assert.Equal(Expected(false, false, false, Resume, false, false), f.Controller.Controls);

        f.Session.ResumeGate.SetResult();
        await resume;
    }

    [Fact]
    public void D8_FinalizingNotBusy()
    {
        using var f = new ControllerFixture();
        f.Session.State = RecordingState.Finalizing;
        f.Session.RaiseStateChanged();

        Assert.Equal(Expected(false, false, false, Pause, false, false), f.Controller.Controls);
    }

    [Fact]
    public async Task D9_FinalizingWhileAStopIsPending()
    {
        using var f = new ControllerFixture();
        await f.Controller.StartAsync();
        f.Session.StopGate = ControllerFixture.Gate();
        Task stop = f.Controller.StopAsync();

        Assert.Equal(RecordingState.Finalizing, f.Session.State);
        Assert.Equal(Expected(false, false, false, Pause, false, false), f.Controller.Controls);

        f.Session.StopGate.SetResult();
        await stop;
    }
}
