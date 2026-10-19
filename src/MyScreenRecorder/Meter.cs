using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MyScreenRecorder;

public sealed class Meter : Border
{
    private const int Steps = 16;
    private readonly ScaleTransform _scale = new(0, 1);

    public Meter()
    {
        BorderBrush = (Brush)Application.Current.Resources["FgBrush"];
        BorderThickness = new Thickness(2);
        Height = 14;
        SnapsToDevicePixels = true;
        Child = new Rectangle
        {
            Fill = (Brush)Application.Current.Resources["GreenBrush"],
            RenderTransform = _scale,
            RenderTransformOrigin = new Point(0, 0.5),
            SnapsToDevicePixels = true
        };
    }

    public void SetLevel(double level)
    {
        double clamped = Math.Clamp(level, 0, 1);
        _scale.ScaleX = Math.Ceiling(clamped * Steps) / Steps;
    }
}
