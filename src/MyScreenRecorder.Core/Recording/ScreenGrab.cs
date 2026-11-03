using System.Collections.Generic;
using System.Drawing;

namespace MyScreenRecorder.Recording;

public abstract class ScreenGrab
{
    public abstract IReadOnlyList<string> InputArgs(Rectangle bounds, int framerate);

    public abstract IReadOnlyList<string> EncoderArgs();

    public abstract void ApplyEnvironment(IDictionary<string, string?> environment);
}
