using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Recording;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeRecordingSession : IRecordingSession
{
    public FakeRecordingSession(List<string>? log = null)
    {
        Log = log ?? new List<string>();
    }

    public event Action? StateChanged;

    public event Action<string>? Faulted;

    public RecordingState State { get; set; }

    public string? LastError { get; set; }

    public SessionStats Stats { get; set; } = new(TimeSpan.Zero, 0, string.Empty);

    public bool AutoTransitions { get; set; } = true;

    public List<string> Log { get; }

    public (CaptureTarget Target, VideoQuality Video, AudioQuality Audio)? StartArgs { get; private set; }

    public TaskCompletionSource? StartGate { get; set; }

    public TaskCompletionSource? PauseGate { get; set; }

    public TaskCompletionSource? ResumeGate { get; set; }

    public TaskCompletionSource? StopGate { get; set; }

    public TaskCompletionSource? CancelGate { get; set; }

    public Exception? StartThrow { get; set; }

    public Exception? PauseThrow { get; set; }

    public Exception? ResumeThrow { get; set; }

    public Exception? StopThrow { get; set; }

    public Exception? CancelThrow { get; set; }

    public string? StopResult { get; set; }

    public void RaiseStateChanged() => StateChanged?.Invoke();

    public void RaiseFaulted(string message) => Faulted?.Invoke(message);

    public async Task StartAsync(CaptureTarget target, VideoQuality vq, AudioQuality aq)
    {
        Log.Add("session.Start");
        StartArgs = (target, vq, aq);
        await Wait(StartGate);
        if (StartThrow != null) throw StartThrow;
        Transition(RecordingState.Recording);
    }

    public async Task PauseAsync()
    {
        Log.Add("session.Pause");
        await Wait(PauseGate);
        if (PauseThrow != null) throw PauseThrow;
        Transition(RecordingState.Paused);
    }

    public async Task ResumeAsync()
    {
        Log.Add("session.Resume");
        await Wait(ResumeGate);
        if (ResumeThrow != null) throw ResumeThrow;
        Transition(RecordingState.Recording);
    }

    public async Task<string?> StopAsync()
    {
        Log.Add("session.Stop");
        Transition(RecordingState.Finalizing);
        await Wait(StopGate);
        Transition(RecordingState.Idle);
        if (StopThrow != null) throw StopThrow;
        return StopResult;
    }

    public async Task CancelAsync()
    {
        Log.Add("session.Cancel");
        Transition(RecordingState.Finalizing);
        await Wait(CancelGate);
        Transition(RecordingState.Idle);
        if (CancelThrow != null) throw CancelThrow;
    }

    private static async Task Wait(TaskCompletionSource? gate)
    {
        if (gate != null) await gate.Task;
    }

    private void Transition(RecordingState state)
    {
        if (!AutoTransitions) return;
        State = state;
        StateChanged?.Invoke();
    }
}
