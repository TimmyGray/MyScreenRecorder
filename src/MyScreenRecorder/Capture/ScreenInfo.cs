using System.Collections.Generic;
using System.Linq;

namespace MyScreenRecorder.Capture;

public sealed record MonitorInfo(int Number, System.Drawing.Rectangle Bounds, bool IsPrimary);

public static class ScreenInfo
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var screens = System.Windows.Forms.Screen.AllScreens
            .OrderBy(s => s.Bounds.Left)
            .ThenBy(s => s.Bounds.Top)
            .ToList();

        var result = new List<MonitorInfo>(screens.Count);
        for (int i = 0; i < screens.Count; i++)
        {
            result.Add(new MonitorInfo(i + 1, screens[i].Bounds, screens[i].Primary));
        }
        return result;
    }
}
