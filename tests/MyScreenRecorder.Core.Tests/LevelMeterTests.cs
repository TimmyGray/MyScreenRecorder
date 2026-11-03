using MyScreenRecorder.Controller;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class LevelMeterTests
{
    [Fact]
    public void M1_ALouderPeakWins()
    {
        Assert.Equal(0.5f, LevelMeter.Next(0.5f, 0.2f));
    }

    [Fact]
    public void M2_TheLevelDecaysByEightyFivePercent()
    {
        Assert.Equal(0.85f, LevelMeter.Next(0f, 1f), 1e-6f);
    }

    [Fact]
    public void M3_TheDecayCompoundsPerStep()
    {
        Assert.Equal(0.85f, LevelMeter.Next(0.1f, 1f), 1e-6f);

        float level = 1f;
        for (int i = 0; i < 20; i++) level = LevelMeter.Next(0f, level);

        Assert.Equal(0.03876f, level, 1e-5f);
    }
}
