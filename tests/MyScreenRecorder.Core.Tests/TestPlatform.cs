using System.Collections.Generic;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Platform;
using MyScreenRecorder.Recording;

namespace MyScreenRecorder.Core.Tests;

public sealed class EmptyAudioDeviceCatalog : IAudioDeviceCatalog
{
    public IReadOnlyList<AudioDeviceInfo> Inputs() => new List<AudioDeviceInfo>();

    public IReadOnlyList<AudioDeviceInfo> Outputs() => new List<AudioDeviceInfo>();

    public string? DefaultInputId() => null;

    public string? DefaultOutputId() => null;
}

public static class TestPlatform
{
    public static PlatformServices Create(
        FakeAudioCaptureFactory captures,
        FakeChildProcessLauncher launcher,
        FakeAudioPipeFactory pipes,
        ScreenGrab grab,
        FfmpegLocatorOptions ffmpeg) =>
        new(captures, new EmptyAudioDeviceCatalog(), launcher, pipes, grab, ffmpeg);
}
