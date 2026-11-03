using System;
using System.Threading.Tasks;
using MyScreenRecorder.Capture;

namespace MyScreenRecorder.Recording;

public interface IRecordingSession
{
    event Action? StateChanged;

    event Action<string>? Faulted;

    RecordingState State { get; }

    string? LastError { get; }

    SessionStats Stats { get; }

    Task StartAsync(CaptureTarget target, VideoQuality vq, AudioQuality aq);

    Task PauseAsync();

    Task ResumeAsync();

    Task<string?> StopAsync();

    Task CancelAsync();
}
