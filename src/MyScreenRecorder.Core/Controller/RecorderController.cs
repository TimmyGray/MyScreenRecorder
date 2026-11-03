using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Platform;
using MyScreenRecorder.Recording;

namespace MyScreenRecorder.Controller;

public sealed class RecorderController : IDisposable
{
    public const int MeterIntervalMs = 50;
    public const int StatusIntervalMs = 500;
    public const string NoneLabel = "NONE";

    private static readonly VideoQuality[] VideoQualities = Enum.GetValues<VideoQuality>();
    private static readonly AudioQuality[] AudioQualities = Enum.GetValues<AudioQuality>();

    private readonly IRecordingSession _session;
    private readonly AudioEngine _audio;
    private readonly FfmpegLocator _ffmpeg;
    private readonly Settings _settings;
    private readonly bool _firstRun;
    private readonly Action _saveSettings;
    private readonly Action<Action> _marshal;
    private readonly List<MonitorInfo> _monitors;
    private readonly List<AudioDeviceInfo> _mics;
    private readonly List<AudioDeviceInfo> _speakers;
    private readonly Action _onStateChanged;
    private readonly Action<string> _onFaulted;

    private List<CaptureTarget> _targets = new();
    private ReadOnlyCollection<CaptureTarget> _targetsView = new List<CaptureTarget>().AsReadOnly();
    private IReadOnlyList<string> _sourceLabels = new List<string>();
    private CaptureTarget? _region;
    private string? _lastFinal;
    private string? _message;
    private bool _busy;
    private bool _confirming;
    private bool _starting;
    private bool _disposed;

    public RecorderController(
        IRecordingSession session,
        AudioEngine audio,
        IAudioDeviceCatalog devices,
        IScreenCatalog screens,
        FfmpegLocator ffmpeg,
        Settings settings,
        bool firstRun,
        Action saveSettings,
        Action<Action> marshal)
    {
        _session = session;
        _audio = audio;
        _ffmpeg = ffmpeg;
        _settings = settings;
        _firstRun = firstRun;
        _saveSettings = saveSettings;
        _marshal = marshal;

        _monitors = screens.GetMonitors().ToList();
        if (_monitors.Count == 0)
        {
            throw new InvalidOperationException("No monitors found.");
        }

        RebuildTargets();
        SourceIndex = InitialTargetIndex();

        VideoQualityLabels = VideoQualities.Select(Quality.Label).ToList();
        AudioQualityLabels = AudioQualities.Select(Quality.Label).ToList();
        VideoQualityIndex = Math.Clamp(Array.IndexOf(VideoQualities, settings.VideoQuality), 0, VideoQualities.Length - 1);
        AudioQualityIndex = Math.Clamp(Array.IndexOf(AudioQualities, settings.AudioQuality), 0, AudioQualities.Length - 1);

        _mics = devices.Inputs().ToList();
        _speakers = devices.Outputs().ToList();
        MicNames = DeviceNames(_mics);
        SpeakerNames = DeviceNames(_speakers);
        MicIndex = InitialDeviceIndex(_mics, settings.MicId, devices.DefaultInputId());
        SpeakerIndex = InitialDeviceIndex(_speakers, settings.SpeakerId, devices.DefaultOutputId());
        ApplyMic();
        ApplySpeaker();

        if (_ffmpeg.Resolve() == null)
        {
            _message = "ERROR: ffmpeg not found";
        }

        Controls = ComputeControls();
        Status = ComputeStatus();
        Overlay = ComputeOverlay();

        _onStateChanged = () => _marshal(() =>
        {
            if (_disposed) return;
            Refresh();
        });
        _onFaulted = message => _marshal(() =>
        {
            if (_disposed) return;
            _message = message;
            Refresh();
        });
        _session.StateChanged += _onStateChanged;
        _session.Faulted += _onFaulted;
    }

    public event Action? Changed;

    public IReadOnlyList<CaptureTarget> Targets => _targetsView;

    public IReadOnlyList<string> SourceLabels => _sourceLabels;

    public int SourceIndex { get; private set; }

    public CaptureTarget Target => _targets[SourceIndex];

    public IReadOnlyList<string> VideoQualityLabels { get; }

    public int VideoQualityIndex { get; private set; }

    public IReadOnlyList<string> AudioQualityLabels { get; }

    public int AudioQualityIndex { get; private set; }

    public IReadOnlyList<string> MicNames { get; }

    public int MicIndex { get; private set; }

    public IReadOnlyList<string> SpeakerNames { get; }

    public int SpeakerIndex { get; private set; }

    public ControlState Controls { get; private set; }

