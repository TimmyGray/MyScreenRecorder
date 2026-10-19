using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MyScreenRecorder.Native;

namespace MyScreenRecorder.Overlay;

public sealed class OverlayPiece : Window
{
    private System.Drawing.Rectangle _bounds;

    public OverlayPiece(int? number = null)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
        Background = System.Windows.Media.Brushes.Black;

        if (number.HasValue)
        {
            Content = BuildBadge(number.Value);
        }

        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            Win32.MakeClickThrough(hwnd);
            Win32.SetOpaqueLayered(hwnd);
            Win32.ExcludeFromCapture(hwnd);
            Reposition();
        };
        Loaded += (_, _) => Reposition();
    }

    public void Place(System.Drawing.Rectangle bounds)
    {
        _bounds = bounds;
        Reposition();
    }

    public void SetFill(System.Windows.Media.Brush brush)
    {
        Background = brush;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    private void Reposition()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && _bounds.Width > 0 && _bounds.Height > 0)
        {
            Win32.PlaceTopmost(hwnd, _bounds, IsVisible);
        }
    }

    private static Border BuildBadge(int number)
    {
        var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x66));
        accent.Freeze();

        var text = new TextBlock
        {
            Text = number.ToString(),
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (System.Windows.Application.Current?.TryFindResource("PixelFont") is FontFamily pixelFont)
        {
            text.FontFamily = pixelFont;
        }

        return new Border
        {
            Background = System.Windows.Media.Brushes.Black,
            BorderBrush = accent,
            BorderThickness = new Thickness(4),
            Padding = new Thickness(8),
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = text }
        };
    }
}
