using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace MyScreenRecorder.Recording;

public sealed class GdiGrab : ScreenGrab
{
    public override IReadOnlyList<string> InputArgs(Rectangle bounds, int framerate) => new[]
    {
        "-f", "gdigrab",
        "-framerate", framerate.ToString(CultureInfo.InvariantCulture),
        "-draw_mouse", "1",
        "-offset_x", bounds.X.ToString(CultureInfo.InvariantCulture),
        "-offset_y", bounds.Y.ToString(CultureInfo.InvariantCulture),
        "-video_size", bounds.Width.ToString(CultureInfo.InvariantCulture) + "x" + bounds.Height.ToString(CultureInfo.InvariantCulture),
        "-thread_queue_size", "1024",
        "-i", "desktop"
    };

    public override IReadOnlyList<string> EncoderArgs() => System.Array.Empty<string>();

    public override void ApplyEnvironment(IDictionary<string, string?> environment)
    {
        environment["__COMPAT_LAYER"] = "HighDpiAware";
    }
}
