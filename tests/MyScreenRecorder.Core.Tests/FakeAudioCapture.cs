using System;
using System.Collections.Generic;
using MyScreenRecorder.Platform;
using NAudio.Wave;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeAudioCapture : IAudioCapture
{
    public WaveFormat Format { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public event Action<byte[], int>? DataAvailable;

    public event Action? Stopped;

    public Exception? StartException { get; set; }

    public Exception? StopException { get; set; }

    public Exception? DisposeException { get; set; }

    public int Started { get; private set; }

    public int Stops { get; private set; }

    public int Disposed { get; private set; }

    public void Raise(byte[] data)
    {
        DataAvailable?.Invoke(data, data.Length);
    }

    public void RaiseStopped()
    {
        Stopped?.Invoke();
    }

    public void Start()
    {
        if (StartException != null)
        {
            throw StartException;
        }
        Started++;
    }

    public void Stop()
    {
        Stops++;
        if (StopException != null)
        {
            throw StopException;
        }
    }

    public void Dispose()
    {
        Disposed++;
        if (DisposeException != null)
        {
            throw DisposeException;
        }
    }
}

public sealed class FakeAudioCaptureFactory : IAudioCaptureFactory
{
    public List<(string DeviceId, bool Loopback)> Calls { get; } = new();

    public List<FakeAudioCapture> Created { get; } = new();

    public Exception? OpenException { get; set; }

    public Exception? StartException { get; set; }

    public IAudioCapture Open(string deviceId, bool loopback)
    {
        Calls.Add((deviceId, loopback));
        if (OpenException != null)
        {
            throw OpenException;
        }
        var capture = new FakeAudioCapture { StartException = StartException };
        Created.Add(capture);
        return capture;
    }
}
