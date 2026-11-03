using System.Collections.Generic;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Platform;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeAudioDeviceCatalog : IAudioDeviceCatalog
{
    public List<AudioDeviceInfo> InputList { get; set; } = new()
    {
        new AudioDeviceInfo("in-1", "Mic One"),
        new AudioDeviceInfo("in-2", "Mic Two")
    };

    public List<AudioDeviceInfo> OutputList { get; set; } = new()
    {
        new AudioDeviceInfo("out-1", "Speakers"),
        new AudioDeviceInfo("out-2", "Headset")
    };

    public string? DefaultInput { get; set; } = "in-2";

    public string? DefaultOutput { get; set; } = "out-1";

    public IReadOnlyList<AudioDeviceInfo> Inputs() => InputList;

    public IReadOnlyList<AudioDeviceInfo> Outputs() => OutputList;

    public string? DefaultInputId() => DefaultInput;

    public string? DefaultOutputId() => DefaultOutput;
}
