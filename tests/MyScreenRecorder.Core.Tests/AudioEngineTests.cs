using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class AudioEngineTests
{
    private const int PipeBytesPerSecond = 48000 * 2 * sizeof(float);
    private const string DeviceChangeMessage = "Cannot change audio devices while recording.";

    [Fact]
    public async Task Segment_WithoutData_PumpsSilenceForTheElapsedTime()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        var pipe = new MemoryStream();

        var stopwatch = Stopwatch.StartNew();
        engine.BeginSegment(pipe);
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        engine.EndSegment();
        stopwatch.Stop();

        byte[] bytes = pipe.ToArray();
        double expected = stopwatch.Elapsed.TotalSeconds * PipeBytesPerSecond;
        Assert.InRange(bytes.Length, expected * 0.9, expected * 1.1);
        Assert.Equal(0, bytes.Length % 8);
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public async Task Segment_CapturedDataReachesThePipeAndSourcesAreDisarmedAfterwards()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        var pipe = new MemoryStream();

        engine.BeginSegment(pipe);
        factory.Created[0].Raise(AudioSourceTests.Floats(Repeat(4800, 0.5f, 0.5f)));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        engine.EndSegment();

        Assert.Contains(0.5f, Samples(pipe));
        Assert.False(engine.Mic!.Armed);
    }

    [Fact]
    public async Task Segment_ClampsTheMixToFullScale()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        engine.SetSpeaker("spk-1");
        var pipe = new MemoryStream();
        byte[] frames = AudioSourceTests.Floats(Repeat(4800, 0.75f, -0.75f));

        engine.BeginSegment(pipe);
        factory.Created[0].Raise(frames);
        factory.Created[1].Raise(frames);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        engine.EndSegment();

        float[] samples = Samples(pipe);
        Assert.Contains(1f, samples);
        Assert.Contains(-1f, samples);
        Assert.All(samples, v => Assert.InRange(v, -1f, 1f));
    }

    [Fact]
    public async Task Segment_ReplacesNaNWithSilence()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        var pipe = new MemoryStream();

        engine.BeginSegment(pipe);
        factory.Created[0].Raise(AudioSourceTests.Floats(Repeat(4800, float.NaN, float.NaN)));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        engine.EndSegment();

        float[] samples = Samples(pipe);
        Assert.NotEmpty(samples);
        Assert.All(samples, v => Assert.Equal(0f, v));
    }

    [Fact]
    public async Task Segment_BacklogAbove200MsIsCutTo100Ms()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        var pipe = new MemoryStream();

        engine.BeginSegment(pipe);
        factory.Created[0].Raise(AudioSourceTests.Floats(Repeat(24000, 0.5f, 0.5f)));
        await Task.Delay(800, TestContext.Current.CancellationToken);
        engine.EndSegment();

        int dataFrames = Samples(pipe).Count(v => v == 0.5f) / 2;
        Assert.InRange(dataFrames, 4800, 9600);
    }

    [Fact]
    public async Task BeginSegment_DiscardsAudioBufferedBeforeTheSegment()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        engine.Mic!.Armed = true;
        factory.Created[0].Raise(AudioSourceTests.Floats(Repeat(4800, 0.5f, 0.5f)));
        var pipe = new MemoryStream();

        engine.BeginSegment(pipe);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        engine.EndSegment();

        Assert.DoesNotContain(0.5f, Samples(pipe));
    }

    [Fact]
    public void SetMic_OpensAnInputCaptureAndStartsIt()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);

        engine.SetMic("mic-1");

        Assert.Equal(new[] { ("mic-1", false) }, factory.Calls);
        Assert.Equal(1, factory.Created[0].Started);
        Assert.NotNull(engine.Mic);
        Assert.True(engine.HasAudio);
    }

    [Fact]
    public void SetSpeaker_OpensALoopbackCaptureAndStartsIt()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);

        engine.SetSpeaker("spk-1");

        Assert.Equal(new[] { ("spk-1", true) }, factory.Calls);
        Assert.Equal(1, factory.Created[0].Started);
        Assert.NotNull(engine.Speaker);
        Assert.True(engine.HasAudio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SetMic_NoDevice_LeavesMicEmptyWithoutOpening(string? deviceId)
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);

        engine.SetMic(deviceId);

        Assert.Null(engine.Mic);
        Assert.Empty(factory.Calls);
        Assert.False(engine.HasAudio);
    }

    [Fact]
    public void SetMic_Again_DisposesThePreviousCapture()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");

        engine.SetMic(null);

        Assert.Equal(1, factory.Created[0].Disposed);
        Assert.Null(engine.Mic);
    }

    [Fact]
    public void SetMic_FactoryThrows_RecordsTheErrorAndLeavesMicEmpty()
    {
        var factory = new FakeAudioCaptureFactory { OpenException = new InvalidOperationException("no such device") };
        using var engine = new AudioEngine(factory);

        engine.SetMic("gone");

        Assert.Null(engine.Mic);
        Assert.Equal("no such device", engine.LastError);
    }

    [Fact]
    public void SetMic_StartThrows_RecordsTheErrorAndDisposesTheCapture()
    {
        var factory = new FakeAudioCaptureFactory { StartException = new InvalidOperationException("busy") };
        using var engine = new AudioEngine(factory);

        engine.SetMic("mic-1");

        Assert.Null(engine.Mic);
        Assert.Equal("busy", engine.LastError);
        Assert.Equal(1, factory.Created[0].Disposed);
    }

    [Fact]
    public void SetMic_AfterAFailure_ClearsLastError()
    {
        var factory = new FakeAudioCaptureFactory { OpenException = new InvalidOperationException("no such device") };
        using var engine = new AudioEngine(factory);
        engine.SetMic("gone");
        factory.OpenException = null;

        engine.SetMic("mic-1");

        Assert.NotNull(engine.Mic);
        Assert.Null(engine.LastError);
    }

    [Fact]
    public void SetMic_DuringASegment_Throws()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.BeginSegment(new MemoryStream());

        var ex = Assert.Throws<InvalidOperationException>(() => engine.SetMic("mic-1"));

        Assert.Equal(DeviceChangeMessage, ex.Message);
        Assert.Empty(factory.Calls);
    }

    [Fact]
    public void SetSpeaker_DuringASegment_Throws()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.BeginSegment(new MemoryStream());

        var ex = Assert.Throws<InvalidOperationException>(() => engine.SetSpeaker("spk-1"));

        Assert.Equal(DeviceChangeMessage, ex.Message);
        Assert.Empty(factory.Calls);
    }

    [Fact]
    public void BeginSegment_Twice_Throws()
    {
        using var engine = new AudioEngine(new FakeAudioCaptureFactory());
        engine.BeginSegment(new MemoryStream());

        Assert.Throws<InvalidOperationException>(() => engine.BeginSegment(new MemoryStream()));
    }

    [Fact]
    public void MicAndSpeakerPeak_ForwardTheSourcePeaks()
    {
        var factory = new FakeAudioCaptureFactory();
        using var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        engine.SetSpeaker("spk-1");

        factory.Created[0].Raise(AudioSourceTests.Floats(0.25f, 0f));
        factory.Created[1].Raise(AudioSourceTests.Floats(0.5f, 0f));

        Assert.Equal(0.25f, engine.MicPeak);
        Assert.Equal(0.5f, engine.SpeakerPeak);
    }

    [Fact]
    public void Dispose_EndsTheSegmentAndDisposesMicAndSpeaker()
    {
        var factory = new FakeAudioCaptureFactory();
        var engine = new AudioEngine(factory);
        engine.SetMic("mic-1");
        engine.SetSpeaker("spk-1");
        var pipe = new MemoryStream();
        engine.BeginSegment(pipe);

        engine.Dispose();

        Assert.False(pipe.CanWrite);
        Assert.All(factory.Created, capture => Assert.Equal(1, capture.Disposed));
        Assert.Null(engine.Mic);
        Assert.Null(engine.Speaker);
    }

    private static float[] Repeat(int frames, float left, float right)
    {
        var samples = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            samples[i * 2] = left;
            samples[i * 2 + 1] = right;
        }
        return samples;
    }

    private static float[] Samples(MemoryStream pipe)
    {
        byte[] bytes = pipe.ToArray();
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
        return samples;
    }
}
