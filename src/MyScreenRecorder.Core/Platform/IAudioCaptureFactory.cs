namespace MyScreenRecorder.Platform;

/// <summary>Opens capture streams for devices listed by <see cref="IAudioDeviceCatalog"/>.</summary>
public interface IAudioCaptureFactory
{
    /// <summary>
    /// Opens a capture of <paramref name="deviceId"/> without starting it; <paramref name="loopback"/> selects
    /// capturing what an output device plays instead of an input device. The caller owns the result.
    /// Throws if <paramref name="deviceId"/> is not a currently present device; never falls back to another device.
    /// </summary>
    IAudioCapture Open(string deviceId, bool loopback);
}
