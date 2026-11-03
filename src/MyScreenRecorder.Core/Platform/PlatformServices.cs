using MyScreenRecorder.Recording;

namespace MyScreenRecorder.Platform;

public sealed record PlatformServices(
    IAudioCaptureFactory AudioCaptures,
    IAudioDeviceCatalog AudioDevices,
    IChildProcessLauncher ProcessLauncher,
    IAudioPipeFactory AudioPipes,
    ScreenGrab Grab,
    FfmpegLocatorOptions Ffmpeg);
