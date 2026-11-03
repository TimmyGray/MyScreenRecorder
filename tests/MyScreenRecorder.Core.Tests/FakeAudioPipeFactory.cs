using System;
using System.Collections.Generic;
using MyScreenRecorder.Platform;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeAudioPipeFactory : IAudioPipeFactory
{
    private readonly object _gate = new();

    public PipeBehavior Behavior { get; set; } = PipeBehavior.ConnectAtOnce;

    public TimeSpan StreamGetterDelay { get; set; }

    public List<(string SessionId, int Index)> Calls { get; } = new();

    public List<FakeAudioPipe> Pipes { get; } = new();

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return Calls.Count;
            }
        }
    }

    public IAudioPipe Create(string sessionId, int index)
    {
        lock (_gate)
        {
            Calls.Add((sessionId, index));
            var pipe = new FakeAudioPipe($"fakepipe:{sessionId}_{index}", Behavior) { StreamGetterDelay = StreamGetterDelay };
            Pipes.Add(pipe);
            return pipe;
        }
    }
}
