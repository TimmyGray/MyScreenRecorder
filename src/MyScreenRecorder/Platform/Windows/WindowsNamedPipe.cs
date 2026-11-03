using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace MyScreenRecorder.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNamedPipe : IAudioPipe
{
    private readonly NamedPipeServerStream _pipe;

    public WindowsNamedPipe(string name, NamedPipeServerStream pipe)
    {
        FfmpegInput = @"\\.\pipe\" + name;
        _pipe = pipe;
    }

    public string FfmpegInput { get; }

    public Stream Stream => _pipe;

    public Task WaitForConnectionAsync(CancellationToken cancellationToken) => _pipe.WaitForConnectionAsync(cancellationToken);

    public void Dispose()
    {
        _pipe.Dispose();
    }
}
