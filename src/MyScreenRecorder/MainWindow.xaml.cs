using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Overlay;
using MyScreenRecorder.Recording;

namespace MyScreenRecorder;

public partial class MainWindow : Window
{
    private const string NoneLabel = "NONE";

    private readonly Settings _settings;
    private readonly bool _firstRun;
    private readonly AudioEngine _audio = new();
    private readonly RecordingSession _session;
    private readonly OverlayManager _overlay = new();
    private readonly PreviewGrabber _preview;
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly VideoQuality[] _videoQualities = Enum.GetValues<VideoQuality>();
    private readonly AudioQuality[] _audioQualities = Enum.GetValues<AudioQuality>();

    private List<MonitorInfo> _monitors = new();
    private List<CaptureTarget> _targets = new();
    private List<AudioDeviceInfo> _mics = new();
    private List<AudioDeviceInfo> _speakers = new();
    private CaptureTarget? _region;
    private CaptureTarget _target = null!;
    private string? _lastFinal;
    private string? _message;
    private bool _ready;
    private bool _closing;
    private bool _busy;
    private float _micLevel;
    private float _speakerLevel;

    public MainWindow()
    {
        InitializeComponent();
        _firstRun = !File.Exists(AppPaths.SettingsFile);
        _settings = Settings.Load();
        _session = new RecordingSession(_audio);
        _preview = new PreviewGrabber(frame => PreviewImage.Source = frame);
        _session.StateChanged += () => Dispatcher.BeginInvoke(UpdateUi);
        _session.Faulted += message => Dispatcher.BeginInvoke(() =>
        {
            _message = message;
            UpdateUi();
        });
        _meterTimer.Tick += (_, _) => TickMeters();
        _statsTimer.Tick += (_, _) => UpdateStatus();
        Tips.ForTrimmed(StatusText);
        Tips.ForTrimmed(SavePathText);
        Tips.Set(SavePathText, RecordingSession.OutputFolder);
        Loaded += (_, _) => Initialize();
    }

    private void Initialize()
    {
        _monitors = ScreenInfo.GetMonitors().ToList();
        RebuildTargets(InitialTargetIndex());
        _target = _targets[SourceCycler.Index];

        VideoQualityCycler.SetItems(_videoQualities.Select(Quality.Label).ToList(), Array.IndexOf(_videoQualities, _settings.VideoQuality));
        AudioQualityCycler.SetItems(_audioQualities.Select(Quality.Label).ToList(), Array.IndexOf(_audioQualities, _settings.AudioQuality));

        _mics = AudioDevices.Capture().ToList();
        _speakers = AudioDevices.Render().ToList();
        MicCycler.SetItems(DeviceNames(_mics), InitialDeviceIndex(_mics, _settings.MicId, AudioDevices.DefaultCaptureId()));
        SpeakerCycler.SetItems(DeviceNames(_speakers), InitialDeviceIndex(_speakers, _settings.SpeakerId, AudioDevices.DefaultRenderId()));
        ApplyMic();
        ApplySpeaker();

        SourceCycler.Changed += OnSourceChanged;
        VideoQualityCycler.Changed += SaveSettings;
        AudioQualityCycler.Changed += SaveSettings;
        MicCycler.Changed += () =>
        {
            ApplyMic();
            SaveSettings();
        };
        SpeakerCycler.Changed += () =>
        {
            ApplySpeaker();
            SaveSettings();
        };

        if (FfmpegLocator.Resolve() == null)
        {
            _message = "ERROR: ffmpeg not found";
        }

        UpdateLayout();
        Rect work = SystemParameters.WorkArea;
        Top = Math.Max(work.Top, Math.Min(Top, work.Bottom - ActualHeight));

        _ready = true;
        _preview.Target = _target;
        _preview.Start();
        _meterTimer.Start();
        _statsTimer.Start();
        _overlay.Update(_target, OverlayState.Idle);
        UpdateUi();
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

    private void RebuildTargets(int selectIndex)
    {
        _targets = _monitors.Select(CaptureTarget.ForMonitor).ToList();
        _targets.Add(CaptureTarget.ForAll(ScreenInfo.GetMonitors()));
        if (_region != null) _targets.Add(_region);
        SourceCycler.SetItems(_targets.Select(t => t.Label).ToList(), selectIndex);
    }

    private void OnSourceChanged()
    {
        _target = _targets[SourceCycler.Index];
        _preview.Target = _target;
        _overlay.Update(_target, OverlayState.Idle);
        SaveSettings();
    }

    private void ApplyMic()
    {
        int index = MicCycler.Index;
        _audio.SetMic(index == 0 ? null : _mics[index - 1].Id);
    }

    private void ApplySpeaker()
    {
        int index = SpeakerCycler.Index;
        _audio.SetSpeaker(index == 0 ? null : _speakers[index - 1].Id);
    }

    private void SaveSettings()
    {
        if (!_ready) return;
        _settings.TargetKind = _target.Kind == TargetKind.All ? "All" : "Monitor";
        if (_target.MonitorNumbers.Length > 0 && _target.Kind != TargetKind.All)
        {
            _settings.MonitorNumber = _target.MonitorNumbers[0];
        }
        _settings.MicId = MicCycler.Index == 0 ? null : _mics[MicCycler.Index - 1].Id;
        _settings.SpeakerId = SpeakerCycler.Index == 0 ? null : _speakers[SpeakerCycler.Index - 1].Id;
        _settings.VideoQuality = _videoQualities[VideoQualityCycler.Index];
        _settings.AudioQuality = _audioQualities[AudioQualityCycler.Index];
        _settings.Save();
    }

    private void TickMeters()
    {
        _micLevel = Math.Max(_audio.MicPeak, _micLevel * 0.85f);
        _speakerLevel = Math.Max(_audio.SpeakerPeak, _speakerLevel * 0.85f);
        MicMeter.SetLevel(_micLevel);
        SpeakerMeter.SetLevel(_speakerLevel);
    }

    private async void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        RegionButton.IsEnabled = false;
        try
        {
            RegionResult? result = await RegionSelector.SelectAsync();
            if (result != null)
            {
                _region = CaptureTarget.ForRegion(result.Bounds, ScreenInfo.GetMonitors(), result.MonitorNumber);
                RebuildTargets(_monitors.Count + 1);
                OnSourceChanged();
            }
        }
        finally
        {
            UpdateUi();
        }
    }

