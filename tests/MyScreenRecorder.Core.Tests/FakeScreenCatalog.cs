using System.Collections.Generic;
using System.Drawing;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Platform;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeScreenCatalog : IScreenCatalog
{
    private readonly MonitorInfo[] _monitors;

    public FakeScreenCatalog(params MonitorInfo[] monitors)
    {
        _monitors = monitors;
    }

    public int Calls { get; private set; }

    public static FakeScreenCatalog Standard() => new(
        new MonitorInfo(1, new Rectangle(0, 116, 1920, 1080), false),
        new MonitorInfo(2, new Rectangle(1920, 0, 2560, 1440), true),
        new MonitorInfo(3, new Rectangle(4480, 143, 1920, 1080), false));

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        Calls++;
        return _monitors;
    }
}
