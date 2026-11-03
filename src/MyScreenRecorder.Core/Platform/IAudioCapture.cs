using System;
using NAudio.Wave;

namespace MyScreenRecorder.Platform;

/// <summary>
/// One open audio capture stream of a device. Created by <see cref="IAudioCaptureFactory.Open"/>;
/// owns the underlying device handle and releases it in <see cref="IDisposable.Dispose"/>.
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>Format of the bytes delivered by <see cref="DataAvailable"/>; fixed for the lifetime of the capture.</summary>
    WaveFormat Format { get; }

    /// <summary>
    /// Raised on a capture thread with a buffer and its valid byte count. The buffer may be reused
    /// after the handler returns, so handlers copy what they keep before returning.
    /// </summary>
    event Action<byte[], int>? DataAvailable;

    /// <summary>Raised once capturing has stopped, after <see cref="Stop"/> or when capturing fails.</summary>
    event Action? Stopped;

    /// <summary>Starts capturing. Throws if the device cannot be started.</summary>
    void Start();

    /// <summary>Requests the capture to stop; <see cref="Stopped"/> follows once it has stopped.</summary>
    void Stop();
}
