using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Native;

namespace MyScreenRecorder.Overlay;

public sealed class MonitorOverlayWindow : Window
{
    private const double BorderThicknessDip = 6;

    private readonly MonitorInfo _monitor;
    private readonly Canvas _canvas;
    private readonly Border _border;
    private readonly Border _badge;

    private bool _showBadge;
    private bool _showBorder;
    private System.Drawing.Rectangle? _region;

    public MonitorOverlayWindow(MonitorInfo monitor)
    {
        _monitor = monitor;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(1, 1, 1));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Bounds.X;
        Top = monitor.Bounds.Y;
        Width = monitor.Bounds.Width;
        Height = monitor.Bounds.Height;

        _border = new Border
        {
            BorderThickness = new Thickness(BorderThicknessDip),
            Background = null,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x66));
        accent.Freeze();

        var number = new TextBlock
        {
            Text = monitor.Number.ToString(),
            FontSize = 48,
            Foreground = System.Windows.Media.Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        if (System.Windows.Application.Current?.TryFindResource("PixelFont") is FontFamily pixelFont)
        {
            number.FontFamily = pixelFont;
        }

        _badge = new Border
        {
            Background = System.Windows.Media.Brushes.Black,
            BorderBrush = accent,
            BorderThickness = new Thickness(4),
            Padding = new Thickness(18, 14, 12, 14),
            Child = number,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _canvas = new Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Background = null
        };
        _canvas.Children.Add(_border);
        _canvas.Children.Add(_badge);
        Canvas.SetLeft(_badge, 14);
        Canvas.SetTop(_badge, 14);

        Content = _canvas;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Relayout();
    }

    public void Apply(bool showBadge, bool showBorder, System.Windows.Media.Brush borderBrush, System.Drawing.Rectangle? regionPhysicalPx)
    {
        _showBadge = showBadge;
        _showBorder = showBorder;
        _region = regionPhysicalPx;
        _border.BorderBrush = borderBrush;
        Relayout();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        Win32.MakeClickThrough(hwnd);
        Win32.SetColorKey(hwnd, 1, 1, 1);
        Win32.PlaceTopmost(hwnd, _monitor.Bounds);
        Win32.ExcludeFromCapture(hwnd);
        Relayout();
    }

    private void Reposition()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            Win32.PlaceTopmost(hwnd, _monitor.Bounds);
        }
        Relayout();
    }

    private void Relayout()
    {
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
        double sy = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;

        double width = _monitor.Bounds.Width / sx;
        double height = _monitor.Bounds.Height / sy;
        _canvas.Width = width;
        _canvas.Height = height;

        _badge.Visibility = _showBadge ? Visibility.Visible : Visibility.Collapsed;

        if (!_showBorder)
        {
            _border.Visibility = Visibility.Collapsed;
            return;
        }

        if (_region is System.Drawing.Rectangle region)
        {
            double x = (region.X - _monitor.Bounds.X) / sx - BorderThicknessDip;
            double y = (region.Y - _monitor.Bounds.Y) / sy - BorderThicknessDip;
            double w = region.Width / sx + BorderThicknessDip * 2;
            double h = region.Height / sy + BorderThicknessDip * 2;
            Canvas.SetLeft(_border, x);
            Canvas.SetTop(_border, y);
            _border.Width = Math.Max(0, w);
            _border.Height = Math.Max(0, h);
        }
        else
        {
            Canvas.SetLeft(_border, 0);
            Canvas.SetTop(_border, 0);
            _border.Width = width;
            _border.Height = height;
        }

        _border.Visibility = Visibility.Visible;
    }
}
