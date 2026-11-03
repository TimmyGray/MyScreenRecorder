using System.IO.Pipes;
using System.Runtime.Versioning;

namespace MyScreenRecorder.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNamedPipeFactory : IAudioPipeFactory
{
    public IAudioPipe Create(string sessionId, int index)
    {
        string name = $"srec_{sessionId}_{index}";
        return new WindowsNamedPipe(name, new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 0, 1 << 20));
    }
}
