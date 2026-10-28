using System;
using MyScreenRecorder.Audio;
using NAudio.Wave;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class StereoDownmixSampleProviderTests
{
    [Fact]
    public void Mono_IsCopiedToBothChannels()
    {
        float[] output = ReadOneFrame(new ArraySampleProvider(1, 0.25f));

        Assert.Equal(new[] { 0.25f, 0.25f }, output);
    }

    [Fact]
    public void FourChannels_AverageEvenChannelsLeftAndOddChannelsRight()
    {
        float[] output = ReadOneFrame(new ArraySampleProvider(4, 1f, 2f, 3f, 4f));

        Assert.Equal(new[] { 2f, 3f }, output);
    }

    [Fact]
    public void Stereo_PassesThrough()
    {
        float[] output = ReadOneFrame(new ArraySampleProvider(2, 0.5f, -0.5f));

        Assert.Equal(new[] { 0.5f, -0.5f }, output);
    }

    [Fact]
    public void OutputFormat_IsStereoFloatAtTheSourceRate()
    {
        var provider = new StereoDownmixSampleProvider(new ArraySampleProvider(4));

        Assert.Equal(WaveFormatEncoding.IeeeFloat, provider.WaveFormat.Encoding);
        Assert.Equal(2, provider.WaveFormat.Channels);
        Assert.Equal(ArraySampleProvider.SampleRate, provider.WaveFormat.SampleRate);
    }

    private static float[] ReadOneFrame(ISampleProvider source)
    {
        var provider = new StereoDownmixSampleProvider(source);
        var buffer = new float[2];

        int read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(2, read);
        return buffer;
    }

    private sealed class ArraySampleProvider : ISampleProvider
    {
        public const int SampleRate = 44100;

        private readonly float[] _samples;
        private int _position;

        public ArraySampleProvider(int channels, params float[] samples)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels);
            _samples = samples;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _samples.Length - _position);
            Array.Copy(_samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
