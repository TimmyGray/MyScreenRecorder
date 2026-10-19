using System;
using MyScreenRecorder.Capture;

namespace MyScreenRecorder.Overlay;

internal sealed class MonitorOverlay
{
    private const int Thickness = 6;
    private const int BadgeSize = 88;
    private const int BadgeMargin = 14;

    private readonly MonitorInfo _monitor;
    private readonly OverlayPiece _badge;
    private readonly OverlayPiece[] _edges = new OverlayPiece[4];
    private bool _visible = true;
    private bool _wantBadge;
    private bool _wantBorder;

    public MonitorOverlay(MonitorInfo monitor)
    {
        _monitor = monitor;
        _badge = new OverlayPiece(monitor.Number);
        _badge.Place(new System.Drawing.Rectangle(
            monitor.Bounds.X + BadgeMargin,
            monitor.Bounds.Y + BadgeMargin,
            BadgeSize,
            BadgeSize));
        for (int i = 0; i < _edges.Length; i++)
        {
            _edges[i] = new OverlayPiece();
        }
    }

    public void Apply(bool showBadge, bool showBorder, System.Windows.Media.Brush brush, System.Drawing.Rectangle? region)
    {
        _wantBadge = showBadge;
        _wantBorder = showBorder;

        if (showBorder)
        {
            System.Drawing.Rectangle outer = region.HasValue
                ? System.Drawing.Rectangle.Intersect(
                    System.Drawing.Rectangle.Inflate(region.Value, Thickness, Thickness),
                    _monitor.Bounds)
                : _monitor.Bounds;

            _edges[0].Place(new System.Drawing.Rectangle(outer.X, outer.Y, outer.Width, Thickness));
            _edges[1].Place(new System.Drawing.Rectangle(outer.X, outer.Bottom - Thickness, outer.Width, Thickness));
            _edges[2].Place(new System.Drawing.Rectangle(outer.X, outer.Y + Thickness, Thickness, Math.Max(1, outer.Height - 2 * Thickness)));
            _edges[3].Place(new System.Drawing.Rectangle(outer.Right - Thickness, outer.Y + Thickness, Thickness, Math.Max(1, outer.Height - 2 * Thickness)));
            foreach (OverlayPiece edge in _edges)
            {
                edge.SetFill(brush);
            }
        }

        UpdateVisibility();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateVisibility();
    }

    public void Close()
    {
        Close(_badge);
        foreach (OverlayPiece edge in _edges)
        {
            Close(edge);
        }
    }

    private void UpdateVisibility()
    {
        Show(_badge, _visible && _wantBadge);
        foreach (OverlayPiece edge in _edges)
        {
            Show(edge, _visible && _wantBorder);
        }
    }

    private static void Show(OverlayPiece piece, bool show)
    {
        if (show && !piece.IsVisible)
        {
            piece.Show();
        }
        else if (!show && piece.IsVisible)
        {
            piece.Hide();
        }
    }

    private static void Close(OverlayPiece piece)
    {
        try
        {
            piece.Close();
        }
        catch (InvalidOperationException)
        {
        }
    }
}
