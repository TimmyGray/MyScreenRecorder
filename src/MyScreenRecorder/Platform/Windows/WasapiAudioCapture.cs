using System;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyScreenRecorder.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class WasapiAudioCapture : IAudioCapture
{
    private readonly MMDevice _device;
    private readonly WasapiCapture _capture;

    public WasapiAudioCapture(MMDevice device, bool loopback)
    {
        _device = device;
        try
        {
            _capture = loopback ? new WasapiLoopbackCapture(_device) : new WasapiCapture(_device);
        }
        catch
        {
            _device.Dispose();
            throw;
        }
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public WaveFormat Format => _capture.WaveFormat;

    public event Action<byte[], int>? DataAvailable;

    public event Action? Stopped;

    public void Start()
    {
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture.StopRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        DataAvailable?.Invoke(e.Buffer, e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Stopped?.Invoke();
    }

    public void Dispose()
    {
        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
        }
        catch (Exception)
        {
        }

        try
        {
            _capture.StopRecording();
        }
        catch (Exception)
        {
        }

        try
        {
            _capture.Dispose();
        }
        catch (Exception)
        {
        }

        try
        {
            _device.Dispose();
        }
        catch (Exception)
        {
        }
    }
}
