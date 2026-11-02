using System;
using NAudio.Wave;

namespace MyScreenRecorder.Audio;

public sealed class StereoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _buffer = Array.Empty<float>();

    public StereoDownmixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_channels == 2)
        {
            return _source.Read(buffer, offset, count);
        }

        int framesWanted = count / 2;
        int needed = framesWanted * _channels;
        if (_buffer.Length < needed)
        {
            _buffer = new float[needed];
        }

        int samplesRead = _source.Read(_buffer, 0, needed);
        int framesRead = samplesRead / _channels;

        if (_channels == 1)
        {
            for (int f = 0; f < framesRead; f++)
            {
                float v = _buffer[f];
                buffer[offset + f * 2] = v;
                buffer[offset + f * 2 + 1] = v;
            }
        }
        else
        {
            int leftCount = (_channels + 1) / 2;
            int rightCount = _channels / 2;
            for (int f = 0; f < framesRead; f++)
            {
                int baseIndex = f * _channels;
                float left = 0f;
                float right = 0f;
                for (int c = 0; c < _channels; c++)
                {
                    if ((c & 1) == 0)
                    {
                        left += _buffer[baseIndex + c];
                    }
                    else
                    {
                        right += _buffer[baseIndex + c];
                    }
                }
                buffer[offset + f * 2] = left / leftCount;
                buffer[offset + f * 2 + 1] = right / rightCount;
            }
        }

        return framesRead * 2;
    }
}
