using System;
using System.Drawing;
using MyScreenRecorder.Capture;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class CaptureTargetTests
{
    private static readonly MonitorInfo[] DevMachine =
    {
        new(1, new Rectangle(0, 116, 1920, 1080), false),
        new(2, new Rectangle(1920, 0, 2560, 1440), true),
        new(3, new Rectangle(4480, 143, 1920, 1080), false)
    };

    private static readonly MonitorInfo[] SingleMonitor =
    {
        new(1, new Rectangle(0, 0, 1920, 1080), true)
    };

    [Fact]
    public void ForAll_IsTheUnionOfMonitorBounds()
    {
        CaptureTarget target = CaptureTarget.ForAll(DevMachine);

        Assert.Equal(TargetKind.All, target.Kind);
        Assert.Equal(new Rectangle(0, 0, 6400, 1440), target.Bounds);
        Assert.Equal("ALL 6400x1440", target.Label);
        Assert.Equal(new[] { 1, 2, 3 }, target.MonitorNumbers);
    }

    [Fact]
    public void ForAll_ForcesEvenSize()
    {
        MonitorInfo[] monitors =
        {
            new(1, new Rectangle(0, 0, 1920, 1080), true),
            new(2, new Rectangle(1920, 1, 1920, 1080), false)
        };

        CaptureTarget target = CaptureTarget.ForAll(monitors);

        Assert.Equal(new Rectangle(0, 0, 3840, 1080), target.Bounds);
        Assert.Equal("ALL 3840x1080", target.Label);
    }

    [Fact]
    public void ForAll_EmptyMonitorList_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => CaptureTarget.ForAll(Array.Empty<MonitorInfo>()));

        Assert.Equal("monitors", ex.ParamName);
    }

    [Fact]
    public void ForRegion_ForcesEvenSize()
    {
        CaptureTarget target = CaptureTarget.ForRegion(new Rectangle(100, 100, 641, 361), SingleMonitor, 1);

        Assert.Equal(TargetKind.Region, target.Kind);
        Assert.Equal(new Rectangle(100, 100, 640, 360), target.Bounds);
        Assert.Equal("REGION 640x360 @MON 1", target.Label);
        Assert.Equal(new[] { 1 }, target.MonitorNumbers);
    }

    [Fact]
    public void ForRegion_ClampsToTheMonitor()
    {
        CaptureTarget target = CaptureTarget.ForRegion(new Rectangle(1800, 1000, 400, 300), SingleMonitor, 1);

        Assert.Equal(new Rectangle(1800, 1000, 120, 80), target.Bounds);
    }

    [Fact]
    public void ForRegion_WithoutOverlap_TakesTheWholeMonitor()
    {
        CaptureTarget target = CaptureTarget.ForRegion(new Rectangle(3000, 3000, 200, 200), SingleMonitor, 1);

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), target.Bounds);
    }

    [Fact]
    public void ForRegion_UnknownMonitor_IsNotClamped()
    {
        CaptureTarget target = CaptureTarget.ForRegion(new Rectangle(10, 10, 201, 101), SingleMonitor, 9);

        Assert.Equal(new Rectangle(10, 10, 200, 100), target.Bounds);
        Assert.Equal("REGION 200x100 @MON 9", target.Label);
    }

    [Fact]
    public void ForMonitor_ForcesEvenSize()
    {
        CaptureTarget target = CaptureTarget.ForMonitor(new MonitorInfo(1, new Rectangle(0, 0, 1365, 767), true));

        Assert.Equal(TargetKind.Monitor, target.Kind);
        Assert.Equal(new Rectangle(0, 0, 1364, 766), target.Bounds);
        Assert.Equal("MON 1 1364x766", target.Label);
        Assert.Equal(new[] { 1 }, target.MonitorNumbers);
    }
}
