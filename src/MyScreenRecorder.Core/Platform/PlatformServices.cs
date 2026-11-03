namespace MyScreenRecorder.Platform;

public sealed record PlatformServices(IAudioCaptureFactory AudioCaptures, IAudioDeviceCatalog AudioDevices);
