using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MyScreenRecorder.Platform;

/// <summary>
/// Write side of the audio channel to one ffmpeg process. It accepts a connection from the moment
/// <see cref="IAudioPipeFactory.Create"/> returns.
/// </summary>
public interface IAudioPipe : IDisposable
{
    /// <summary>The value for ffmpeg <c>-i</c>.</summary>
    string FfmpegInput { get; }

    /// <summary>Write side carrying <c>f32le</c> 48 kHz stereo.</summary>
    Stream Stream { get; }

    /// <summary>
    /// Completes when ffmpeg connects. Throws <see cref="OperationCanceledException"/> when the token is cancelled
    /// and an <see cref="IOException"/> family exception on a broken endpoint.
    /// </summary>
    Task WaitForConnectionAsync(CancellationToken cancellationToken);
}
