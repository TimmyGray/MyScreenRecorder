using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyScreenRecorder.Platform;

namespace MyScreenRecorder.Core.Tests;

public enum PipeBehavior
{
    ConnectAtOnce,
    NeverConnect,
    Fault
}

public sealed class FakeAudioPipe : IAudioPipe
{
    private readonly MemoryStream _stream = new();
    private int _disposed;

    public FakeAudioPipe(string ffmpegInput, PipeBehavior behavior)
    {
        FfmpegInput = ffmpegInput;
        Behavior = behavior;
    }

    public PipeBehavior Behavior { get; }

    public string FfmpegInput { get; }

    public TimeSpan StreamGetterDelay { get; set; }

    public Stream Stream
    {
        get
        {
            if (StreamGetterDelay > TimeSpan.Zero) Thread.Sleep(StreamGetterDelay);
            return _stream;
        }
    }

    public byte[] WrittenBytes => _stream.ToArray();

    public bool StreamClosed => !_stream.CanWrite;

    public int Disposed => Volatile.Read(ref _disposed);

    public Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        switch (Behavior)
        {
            case PipeBehavior.ConnectAtOnce:
                return Task.CompletedTask;
            case PipeBehavior.Fault:
                return Task.FromException(new IOException("pipe broken"));
            default:
                return Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _disposed);
        _stream.Dispose();
    }
}
