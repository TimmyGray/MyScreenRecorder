using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyScreenRecorder.Recording;

namespace MyScreenRecorder;

public sealed class Settings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string TargetKind { get; set; } = "Monitor";

    public int MonitorNumber { get; set; } = 1;

    public string? MicId { get; set; }

    public string? SpeakerId { get; set; }

    public VideoQuality VideoQuality { get; set; } = VideoQuality.P720;

    public AudioQuality AudioQuality { get; set; } = AudioQuality.Aac256;

    public static Settings Load() => Load(AppPaths.SettingsFile);

    public void Save() => Save(AppPaths.SettingsFile);

    internal static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Settings();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    internal void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
        }
    }
}
