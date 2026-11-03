using System;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using NAudio.Wave;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class AudioSourceTests
{
    private const double PeakTolerance = 1e-4;

    [Fact]
    public void Peak_Float32_IsTheLargestAbsoluteSample()
    {
        AssertPeak(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), Floats(0.1f, -0.75f, 0.3f, 0f), 0.75);
    }

    [Fact]
    public void Peak_Pcm16_IsScaledByFullScale32768()
    {
        AssertPeak(new WaveFormat(48000, 16, 2), Int16s(1000, -24576, 300, 0), 0.75);
    }

    [Fact]
    public void Peak_Pcm24_IsSignExtendedAndScaledByTwoToThe31()
    {
        byte[] data = { 0x00, 0x00, 0x10, 0x00, 0x00, 0xA0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        AssertPeak(new WaveFormat(48000, 24, 2), data, 0.75);
    }

    [Fact]
    public void Peak_Pcm32_IsScaledByTwoToThe31()
    {
        AssertPeak(new WaveFormat(48000, 32, 2), Int32s(268435456, -1610612736), 0.75);
    }

    [Fact]
    public void Peak_WasapiSharedModeExtensibleFormat_IsReadAsFloat()
    {
        AssertPeak(new WaveFormatExtensible(48000, 32, 2), Floats(0.1f, -0.75f, 0.3f, 0f), 0.75);
    }

    [Fact]
    public void Peak_AboveFullScale_IsClampedToOne()
    {
        AssertPeak(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), Floats(1.5f, 0f), 1.0);
    }

    [Fact]
    public void Peak_UnsupportedBitDepth_IsZero()
    {
        AssertPeak(new WaveFormat(48000, 8, 2), new byte[] { 0x10, 0xF0, 0x7F, 0x01 }, 0.0);
    }

    [Fact]
    public void Peak_BeforeAnyData_IsZero()
    {
        using var source = new AudioSource(new FakeAudioCapture());

        Assert.Equal(0f, source.Peak);
    }

    [Fact]
    public async Task Peak_MoreThan300MsAfterTheLastData_IsZero()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture);
        capture.Raise(Floats(0.75f, 0f));
        Assert.Equal(0.75, source.Peak, PeakTolerance);

        await Task.Delay(450, TestContext.Current.CancellationToken);

        Assert.Equal(0f, source.Peak);
    }

    [Fact]
    public void Peak_AfterCaptureStopped_IsZero()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture);
        capture.Raise(Floats(0.75f, 0f));

        capture.RaiseStopped();

        Assert.Equal(0f, source.Peak);
    }

    [Fact]
    public void NotArmed_DataIsNotBuffered()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture);

        capture.Raise(new byte[9600]);

        Assert.Equal(0.0, source.BufferedMilliseconds);
    }

    [Fact]
    public void Armed_DataIsBuffered()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture) { Armed = true };

        capture.Raise(new byte[9600]);

        Assert.Equal(25.0, source.BufferedMilliseconds, 0.001);
    }

    [Fact]
    public void Buffer_HoldsAtMostTwoSeconds()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture) { Armed = true };

        capture.Raise(new byte[48000 * 8 * 3]);

        Assert.Equal(2000.0, source.BufferedMilliseconds, 0.001);
    }

    [Fact]
    public void DropExcess_KeepsTheRequestedMilliseconds()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture) { Armed = true };
        capture.Raise(new byte[9600 * 12]);

        source.DropExcess(100);

        Assert.Equal(100.0, source.BufferedMilliseconds, 0.001);
    }

    [Fact]
    public void ClearBuffer_EmptiesTheBuffer()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture) { Armed = true };
        capture.Raise(new byte[9600]);

        source.ClearBuffer();

        Assert.Equal(0.0, source.BufferedMilliseconds);
    }

    [Fact]
    public void Provider_At44100Hz_IsResampledTo48kHzStereo()
    {
        var capture = new FakeAudioCapture { Format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2) };
        using var source = new AudioSource(capture);

        Assert.Equal(48000, source.Provider.WaveFormat.SampleRate);
        Assert.Equal(2, source.Provider.WaveFormat.Channels);
    }

    [Fact]
    public void Provider_Mono48kHz_IsDownmixedToStereoWithoutResampler()
    {
        var capture = new FakeAudioCapture { Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1) };
        using var source = new AudioSource(capture);

        Assert.IsType<StereoDownmixSampleProvider>(source.Provider);
        Assert.Equal(48000, source.Provider.WaveFormat.SampleRate);
        Assert.Equal(2, source.Provider.WaveFormat.Channels);
    }

    [Fact]
    public void Start_StartsTheCapture()
    {
        var capture = new FakeAudioCapture();
        using var source = new AudioSource(capture);

        source.Start();

        Assert.Equal(1, capture.Started);
    }

    [Fact]
    public void Stop_SwallowsCaptureErrorsAndResetsPeak()
    {
        var capture = new FakeAudioCapture { StopException = new InvalidOperationException("gone") };
        using var source = new AudioSource(capture);
        capture.Raise(Floats(0.75f, 0f));

        source.Stop();

        Assert.Equal(1, capture.Stops);
        Assert.Equal(0f, source.Peak);
    }

    [Fact]
    public void Constructor_UnsupportedFormat_DisposesTheCaptureAndThrows()
    {
        var capture = new FakeAudioCapture
        {
            Format = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Adpcm, 48000, 2, 48000 * 4, 4, 16)
        };

        Assert.Throws<ArgumentException>(() => new AudioSource(capture));
        Assert.Equal(1, capture.Disposed);
    }

    [Fact]
    public void Dispose_StopsDisarmsUnsubscribesAndDisposesTheCapture()
    {
        var capture = new FakeAudioCapture();
        var source = new AudioSource(capture) { Armed = true };

        source.Dispose();
        capture.Raise(Floats(0.75f, 0f));

        Assert.Equal(1, capture.Stops);
        Assert.Equal(1, capture.Disposed);
        Assert.False(source.Armed);
        Assert.Equal(0f, source.Peak);
        Assert.Equal(0.0, source.BufferedMilliseconds);
    }

    [Fact]
    public void Dispose_ResetsPeak()
    {
        var capture = new FakeAudioCapture();
        var source = new AudioSource(capture);
        capture.Raise(Floats(0.75f, 0f));

        source.Dispose();

        Assert.Equal(0f, source.Peak);
    }

    [Fact]
    public void Dispose_Twice_DisposesTheCaptureOnce()
    {
        var capture = new FakeAudioCapture();
        var source = new AudioSource(capture);

        source.Dispose();
        source.Dispose();

        Assert.Equal(1, capture.Disposed);
    }

    [Fact]
    public void Dispose_SwallowsCaptureErrors()
    {
        var capture = new FakeAudioCapture
        {
            StopException = new InvalidOperationException("stop"),
            DisposeException = new InvalidOperationException("dispose")
        };
        var source = new AudioSource(capture);

        source.Dispose();

        Assert.Equal(1, capture.Disposed);
    }

    private static void AssertPeak(WaveFormat format, byte[] data, double expected)
    {
        var capture = new FakeAudioCapture { Format = format };
        using var source = new AudioSource(capture);

        capture.Raise(data);

        Assert.Equal(expected, source.Peak, PeakTolerance);
    }

    internal static byte[] Floats(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Int16s(params short[] values)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Int32s(params int[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
