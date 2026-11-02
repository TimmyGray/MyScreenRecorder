using System.Reflection;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public class QualityTests
{
    [Theory]
    [InlineData(VideoQuality.P480, "scale=-2:'min(480,ih)':flags=bicubic,format=yuv420p")]
    [InlineData(VideoQuality.P720, "scale=-2:'min(720,ih)':flags=bicubic,format=yuv420p")]
    [InlineData(VideoQuality.P1080, "scale=-2:'min(1080,ih)':flags=bicubic,format=yuv420p")]
    public void VideoArgList_IsTheReleasedX264Contract(VideoQuality quality, string filter)
    {
        string[] expected =
        {
            "-vf", filter,
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "28",
            "-g", "30"
        };

        Assert.Equal(expected, Quality.VideoArgList(quality));
    }

    [Theory]
    [InlineData(AudioQuality.Aac128, new[] { "-c:a", "aac", "-b:a", "128k" })]
    [InlineData(AudioQuality.Aac256, new[] { "-c:a", "aac", "-b:a", "256k" })]
    [InlineData(AudioQuality.Wav, new[] { "-c:a", "pcm_s16le" })]
    public void AudioArgList_IsTheReleasedEncoderContract(AudioQuality quality, string[] expected)
    {
        Assert.Equal(expected, Quality.AudioArgList(quality));
    }

    [Theory]
    [InlineData(AudioQuality.Wav, true, "mkv")]
    [InlineData(AudioQuality.Wav, false, "mp4")]
    [InlineData(AudioQuality.Aac128, true, "mp4")]
    [InlineData(AudioQuality.Aac256, true, "mp4")]
    public void Extension_IsMkvOnlyForWavWithAudio(AudioQuality quality, bool hasAudio, string expected)
    {
        Assert.Equal(expected, Quality.Extension(quality, hasAudio));
    }

    [Theory]
    [InlineData(VideoQuality.P480, "480P")]
    [InlineData(VideoQuality.P720, "720P")]
    [InlineData(VideoQuality.P1080, "1080P")]
    public void Label_Video(VideoQuality quality, string expected)
    {
        Assert.Equal(expected, Quality.Label(quality));
    }

    [Theory]
    [InlineData(AudioQuality.Aac128, "AAC 128K")]
    [InlineData(AudioQuality.Aac256, "AAC 256K")]
    [InlineData(AudioQuality.Wav, "WAV")]
    public void Label_Audio(AudioQuality quality, string expected)
    {
        Assert.Equal(expected, Quality.Label(quality));
    }

    [Theory]
    [InlineData("AllVideo")]
    [InlineData("AllAudio")]
    [InlineData("VideoArgs")]
    [InlineData("AudioArgs")]
    public void RemovedMembers_DoNotExist(string name)
    {
        Assert.Empty(typeof(Quality).GetMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
    }
}
