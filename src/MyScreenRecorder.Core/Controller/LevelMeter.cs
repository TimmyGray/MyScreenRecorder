using System;

namespace MyScreenRecorder.Controller;

internal static class LevelMeter
{
    internal static float Next(float peak, float previous) => Math.Max(peak, previous * 0.85f);
}
