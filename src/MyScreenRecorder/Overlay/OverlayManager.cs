using System;
using System.Collections.Generic;
using System.Linq;
using MyScreenRecorder.Capture;

namespace MyScreenRecorder.Overlay;

public enum OverlayState
{
    Idle,
    Recording,
    Paused
}

public sealed class OverlayManager : IDisposable
{
    private static readonly System.Windows.Media.Brush GreenBrush = Create(0x00, 0xFF, 0x66);
    private static readonly System.Windows.Media.Brush RedBrush = Create(0xFF, 0x22, 0x44);
    private static readonly System.Windows.Media.Brush YellowBrush = Create(0xFF, 0xCC, 0x00);

    private readonly List<MonitorOverlay> _windows = new();
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private bool _visible = true;

    public void Update(CaptureTarget? target, OverlayState state)
    {
        EnsureWindows();

        System.Windows.Media.Brush brush = state switch
        {
            OverlayState.Recording => RedBrush,
            OverlayState.Paused => YellowBrush,
            _ => GreenBrush
        };
        bool showBadge = state == OverlayState.Idle;

        for (int i = 0; i < _windows.Count && i < _monitors.Count; i++)
        {
            var monitor = _monitors[i];
            bool inTarget = target != null && target.MonitorNumbers.Contains(monitor.Number);
            System.Drawing.Rectangle? region = null;
            if (inTarget && target!.Kind == TargetKind.Region)
            {
                region = target.Bounds;
            }
            _windows[i].Apply(showBadge, inTarget, brush, region);
        }
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        foreach (var window in _windows)
        {
            window.SetVisible(visible);
        }
    }

    public void CloseAll()
    {
        foreach (var window in _windows)
        {
            window.Close();
        }
        _windows.Clear();
        _monitors = Array.Empty<MonitorInfo>();
    }

    public void Dispose()
    {
        CloseAll();
    }

    private void EnsureWindows()
    {
        var current = ScreenInfo.GetMonitors();
        if (_windows.Count > 0 && current.SequenceEqual(_monitors))
        {
            return;
        }

        CloseAll();
        _monitors = current;
        foreach (var monitor in current)
        {
            var window = new MonitorOverlay(monitor);
            window.SetVisible(_visible);
            _windows.Add(window);
        }
    }

    private static System.Windows.Media.Brush Create(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
