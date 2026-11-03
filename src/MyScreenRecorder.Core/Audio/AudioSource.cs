using System;
using System.Threading;
using MyScreenRecorder.Platform;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MyScreenRecorder.Audio;

public sealed class AudioSource : IDisposable
{
    private const int TargetRate = 48000;
    private const long PeakTimeoutMs = 300;

    private readonly IAudioCapture _capture;
    private readonly WaveFormat _format;
    private readonly BufferedWaveProvider _buffered;
    private readonly bool _isFloat;
    private volatile float _peak;
    private volatile bool _armed;
    private long _lastDataTicks;
    private byte[] _scratch = new byte[16384];
    private bool _disposed;

    public AudioSource(IAudioCapture capture)
    {
        _capture = capture;
        try
        {
            var captureFormat = _capture.Format;
            _format = captureFormat is WaveFormatExtensible ext ? ext.ToStandardWaveFormat() : captureFormat;
            _isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat;

            _buffered = new BufferedWaveProvider(_format)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
                ReadFully = true
            };

            ISampleProvider chain = _buffered.ToSampleProvider();
            chain = new StereoDownmixSampleProvider(chain);
            if (chain.WaveFormat.SampleRate != TargetRate)
            {
                chain = new WdlResamplingSampleProvider(chain, TargetRate);
            }
            Provider = chain;

            _capture.DataAvailable += OnDataAvailable;
            _capture.Stopped += OnStopped;
        }
        catch
        {
            _capture.Dispose();
            throw;
        }
    }

    public ISampleProvider Provider { get; }

    public float Peak
    {
        get
        {
            long last = Volatile.Read(ref _lastDataTicks);
            if (last == 0 || Environment.TickCount64 - last > PeakTimeoutMs)
            {
                return 0f;
            }
            return _peak;
        }
    }

    public bool Armed
    {
        get => _armed;
        set => _armed = value;
    }

    public double BufferedMilliseconds
    {
        get
        {
            int avg = _format.AverageBytesPerSecond;
            return avg <= 0 ? 0 : _buffered.BufferedBytes * 1000.0 / avg;
        }
    }

    public void Start()
    {
        _capture.Start();
    }

    public void Stop()
    {
        try
        {
            _capture.Stop();
        }
        catch (Exception)
        {
        }
        _peak = 0f;
    }

    public void ClearBuffer()
    {
        _buffered.ClearBuffer();
    }

    public void DropExcess(int keepMilliseconds)
    {
        int avg = _format.AverageBytesPerSecond;
        int align = Math.Max(1, _format.BlockAlign);
        if (avg <= 0)
        {
            return;
        }

        long keepBytes = (long)keepMilliseconds * avg / 1000;
        keepBytes -= keepBytes % align;
        long excess = _buffered.BufferedBytes - keepBytes;
        excess -= excess % align;

        while (excess > 0)
        {
            int chunk = (int)Math.Min(excess, _scratch.Length - (_scratch.Length % align));
            if (chunk <= 0)
            {
                break;
            }
            int read = _buffered.Read(_scratch, 0, chunk);
            if (read <= 0)
            {
                break;
            }
            excess -= read;
        }
    }

    private void OnDataAvailable(byte[] buffer, int bytes)
    {
        try
        {
            _peak = ComputePeak(buffer, bytes);
            Volatile.Write(ref _lastDataTicks, Math.Max(1L, Environment.TickCount64));

            if (_armed && bytes > 0)
            {
                _buffered.AddSamples(buffer, 0, bytes);
            }
        }
        catch (Exception)
        {
        }
    }

    private void OnStopped()
    {
        _peak = 0f;
        Volatile.Write(ref _lastDataTicks, 0L);
    }

    private float ComputePeak(byte[] data, int bytes)
    {
        int bits = _format.BitsPerSample;
        float max = 0f;

        if (_isFloat && bits == 32)
        {
            int count = bytes / 4;
            for (int i = 0; i < count; i++)
            {
                float v = Math.Abs(BitConverter.ToSingle(data, i * 4));
                if (v > max)
                {
                    max = v;
                }
            }
        }
        else if (bits == 16)
        {
            int count = bytes / 2;
            for (int i = 0; i < count; i++)
            {
                float v = Math.Abs(BitConverter.ToInt16(data, i * 2) / 32768f);
                if (v > max)
                {
                    max = v;
                }
            }
        }
        else if (bits == 24)
        {
            int count = bytes / 3;
            for (int i = 0; i < count; i++)
            {
                int idx = i * 3;
                int sample = (data[idx + 2] << 24) | (data[idx + 1] << 16) | (data[idx] << 8);
                float v = Math.Abs(sample / 2147483648f);
                if (v > max)
                {
                    max = v;
                }
            }
        }
        else if (bits == 32)
        {
            int count = bytes / 4;
            for (int i = 0; i < count; i++)
            {
                float v = Math.Abs(BitConverter.ToInt32(data, i * 4) / 2147483648f);
                if (v > max)
                {
                    max = v;
                }
            }
        }

        if (float.IsNaN(max))
        {
            return 0f;
        }
        return Math.Min(1f, max);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _armed = false;

        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.Stopped -= OnStopped;
        }
        catch (Exception)
        {
        }

        try
        {
            _capture.Stop();
        }
        catch (Exception)
        {
        }

        try
        {
            _capture.Dispose();
        }
        catch (Exception)
        {
        }

        _peak = 0f;
    }
}