    public StatusLine Status { get; private set; }

    public OverlayMode Overlay { get; private set; }

    public float MicLevel { get; private set; }

    public float SpeakerLevel { get; private set; }

    public string? LastFile => _lastFinal != null && File.Exists(_lastFinal) ? _lastFinal : null;

    public void SelectSource(int index)
    {
        if (index < 0 || index >= _targets.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!CanSelect()) return;
        if (index == SourceIndex) return;
        SourceIndex = index;
        SaveSettings();
        Refresh();
    }

    public void SelectVideoQuality(int index)
    {
        if (index < 0 || index >= VideoQualities.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!CanSelect()) return;
        if (index == VideoQualityIndex) return;
        VideoQualityIndex = index;
        SaveSettings();
        Refresh();
    }

    public void SelectAudioQuality(int index)
    {
        if (index < 0 || index >= AudioQualities.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!CanSelect()) return;
        if (index == AudioQualityIndex) return;
        AudioQualityIndex = index;
        SaveSettings();
        Refresh();
    }

    public void SelectMic(int index)
    {
        if (index < 0 || index >= MicNames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!CanSelect()) return;
        if (index == MicIndex) return;
        MicIndex = index;
        ApplyMic();
        SaveSettings();
        Refresh();
    }

    public void SelectSpeaker(int index)
    {
        if (index < 0 || index >= SpeakerNames.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!CanSelect()) return;
        if (index == SpeakerIndex) return;
        SpeakerIndex = index;
        ApplySpeaker();
        SaveSettings();
        Refresh();
    }

    public void SetRegion(System.Drawing.Rectangle bounds, int monitorNumber)
    {
        if (!CanSelect()) return;
        _region = CaptureTarget.ForRegion(bounds, _monitors, monitorNumber);
        RebuildTargets();
        SourceIndex = _monitors.Count + 1;
        SaveSettings();
        Refresh();
    }

    public async Task StartAsync()
    {
        if (!CanStart()) return;
        _busy = true;
        _starting = true;
        _message = null;
        _lastFinal = null;
        try
        {
            Refresh();
            await _session.StartAsync(Target, VideoQualities[VideoQualityIndex], AudioQualities[AudioQualityIndex]);
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
        }
        finally
        {
            _busy = false;
            _starting = false;
            Refresh();
        }
    }

