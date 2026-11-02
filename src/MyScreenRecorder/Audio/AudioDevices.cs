using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace MyScreenRecorder.Audio;

public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> Capture()
    {
        return Enumerate(DataFlow.Capture);
    }

    public static IReadOnlyList<AudioDeviceInfo> Render()
    {
        return Enumerate(DataFlow.Render);
    }

    public static string? DefaultCaptureId()
    {
        return DefaultId(DataFlow.Capture);
    }

    public static string? DefaultRenderId()
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
