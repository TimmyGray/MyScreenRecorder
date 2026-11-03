using System.Collections.Generic;
using MyScreenRecorder.Capture;

namespace MyScreenRecorder.Platform;

/// <summary>Lists the monitors of the desktop. Ordered by <c>Left</c>, then <c>Top</c>; numbered from 1 in that order; exactly one entry has <c>IsPrimary</c> true when the platform has a primary monitor. Called on the UI thread, once per controller. May throw when the display cannot be queried; an empty list is a failure the caller reports.</summary>
public interface IScreenCatalog
{
    IReadOnlyList<MonitorInfo> GetMonitors();
}
