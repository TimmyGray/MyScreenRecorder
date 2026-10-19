using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Native;

namespace MyScreenRecorder.Overlay;

public sealed record RegionResult(System.Drawing.Rectangle Bounds, int MonitorNumber);

public static class RegionSelector
{
    public static Task<RegionResult?> SelectAsync()
    {
        var tcs = new TaskCompletionSource<RegionResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var windows = new List<RegionSelectWindow>();
        bool done = false;

        void Finish(RegionResult? result)
        {
            if (done)
            {
                return;
            }
            done = true;
            foreach (var window in windows)
            {
                try
                {
                    window.Close();
                }
                catch (InvalidOperationException)
                {
                }
            }
            windows.Clear();
            tcs.TrySetResult(result);
        }

        var monitors = ScreenInfo.GetMonitors();
        if (monitors.Count == 0)
        {
            tcs.TrySetResult(null);
            return tcs.Task;
        }

        foreach (var monitor in monitors)
        {
            windows.Add(new RegionSelectWindow(monitor, Finish));
        }

        var cursor = System.Windows.Forms.Cursor.Position;
        var active = windows.FirstOrDefault(w => w.MonitorBounds.Contains(cursor)) ?? windows[0];

        foreach (var window in windows.ToList())
        {
            window.Show();
        }

        active.Activate();
        active.Focus();

        return tcs.Task;
    }
}

internal sealed class RegionSelectWindow : Window
{
    private const int MinSize = 64;

    private readonly MonitorInfo _monitor;
    private readonly Action<RegionResult?> _finish;
    private readonly Canvas _canvas;
    private readonly System.Windows.Shapes.Path _dim;
    private readonly System.Windows.Shapes.Rectangle _outline;

    private bool _dragging;
    private System.Windows.Point _startDip;
    private System.Drawing.Point _startPx;

    public RegionSelectWindow(MonitorInfo monitor, Action<RegionResult?> finish)
    {
        _monitor = monitor;
        _finish = finish;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x01, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;
        Left = monitor.Bounds.X;
        Top = monitor.Bounds.Y;
        Width = monitor.Bounds.Width;
        Height = monitor.Bounds.Height;

        var dimBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0, 0, 0));
        dimBrush.Freeze();

        _dim = new System.Windows.Shapes.Path
        {
            Fill = dimBrush,
            IsHitTestVisible = false
        };

        _outline = new System.Windows.Shapes.Rectangle
        {
            Stroke = System.Windows.Media.Brushes.White,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _canvas = new Canvas
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x01, 0, 0, 0)),
            Cursor = Cursors.Cross
        };
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_outline);

        Content = _canvas;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => UpdateDim(null);
        PreviewKeyDown += OnPreviewKeyDown;
        Closed += (_, _) => _finish(null);
    }

    public System.Drawing.Rectangle MonitorBounds => _monitor.Bounds;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _startDip = e.GetPosition(_canvas);
        System.Windows.Point screen = PointToScreen(e.GetPosition(this));
        _startPx = new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        _dragging = true;
        CaptureMouse();
        UpdateSelection(_startDip);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            UpdateSelection(e.GetPosition(_canvas));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;

        System.Windows.Point screen = PointToScreen(e.GetPosition(this));
        var endPx = new System.Drawing.Point((int)Math.Round(screen.X), (int)Math.Round(screen.Y));

        int x = Math.Min(_startPx.X, endPx.X);
        int y = Math.Min(_startPx.Y, endPx.Y);
        int w = Math.Abs(endPx.X - _startPx.X);
        int h = Math.Abs(endPx.Y - _startPx.Y);
        var rect = System.Drawing.Rectangle.Intersect(new System.Drawing.Rectangle(x, y, w, h), _monitor.Bounds);

        if (rect.Width < MinSize || rect.Height < MinSize)
        {
            _outline.Visibility = Visibility.Collapsed;
            UpdateDim(null);
            return;
        }

        _finish(new RegionResult(rect, _monitor.Number));
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        e.Handled = true;
        _dragging = false;
        ReleaseMouseCapture();
        _finish(null);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _dragging = false;
            _finish(null);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Win32.PlaceTopmost(hwnd, _monitor.Bounds);
        Win32.ExcludeFromCapture(hwnd);
    }

    private void Reposition()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            Win32.PlaceTopmost(hwnd, _monitor.Bounds);
        }
        UpdateDim(null);
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        double x = Math.Min(_startDip.X, current.X);
        double y = Math.Min(_startDip.Y, current.Y);
        double w = Math.Abs(current.X - _startDip.X);
        double h = Math.Abs(current.Y - _startDip.Y);
        var rect = new Rect(x, y, w, h);

        Canvas.SetLeft(_outline, x);
        Canvas.SetTop(_outline, y);
        _outline.Width = w;
        _outline.Height = h;
        _outline.Visibility = Visibility.Visible;
        UpdateDim(rect);
    }

    private void UpdateDim(Rect? hole)
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        double sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
        double width = _monitor.Bounds.Width / sx;
        double height = _monitor.Bounds.Height / sy;

        Geometry full = new RectangleGeometry(new Rect(0, 0, width, height));
        if (hole is Rect r)
        {
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(r));
        }
        else
        {
            _dim.Data = full;
        }
    }
}
