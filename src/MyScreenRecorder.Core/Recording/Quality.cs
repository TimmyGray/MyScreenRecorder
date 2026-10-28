using System.Collections.Generic;

namespace MyScreenRecorder.Recording;

public enum VideoQuality
{
    P480,
    P720,
    P1080
}

public enum AudioQuality
{
    Aac128,
    Aac256,
    Wav
}

public static class Quality
{
    public static string Label(VideoQuality vq) => vq switch
    {
        VideoQuality.P480 => "480P",
        VideoQuality.P720 => "720P",
        _ => "1080P"
    };

    public static string Label(AudioQuality aq) => aq switch
    {
        AudioQuality.Aac128 => "AAC 128K",
        AudioQuality.Aac256 => "AAC 256K",
        _ => "WAV"
    };

    public static int Height(VideoQuality vq) => vq switch
    {
        VideoQuality.P480 => 480,
        VideoQuality.P720 => 720,
        _ => 1080
    };

    public static IEnumerable<string> VideoArgList(VideoQuality vq)
    {
        int h = Height(vq);
        return new[]
        {
            "-vf", $"scale=-2:'min({h},ih)':flags=bicubic,format=yuv420p",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "28",
            "-g", "30"
        };
    }

    public static IEnumerable<string> AudioArgList(AudioQuality aq) => aq switch
    {
        AudioQuality.Aac128 => new[] { "-c:a", "aac", "-b:a", "128k" },
        AudioQuality.Aac256 => new[] { "-c:a", "aac", "-b:a", "256k" },
        _ => new[] { "-c:a", "pcm_s16le" }
    };

    public static string Extension(AudioQuality aq, bool hasAudio) =>
        aq == AudioQuality.Wav && hasAudio ? "mkv" : "mp4";
}
