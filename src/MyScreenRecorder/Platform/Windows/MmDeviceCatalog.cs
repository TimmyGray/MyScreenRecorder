using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using MyScreenRecorder.Audio;
using NAudio.CoreAudioApi;

namespace MyScreenRecorder.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class MmDeviceCatalog : IAudioDeviceCatalog
{
    public IReadOnlyList<AudioDeviceInfo> Inputs()
    {
        return Enumerate(DataFlow.Capture);
    }

    public IReadOnlyList<AudioDeviceInfo> Outputs()
    {
        return Enumerate(DataFlow.Render);
    }

    public string? DefaultInputId()
    {
        return DefaultId(DataFlow.Capture);
    }

    public string? DefaultOutputId()
    {
        return DefaultId(DataFlow.Render);
    }

    private static IReadOnlyList<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                try
                {
                    result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
                }
                catch (Exception)
                {
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch (Exception)
        {
        }
        return result;
    }

    private static string? DefaultId(DataFlow flow)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
            return device.ID;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
