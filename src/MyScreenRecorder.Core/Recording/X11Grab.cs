using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MyScreenRecorder.Recording;

public sealed class X11Grab : ScreenGrab
{
    private readonly string _display;

    public X11Grab(string display)
    {
        if (string.IsNullOrEmpty(display))
        {
            throw new ArgumentException("The X11 display must not be empty.", nameof(display));
        }

        _display = display;
    }

    public override IReadOnlyList<string> InputArgs(Rectangle bounds, int framerate) => new[]
    {
        "-f", "x11grab",
        "-framerate", framerate.ToString(CultureInfo.InvariantCulture),
        "-draw_mouse", "1",
        "-video_size", bounds.Width.ToString(CultureInfo.InvariantCulture) + "x" + bounds.Height.ToString(CultureInfo.InvariantCulture),
        "-thread_queue_size", "1024",
        "-i", _display + "+" + bounds.X.ToString(CultureInfo.InvariantCulture) + "," + bounds.Y.ToString(CultureInfo.InvariantCulture)
    };

    public override IReadOnlyList<string> EncoderArgs() => new[] { "-x264-params", "sliced-threads=1" };

    public override void ApplyEnvironment(IDictionary<string, string?> environment)
    {
    }
}