    public async Task PauseOrResumeAsync()
    {
        if (_busy) return;
        RecordingState state = _session.State;
        if (state != RecordingState.Recording && state != RecordingState.Paused) return;
        _busy = true;
        try
        {
            Refresh();
            if (state == RecordingState.Recording) await _session.PauseAsync();
            else await _session.ResumeAsync();
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public Task StopAsync() => StopRecordingAsync();

    public async Task CancelAsync(Func<Task<bool>> confirm)
    {
        if (_busy || _confirming) return;
        if (!IsActive()) return;
        bool confirmed;
        _confirming = true;
        try
        {
            confirmed = await confirm();
        }
        finally
        {
            _confirming = false;
        }

        if (!confirmed || _busy || !IsActive()) return;
        _busy = true;
        _message = "CANCELING...";
        try
        {
            Refresh();
            await _session.CancelAsync();
            _lastFinal = null;
            _message = _session.LastError ?? "CANCELED: recording discarded";
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    public async Task<bool> PrepareCloseAsync()
    {
        RecordingState state = _session.State;
        if (state == RecordingState.Recording || state == RecordingState.Paused)
        {
            await StopRecordingAsync();
            return true;
        }

        return state != RecordingState.Finalizing;
    }

    public void UpdateStatus()
    {
        StatusLine status = ComputeStatus();
        if (status == Status) return;
        Status = status;
        Changed?.Invoke();
    }

    public void TickMeters()
    {
        MicLevel = LevelMeter.Next(_audio.MicPeak, MicLevel);
        SpeakerLevel = LevelMeter.Next(_audio.SpeakerPeak, SpeakerLevel);
    }

    public void ReportError(string message)
    {
        _message = "ERROR: " + message;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.StateChanged -= _onStateChanged;
        _session.Faulted -= _onFaulted;
        Changed = null;
    }

    private async Task StopRecordingAsync()
    {
        if (!IsActive()) return;
        _busy = true;
        _message = "SAVING...";
        try
        {
            Refresh();
            string? path = await _session.StopAsync();
            if (path != null)
            {
                _lastFinal = path;
                _message = "SAVED: " + Path.GetFileName(path);
            }
            else
            {
                _message = _session.LastError ?? "ERROR: save failed";
            }
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private bool IsActive() => _session.State == RecordingState.Recording || _session.State == RecordingState.Paused;

    private bool CanSelect()
    {
        if (_session.State == RecordingState.Idle && !_busy) return true;
        Changed?.Invoke();
        return false;
    }

    private bool CanStart() => _session.State == RecordingState.Idle && !_busy && _ffmpeg.Resolve() != null;

    private void Refresh()
    {
        Controls = ComputeControls();
        Overlay = ComputeOverlay();
        Status = ComputeStatus();
        Changed?.Invoke();
    }

    private ControlState ComputeControls()
    {
        RecordingState state = _session.State;
        bool idle = state == RecordingState.Idle;
        bool active = IsActive();
        return new ControlState(
            idle, idle, idle, idle, idle, idle,
            CanStart(),
            active && !_busy,
            state == RecordingState.Paused ? "> RESUME" : "|| PAUSE",
            active && !_busy,
            active && !_busy);
    }

    private OverlayMode ComputeOverlay()
    {
        if (_starting) return OverlayMode.Recording;
        return _session.State switch
        {
            RecordingState.Recording => OverlayMode.Recording,
            RecordingState.Paused => OverlayMode.Paused,
            _ => OverlayMode.Idle
        };
    }

    private StatusLine ComputeStatus()
    {
        RecordingState state = _session.State;
        switch (state)
        {
            case RecordingState.Recording:
            case RecordingState.Paused:
            {
                SessionStats stats = _session.Stats;
                string label = state == RecordingState.Recording ? "* REC" : "|| PAUSED";
                string text = $"{label}  {stats.Duration:hh\\:mm\\:ss}  {stats.Bytes / 1048576.0:F1} MB  {stats.QualityLabel}";
                return new StatusLine(text, state == RecordingState.Recording ? StatusSeverity.Recording : StatusSeverity.Warning);
            }
            case RecordingState.Finalizing:
                return new StatusLine("SAVING...", StatusSeverity.Warning);
            default:
                if (_message == null) return new StatusLine("READY", StatusSeverity.Normal);
                if (_message.StartsWith("ERROR", StringComparison.Ordinal)) return new StatusLine(_message, StatusSeverity.Error);
                if (_message.StartsWith("SAVED", StringComparison.Ordinal)) return new StatusLine(_message, StatusSeverity.Success);
                return new StatusLine(_message, StatusSeverity.Normal);
        }
    }

    private void RebuildTargets()
    {
        _targets = _monitors.Select(CaptureTarget.ForMonitor).ToList();
        _targets.Add(CaptureTarget.ForAll(_monitors));
        if (_region != null) _targets.Add(_region);
        _targetsView = _targets.AsReadOnly();
        _sourceLabels = _targets.Select(t => t.Label).ToList();
    }

    private int InitialTargetIndex()
    {
        if (_settings.TargetKind == "All") return _monitors.Count;
        int index = _monitors.FindIndex(m => m.Number == _settings.MonitorNumber);
        if (index >= 0) return index;
        index = _monitors.FindIndex(m => m.IsPrimary);
        return Math.Max(index, 0);
    }

    private static List<string> DeviceNames(List<AudioDeviceInfo> devices)
    {
        var names = new List<string> { NoneLabel };
        names.AddRange(devices.Select(d => d.Name));
        return names;
    }

    private int InitialDeviceIndex(List<AudioDeviceInfo> devices, string? savedId, string? defaultId)
    {
        string? wanted = _firstRun ? defaultId : savedId;
        if (wanted == null) return 0;
        int index = devices.FindIndex(d => d.Id == wanted);
        return index < 0 ? 0 : index + 1;
    }

    private void ApplyMic() => _audio.SetMic(MicIndex == 0 ? null : _mics[MicIndex - 1].Id);

    private void ApplySpeaker() => _audio.SetSpeaker(SpeakerIndex == 0 ? null : _speakers[SpeakerIndex - 1].Id);

    private void SaveSettings()
    {
        CaptureTarget target = Target;
        _settings.TargetKind = target.Kind == TargetKind.All ? "All" : "Monitor";
        if (target.Kind != TargetKind.All)
        {
            _settings.MonitorNumber = target.MonitorNumbers[0];
        }

        _settings.MicId = MicIndex == 0 ? null : _mics[MicIndex - 1].Id;
        _settings.SpeakerId = SpeakerIndex == 0 ? null : _speakers[SpeakerIndex - 1].Id;
        _settings.VideoQuality = VideoQualities[VideoQualityIndex];
        _settings.AudioQuality = AudioQualities[AudioQualityIndex];
        _saveSettings();
    }
}
