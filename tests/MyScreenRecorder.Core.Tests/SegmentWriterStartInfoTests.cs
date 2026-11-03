using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public sealed class SegmentWriterStartInfoTests
{
    private const string SessionId = "0123456789abcdef0123456789abcdef";
    private const string WindowsPipe = @"\\.\pipe\srec_0123456789abcdef0123456789abcdef_0";
    private const string PartsDir = "/tmp/parts/2026-11-03_10-00-00";

    private static SegmentWriter Create(
        ScreenGrab grab,
        Rectangle bounds,
        int index = 0,
        VideoQuality vq = VideoQuality.P720,
        AudioQuality aq = AudioQuality.Aac256,
        string ffmpeg = @"C:\ff\ffmpeg.exe")
    {
        var target = new CaptureTarget(TargetKind.Region, bounds, "x", new[] { 1 });
        return new SegmentWriter(ffmpeg, PartsDir, index, target, vq, aq, new AudioEngine(new FakeAudioCaptureFactory()), SessionId, new FakeChildProcessLauncher(), new FakeAudioPipeFactory(), grab);
    }

    private static string Seg(int index) => Path.Combine(PartsDir, $"seg_{index:D3}.mkv");

    private static string[] WindowsHead(string? audioInput)
    {
        var list = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        if (audioInput != null)
        {
            list.AddRange(new[] { "-f", "f32le", "-ar", "48000", "-ac", "2", "-thread_queue_size", "1024", "-i", audioInput });
        }

        return list.ToArray();
    }

    [Fact]
    public void Windows_WithAudio_IsTheReleasedList()
    {
        SegmentWriter writer = Create(new GdiGrab(), new Rectangle(-1920, 116, 1921, 1081));

        ProcessStartInfo psi = writer.BuildStartInfo(WindowsPipe);

        string[] expected = WindowsHead(WindowsPipe).Concat(new[]
        {
            "-f", "gdigrab", "-framerate", "15", "-draw_mouse", "1", "-offset_x", "-1920", "-offset_y", "116",
            "-video_size", "1920x1080", "-thread_queue_size", "1024", "-i", "desktop",
            "-map", "1:v", "-map", "0:a",
            "-vf", "scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-g", "30",
            "-c:a", "aac", "-b:a", "256k",
            "-progress", "pipe:1", "-nostats", Seg(0)
        }).ToArray();
        Assert.Equal(expected, psi.ArgumentList);
    }

    [Fact]
    public void Windows_WithoutAudio_IsTheReleasedList()
    {
        SegmentWriter writer = Create(new GdiGrab(), new Rectangle(-1920, 116, 1921, 1081));

        ProcessStartInfo psi = writer.BuildStartInfo(null);

        string[] expected = WindowsHead(null).Concat(new[]
        {
            "-f", "gdigrab", "-framerate", "15", "-draw_mouse", "1", "-offset_x", "-1920", "-offset_y", "116",
            "-video_size", "1920x1080", "-thread_queue_size", "1024", "-i", "desktop",
            "-map", "0:v",
            "-vf", "scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-g", "30",
            "-progress", "pipe:1", "-nostats", Seg(0)
        }).ToArray();
        Assert.Equal(expected, psi.ArgumentList);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Windows_FlagsAndEnvironment(bool withAudio)
    {
        SegmentWriter writer = Create(new GdiGrab(), new Rectangle(0, 0, 1920, 1080));

        ProcessStartInfo psi = writer.BuildStartInfo(withAudio ? WindowsPipe : null);

        AssertCommonFlags(psi, @"C:\ff\ffmpeg.exe");
        var expectedEnvironment = new Dictionary<string, string?>(new ProcessStartInfo().Environment) { ["__COMPAT_LAYER"] = "HighDpiAware" };
        Assert.Equal(expectedEnvironment.OrderBy(p => p.Key), psi.Environment.OrderBy(p => p.Key));
    }

    [Fact]
    public void X11_WithAudio_IsTheExpectedList()
    {
        SegmentWriter writer = Create(new X11Grab(":0"), new Rectangle(1920, 0, 2560, 1440), ffmpeg: "/opt/ffmpeg");
        const string audio = "unix:/run/user/1000/srec_0123456789abcdef0123456789abcdef_0";

        ProcessStartInfo psi = writer.BuildStartInfo(audio);

        string[] expected = WindowsHead(audio).Concat(new[]
        {
            "-f", "x11grab", "-framerate", "15", "-draw_mouse", "1", "-video_size", "2560x1440",
            "-thread_queue_size", "1024", "-i", ":0+1920,0",
            "-map", "1:v", "-map", "0:a",
            "-vf", "scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-g", "30",
            "-x264-params", "sliced-threads=1",
            "-c:a", "aac", "-b:a", "256k",
            "-progress", "pipe:1", "-nostats", Seg(0)
        }).ToArray();
        Assert.Equal(expected, psi.ArgumentList);
        AssertCommonFlags(psi, "/opt/ffmpeg");
        Assert.Equal(new ProcessStartInfo().Environment.OrderBy(p => p.Key), psi.Environment.OrderBy(p => p.Key));
    }

    [Fact]
    public void X11_WithoutAudio_HasNoAudioArgsButKeepsEncoderArgs()
    {
        SegmentWriter writer = Create(new X11Grab(":0"), new Rectangle(1920, 0, 2560, 1440), ffmpeg: "/opt/ffmpeg");

        ProcessStartInfo psi = writer.BuildStartInfo(null);

        string[] expected = WindowsHead(null).Concat(new[]
        {
            "-f", "x11grab", "-framerate", "15", "-draw_mouse", "1", "-video_size", "2560x1440",
            "-thread_queue_size", "1024", "-i", ":0+1920,0",
            "-map", "0:v",
            "-vf", "scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p",
            "-c:v", "libx264", "-preset", "veryfast", "-crf", "28", "-g", "30",
            "-x264-params", "sliced-threads=1",
            "-progress", "pipe:1", "-nostats", Seg(0)
        }).ToArray();
        Assert.Equal(expected, psi.ArgumentList);
    }

    [Fact]
    public void OddBounds_AreRoundedDownToEven()
    {
        ProcessStartInfo psi = Create(new GdiGrab(), new Rectangle(0, 0, 1921, 1081)).BuildStartInfo(null);

        Assert.Equal("1920x1080", psi.ArgumentList[psi.ArgumentList.IndexOf("-video_size") + 1]);
    }

    [Fact]
    public void TinyBounds_AreRaisedToTwoByTwo()
    {
        ProcessStartInfo psi = Create(new GdiGrab(), new Rectangle(0, 0, 1, 1)).BuildStartInfo(null);

        Assert.Equal("2x2", psi.ArgumentList[psi.ArgumentList.IndexOf("-video_size") + 1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void FilePath_UsesThreeDigitIndex(int index)
    {
        SegmentWriter writer = Create(new GdiGrab(), new Rectangle(0, 0, 1920, 1080), index);

        Assert.Equal(Path.Combine(PartsDir, $"seg_{index:000}.mkv"), writer.FilePath);
        Assert.Equal(writer.FilePath, writer.BuildStartInfo(null).ArgumentList.Last());
    }

    [Fact]
    public void WithoutAudio_MapsVideoFromInputZeroOnly()
    {
        IList<string> args = Create(new GdiGrab(), new Rectangle(0, 0, 1920, 1080)).BuildStartInfo(null).ArgumentList;

        Assert.Contains("0:v", args);
        Assert.DoesNotContain("0:a", args);
        Assert.DoesNotContain("-c:a", args);
    }

    [Fact]
    public void AudioQualities_ProduceTheirCodecArgs()
    {
        IList<string> wav = Create(new GdiGrab(), new Rectangle(0, 0, 1920, 1080), aq: AudioQuality.Wav).BuildStartInfo(WindowsPipe).ArgumentList;
        IList<string> aac = Create(new GdiGrab(), new Rectangle(0, 0, 1920, 1080), aq: AudioQuality.Aac128).BuildStartInfo(WindowsPipe).ArgumentList;

        Assert.Equal("pcm_s16le", wav[wav.IndexOf("-c:a") + 1]);
        Assert.Equal("128k", aac[aac.IndexOf("-b:a") + 1]);
    }

    [Theory]
    [InlineData(VideoQuality.P480, "scale=-2:'min(480,ih)':flags=bicubic,format=yuv420p")]
    [InlineData(VideoQuality.P1080, "scale=-2:'min(1080,ih)':flags=bicubic,format=yuv420p")]
    public void VideoQualities_ScaleToMinOfTargetAndSourceHeight(VideoQuality vq, string filter)
    {
        IList<string> args = Create(new GdiGrab(), new Rectangle(0, 0, 1920, 1080), vq: vq).BuildStartInfo(null).ArgumentList;

        Assert.Equal(filter, args[args.IndexOf("-vf") + 1]);
    }

    private static void AssertCommonFlags(ProcessStartInfo psi, string fileName)
    {
        Assert.Equal(fileName, psi.FileName);
        Assert.Equal(PartsDir, psi.WorkingDirectory);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.CreateNoWindow);
        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        UTF8Encoding encoding = Assert.IsType<UTF8Encoding>(psi.StandardInputEncoding);
        Assert.Empty(encoding.GetPreamble());
    }
}