    private async void RecButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        RecButton.IsEnabled = false;
        try
        {
            _message = null;
            _lastFinal = null;
            await _session.StartAsync(_target, _videoQualities[VideoQualityCycler.Index], _audioQualities[AudioQualityCycler.Index]);
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
        }
        finally
        {
            _busy = false;
            UpdateUi();
        }
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        PauseButton.IsEnabled = false;
        try
        {
            if (_session.State == RecordingState.Recording) await _session.PauseAsync();
            else if (_session.State == RecordingState.Paused) await _session.ResumeAsync();
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
        }
        finally
        {
            _busy = false;
            UpdateUi();
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        await StopRecordingAsync();
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_session.State != RecordingState.Recording && _session.State != RecordingState.Paused) return;

        MessageBoxResult answer = MessageBox.Show(
            this,
            "Discard this recording? It will not be saved.",
            "CANCEL RECORDING",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        if (_session.State != RecordingState.Recording && _session.State != RecordingState.Paused) return;
        _busy = true;
        _message = "CANCELING...";
        UpdateUi();
        try
        {
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
            UpdateUi();
        }
    }

    private async Task StopRecordingAsync()
    {
        if (_session.State != RecordingState.Recording && _session.State != RecordingState.Paused) return;
        _busy = true;
        _message = "SAVING...";
        UpdateUi();
        try
        {
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
            UpdateUi();
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastFinal != null && File.Exists(_lastFinal))
            {
                Process.Start("explorer.exe", $"/select,\"{_lastFinal}\"");
            }
            else
            {
                Process.Start("explorer.exe", $"\"{RecordingSession.OutputFolder}\"");
            }
        }
        catch (Exception ex)
        {
            _message = "ERROR: " + ex.Message;
            UpdateStatus();
        }
    }

    private void UpdateUi()
    {
        if (!_ready) return;
        RecordingState state = _session.State;
        bool idle = state == RecordingState.Idle;
        bool active = state == RecordingState.Recording || state == RecordingState.Paused;

        SourceCycler.IsEnabled = idle;
        VideoQualityCycler.IsEnabled = idle;
        MicCycler.IsEnabled = idle;
        SpeakerCycler.IsEnabled = idle;
        AudioQualityCycler.IsEnabled = idle;
        RegionButton.IsEnabled = idle;
        RecButton.IsEnabled = idle && !_busy && FfmpegLocator.Resolve() != null;
        PauseButton.IsEnabled = active && !_busy;
        PauseButton.Content = state == RecordingState.Paused ? "> RESUME" : "|| PAUSE";
        StopButton.IsEnabled = active && !_busy;
        CancelButton.IsEnabled = active && !_busy;

        OverlayState overlayState = state switch
        {
            RecordingState.Recording => OverlayState.Recording,
            RecordingState.Paused => OverlayState.Paused,
            _ => OverlayState.Idle
        };
        _overlay.Update(_target, overlayState);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (!_ready) return;
        RecordingState state = _session.State;
        Brush foreground = (Brush)FindResource("FgBrush");
        string text;
        switch (state)
        {
            case RecordingState.Recording:
            case RecordingState.Paused:
            {
                SessionStats stats = _session.Stats;
                string label = state == RecordingState.Recording ? "* REC" : "|| PAUSED";
                text = $"{label}  {stats.Duration:hh\\:mm\\:ss}  {stats.Bytes / 1048576.0:F1} MB  {stats.QualityLabel}";
                foreground = (Brush)FindResource(state == RecordingState.Recording ? "RedBrush" : "YellowBrush");
                break;
            }
            case RecordingState.Finalizing:
                text = "SAVING...";
                foreground = (Brush)FindResource("YellowBrush");
                break;
            default:
                text = _message ?? "READY";
                if (_message != null && _message.StartsWith("ERROR", StringComparison.Ordinal)) foreground = (Brush)FindResource("RedBrush");
                else if (_message != null && _message.StartsWith("SAVED", StringComparison.Ordinal)) foreground = (Brush)FindResource("GreenBrush");
                break;
        }
        Tips.Set(StatusText, text);
        StatusText.Foreground = foreground;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        _overlay.SetVisible(WindowState != WindowState.Minimized);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closing) return;
        RecordingState state = _session.State;
        if (state == RecordingState.Recording || state == RecordingState.Paused)
        {
            e.Cancel = true;
            await StopRecordingAsync();
            _closing = true;
            Close();
        }
        else if (state == RecordingState.Finalizing)
        {
            e.Cancel = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _meterTimer.Stop();
        _statsTimer.Stop();
        _preview.Stop();
        _overlay.Dispose();
        _session.Dispose();
        _audio.Dispose();
    }
}
