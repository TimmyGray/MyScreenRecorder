using System;
using System.IO;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private const string TodayFormat = """
        {
          "TargetKind": "All",
          "MonitorNumber": 2,
          "MicId": "{0.0.1.00000000}.{6b0c}",
          "SpeakerId": null,
          "VideoQuality": "P1080",
          "AudioQuality": "Wav"
        }
        """;

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("srec-settings-");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Load_ReadsEveryFieldOfTodaysFormat()
    {
        string path = Path.Combine(_dir.FullName, "settings.json");
        File.WriteAllText(path, TodayFormat);

        Settings settings = Settings.Load(path);

        Assert.Equal("All", settings.TargetKind);
        Assert.Equal(2, settings.MonitorNumber);
        Assert.Equal("{0.0.1.00000000}.{6b0c}", settings.MicId);
        Assert.Null(settings.SpeakerId);
        Assert.Equal(VideoQuality.P1080, settings.VideoQuality);
        Assert.Equal(AudioQuality.Wav, settings.AudioQuality);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        AssertDefaults(Settings.Load(Path.Combine(_dir.FullName, "missing.json")));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        string path = Path.Combine(_dir.FullName, "settings.json");
        File.WriteAllText(path, "{ not json");

        AssertDefaults(Settings.Load(path));
    }

    [Fact]
    public void Save_IntoMissingDirectory_CreatesItAndWritesEnumsAsStrings()
    {
        string path = Path.Combine(_dir.FullName, "MyScreenRecorder", "settings.json");

        new Settings().Save(path);

        string json = File.ReadAllText(path);
        Assert.Contains("\"VideoQuality\": \"P720\"", json);
        Assert.Contains("\"AudioQuality\": \"Aac256\"", json);
    }

    [Fact]
    public void Save_Failure_IsSwallowed()
    {
        string notADirectory = Path.Combine(_dir.FullName, "file");
        File.WriteAllText(notADirectory, "");

        Exception? error = Record.Exception(() => new Settings().Save(Path.Combine(notADirectory, "settings.json")));

        Assert.Null(error);
    }

    private static void AssertDefaults(Settings settings)
    {
        Assert.Equal("Monitor", settings.TargetKind);
        Assert.Equal(1, settings.MonitorNumber);
        Assert.Null(settings.MicId);
        Assert.Null(settings.SpeakerId);
        Assert.Equal(VideoQuality.P720, settings.VideoQuality);
        Assert.Equal(AudioQuality.Aac256, settings.AudioQuality);
    }
}
