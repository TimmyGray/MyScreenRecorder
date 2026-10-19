using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MyScreenRecorder;

public static class Tips
{
    public static void ForTrimmed(TextBlock block)
    {
        block.TextTrimming = TextTrimming.CharacterEllipsis;
        block.ToolTipOpening += (_, e) =>
        {
            if (string.IsNullOrEmpty(block.Text) || !IsTrimmed(block))
            {
                e.Handled = true;
            }
        };
    }

    public static void Set(TextBlock block, string text)
    {
        block.Text = text;
        block.ToolTip = text;
    }

    private static bool IsTrimmed(TextBlock block)
    {
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        var formatted = new FormattedText(
            block.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace > block.ActualWidth + 0.5;
    }
}
