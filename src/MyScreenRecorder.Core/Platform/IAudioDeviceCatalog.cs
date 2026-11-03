using System.Collections.Generic;
using MyScreenRecorder.Audio;

namespace MyScreenRecorder.Platform;

/// <summary>
/// Lists the active audio devices whose ids <see cref="IAudioCaptureFactory.Open"/> accepts.
/// Never throws: when enumeration fails, lists are empty and default ids are <c>null</c>;
/// a single device that cannot be read is left out of its list.
/// </summary>
public interface IAudioDeviceCatalog
{
    /// <summary>Active input devices (microphones).</summary>
    IReadOnlyList<AudioDeviceInfo> Inputs();

    /// <summary>Active output devices, captured with <c>loopback: true</c>.</summary>
    IReadOnlyList<AudioDeviceInfo> Outputs();

    /// <summary>Id of the default input device, or <c>null</c> if there is none or it cannot be read.</summary>
    string? DefaultInputId();

    /// <summary>Id of the default output device, or <c>null</c> if there is none or it cannot be read.</summary>
    string? DefaultOutputId();
}
