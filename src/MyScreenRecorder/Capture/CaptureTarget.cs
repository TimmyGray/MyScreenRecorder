using System.Linq;

namespace MyScreenRecorder.Capture;

public enum TargetKind
{
    Monitor,
    All,
    Region
}

public sealed record CaptureTarget(TargetKind Kind, System.Drawing.Rectangle Bounds, string Label, int[] MonitorNumbers)
{
    public static CaptureTarget ForMonitor(MonitorInfo m)
    {
        var bounds = Even(m.Bounds);
        return new CaptureTarget(
            TargetKind.Monitor,
            bounds,
            $"MON {m.Number} {bounds.Width}x{bounds.Height}",
            new[] { m.Number });
    }

    public static CaptureTarget ForAll()
    {
        var bounds = Even(System.Windows.Forms.SystemInformation.VirtualScreen);
        var numbers = ScreenInfo.GetMonitors().Select(m => m.Number).ToArray();
        return new CaptureTarget(
            TargetKind.All,
            bounds,
            $"ALL {bounds.Width}x{bounds.Height}",
            numbers);
    }

    public static CaptureTarget ForRegion(System.Drawing.Rectangle bounds, int monitorNumber)
    {
        var monitor = ScreenInfo.GetMonitors().FirstOrDefault(m => m.Number == monitorNumber);
        if (monitor != null)
        {
            var clamped = System.Drawing.Rectangle.Intersect(bounds, monitor.Bounds);
            bounds = clamped.IsEmpty ? monitor.Bounds : clamped;
        }
        bounds = Even(bounds);
        return new CaptureTarget(
            TargetKind.Region,
            bounds,
            $"REGION {bounds.Width}x{bounds.Height} @MON {monitorNumber}",
            new[] { monitorNumber });
    }

    private static System.Drawing.Rectangle Even(System.Drawing.Rectangle r)
    {
        int w = r.Width & ~1;
        int h = r.Height & ~1;
        return new System.Drawing.Rectangle(r.X, r.Y, w, h);
    }
}
