using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace MyScreenRecorder.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WasapiAudioCaptureFactory : IAudioCaptureFactory
{
    public IAudioCapture Open(string deviceId, bool loopback)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice device = enumerator.GetDevice(deviceId);
        return new WasapiAudioCapture(device, loopback);
    }
}
