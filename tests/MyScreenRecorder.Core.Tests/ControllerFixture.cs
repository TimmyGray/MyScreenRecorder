using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Controller;
using MyScreenRecorder.Platform;
using MyScreenRecorder.Recording;

namespace MyScreenRecorder.Core.Tests;

public sealed record SavedSettings(string TargetKind, int MonitorNumber, string? MicId, string? SpeakerId, VideoQuality VideoQuality, AudioQuality AudioQuality);

public sealed class ControllerFixture : IDisposable
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly List<string> _tempDirs = new();
    private readonly FakeRecordingSession? _fake;
    private readonly bool _ownsAudio;

    public ControllerFixture(
        Settings? settings = null,
        bool firstRun = false,
        bool ffmpegFound = true,
        bool queueing = false,
        FakeScreenCatalog? screens = null,
        FakeAudioDeviceCatalog? devices = null,
        IRecordingSession? session = null,
        AudioEngine? audio = null,
        FakeAudioCaptureFactory? captures = null,
        FfmpegLocator? locator = null)
    {
        Settings = settings ?? new Settings();
        Screens = screens ?? FakeScreenCatalog.Standard();
        Devices = devices ?? new FakeAudioDeviceCatalog();
        Captures = captures ?? new FakeAudioCaptureFactory();
        _ownsAudio = audio == null;
        Audio = audio ?? new AudioEngine(Captures);
        if (session == null)
        {
            _fake = new FakeRecordingSession(Log);
            session = _fake;
        }

        Locator = locator ?? NewLocator(ffmpegFound);
        Action<Action> marshal = queueing ? _queue.Enqueue : a => a();
        Controller = new RecorderController(session, Audio, Devices, Screens, Locator, Settings, firstRun, SaveSettings, marshal);
        Controller.Changed += () => ChangedCount++;
    }

    public Settings Settings { get; }

    public FakeScreenCatalog Screens { get; }

    public FakeAudioDeviceCatalog Devices { get; }

    public FakeAudioCaptureFactory Captures { get; }

    public AudioEngine Audio { get; }

    public FfmpegLocator Locator { get; }

    public RecorderController Controller { get; }

    public FakeRecordingSession Session => _fake ?? throw new InvalidOperationException("The fixture runs over a real session.");

    public List<string> Log { get; } = new();

    public List<SavedSettings> Saves { get; } = new();

    public List<int> CaptureCallsAtSave { get; } = new();

    public int ChangedCount { get; private set; }

    public static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetState(RecordingState state)
    {
        Session.State = state;
        Session.RaiseStateChanged();
    }

    public string NewTempDir()
    {
        string path = Directory.CreateTempSubdirectory("srec-ctl-").FullName;
        _tempDirs.Add(path);
        return path;
    }

    public string NewFile(string name)
    {
        string path = Path.Combine(NewTempDir(), name);
        File.WriteAllText(path, "x");
        return path;
    }

    public int Drain()
    {
        int count = 0;
        while (_queue.TryDequeue(out Action? action))
        {
            action();
            count++;
        }

        return count;
    }

    public void Dispose()
    {
        Controller.Dispose();
        if (_ownsAudio) Audio.Dispose();
        foreach (string dir in _tempDirs)
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }

    private FfmpegLocator NewLocator(bool found)
    {
        string baseDir = NewTempDir();
        if (found) File.WriteAllText(Path.Combine(baseDir, "ffmpeg"), string.Empty);
        var options = new FfmpegLocatorOptions("ffmpeg", null, null, NewTempDir());
        return new FfmpegLocator(options, baseDir, _ => null);
    }

    private void SaveSettings()
    {
        CaptureCallsAtSave.Add(Captures.Calls.Count);
        Saves.Add(new SavedSettings(Settings.TargetKind, Settings.MonitorNumber, Settings.MicId, Settings.SpeakerId, Settings.VideoQuality, Settings.AudioQuality));
    }
}
