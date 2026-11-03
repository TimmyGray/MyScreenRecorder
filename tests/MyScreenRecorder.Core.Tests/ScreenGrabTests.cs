using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public sealed class ScreenGrabTests
{
    [Fact]
    public void GdiGrab_InputArgs_AreTheReleasedWindowsList()
    {
        string[] expected =
        {
            "-f", "gdigrab", "-framerate", "15", "-draw_mouse", "1", "-offset_x", "0", "-offset_y", "0",
            "-video_size", "1920x1080", "-thread_queue_size", "1024", "-i", "desktop"
        };

        Assert.Equal(expected, new GdiGrab().InputArgs(new Rectangle(0, 0, 1920, 1080), 15));
        Assert.Empty(new GdiGrab().EncoderArgs());
    }

    [Fact]
    public void GdiGrab_ApplyEnvironment_SetsHighDpiAwareOnly()
    {
        var environment = new Dictionary<string, string?> { ["A"] = "1" };

        new GdiGrab().ApplyEnvironment(environment);

        Assert.Equal(2, environment.Count);
        Assert.Equal("HighDpiAware", environment["__COMPAT_LAYER"]);
        Assert.Equal("1", environment["A"]);
    }

    [Fact]
    public void X11Grab_InputArgs_UseDisplayPlusOffset()
    {
        string[] expected =
        {
            "-f", "x11grab", "-framerate", "15", "-draw_mouse", "1", "-video_size", "2560x1440",
            "-thread_queue_size", "1024", "-i", ":0+1920,0"
        };

        Assert.Equal(expected, new X11Grab(":0").InputArgs(new Rectangle(1920, 0, 2560, 1440), 15));
    }

    [Fact]
    public void X11Grab_EncoderArgs_AreSlicedThreads()
    {
        Assert.Equal(new[] { "-x264-params", "sliced-threads=1" }, new X11Grab(":0").EncoderArgs());
    }

    [Fact]
    public void X11Grab_ApplyEnvironment_ChangesNothing()
    {
        var environment = new Dictionary<string, string?> { ["A"] = "1" };

        new X11Grab(":0").ApplyEnvironment(environment);

        Assert.Equal(new KeyValuePair<string, string?>("A", "1"), Assert.Single(environment));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void X11Grab_EmptyDisplay_Throws(string? display)
    {
        Assert.Throws<ArgumentException>(() => new X11Grab(display!));
    }

    [Fact]
    public void Grabs_FormatNumbersWithInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
        try
        {
            Assert.NotEqual("-", CultureInfo.CurrentCulture.NumberFormat.NegativeSign);
            var bounds = new Rectangle(-1920, 1116, 1920, 1080);

            string[] gdi = new GdiGrab().InputArgs(bounds, 15).ToArray();
            string[] x11 = new X11Grab(":0").InputArgs(bounds, 15).ToArray();

            Assert.Contains("-1920", gdi);
            Assert.Contains("1920x1080", gdi);
            Assert.Contains(":0+-1920,1116", x11);
            Assert.Contains("1920x1080", x11);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
