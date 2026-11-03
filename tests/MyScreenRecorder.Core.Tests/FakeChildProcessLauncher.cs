using System;
using System.Collections.Generic;
using System.Diagnostics;
using MyScreenRecorder.Platform;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeChildProcessLauncher : IChildProcessLauncher
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Exception> _throwOnCall = new();

    public List<ProcessStartInfo> StartInfos { get; } = new();

    public List<int> ProcessIds { get; } = new();

    public Action<ProcessStartInfo>? BeforeStart { get; set; }

    public int Calls
    {
        get
        {
            lock (_gate)
            {
                return StartInfos.Count;
            }
        }
    }

    public void ThrowOnCall(int callNumber, Exception exception)
    {
        lock (_gate)
        {
            _throwOnCall[callNumber] = exception;
        }
    }

    public void Start(Process process)
    {
        int call;
        Exception? toThrow;
        lock (_gate)
        {
            StartInfos.Add(process.StartInfo);
            call = StartInfos.Count;
            _throwOnCall.TryGetValue(call, out toThrow);
        }

        BeforeStart?.Invoke(process.StartInfo);
        if (toThrow != null)
        {
            throw toThrow;
        }

        process.Start();
        lock (_gate)
        {
            ProcessIds.Add(process.Id);
        }
    }

    public static bool IsAlive(int pid)
    {
        try
        {
            using Process p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
