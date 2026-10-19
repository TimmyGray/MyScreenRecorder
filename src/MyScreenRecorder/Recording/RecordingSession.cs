using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Native;

namespace MyScreenRecorder.Recording;

public enum RecordingState
{
    Idle,
    Recording,
    Paused,
    Finalizing
}

public sealed record SessionStats(TimeSpan Duration, long Bytes, string QualityLabel);

public sealed class RecordingSession : IDisposable
{
    private readonly AudioEngine _audio;
    private readonly SemaphoreSlim _ops = new(1, 1);
    private readonly Stopwatch _watch = new();
    private readonly List<SegmentWriter> _segments = new();

    private volatile RecordingState _state = RecordingState.Idle;
    private SegmentWriter? _current;
    private long _finishedBytes;
    private int _nextIndex;
    private string _ffmpegPath = string.Empty;
    private string _partsDir = string.Empty;
    private string _stamp = string.Empty;
    private string _sessionId = string.Empty;
    private string _qualityLabel = string.Empty;
    private CaptureTarget? _target;
    private VideoQuality _videoQuality;
    private AudioQuality _audioQuality;
    private bool _hasAudio;
    private bool _disposed;

    public RecordingSession(AudioEngine audio)
    {
        _audio = audio;
    }

    public event Action? StateChanged;

    public event Action<string>? Faulted;

    public RecordingState State => _state;

    public string? LastError { get; private set; }

    public static string OutputFolder
    {
        get
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
            {
                videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            }

            string folder = Path.Combine(videos, "MyScreenRecorder");
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
            }

            return folder;
        }
    }

    public SessionStats Stats
    {
        get
        {
            SegmentWriter? current = _current;
            long bytes = Interlocked.Read(ref _finishedBytes) + (current?.CurrentBytes ?? 0);
            return new SessionStats(_watch.Elapsed, bytes, _qualityLabel);
        }
    }

    public async Task StartAsync(CaptureTarget target, VideoQuality vq, AudioQuality aq)
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state != RecordingState.Idle)
            {
                throw new InvalidOperationException("A recording is already in progress");
            }

            string? ffmpeg = FfmpegLocator.Resolve();
            if (ffmpeg == null)
            {
                throw new InvalidOperationException("ffmpeg not found");
            }

            LastError = null;
            _ffmpegPath = ffmpeg;
            _target = target;
            _videoQuality = vq;
            _audioQuality = aq;
            _hasAudio = _audio.HasAudio;
            _qualityLabel = _hasAudio
                ? Quality.Label(vq) + " / " + Quality.Label(aq)
                : Quality.Label(vq);
            _stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            _sessionId = Guid.NewGuid().ToString("N");
            _partsDir = Path.Combine(OutputFolder, ".parts", _stamp);
            Directory.CreateDirectory(_partsDir);

            _segments.Clear();
            Interlocked.Exchange(ref _finishedBytes, 0);
            _nextIndex = 0;
            _watch.Reset();

            try
            {
                await StartSegmentAsync().ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    Directory.Delete(_partsDir, true);
                }
                catch
                {
                }

                throw;
            }

            _watch.Start();
            SetState(RecordingState.Recording);
        }
        finally
        {
            _ops.Release();
        }
    }

    public async Task PauseAsync()
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state != RecordingState.Recording) return;
            _watch.Stop();
            await StopCurrentAsync().ConfigureAwait(false);
            SetState(RecordingState.Paused);
        }
        finally
        {
            _ops.Release();
        }
    }

    public async Task ResumeAsync()
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state != RecordingState.Paused) return;
            try
            {
                await StartSegmentAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                throw;
            }

            _watch.Start();
            SetState(RecordingState.Recording);
        }
        finally
        {
            _ops.Release();
        }
    }

    public async Task<string?> StopAsync()
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state != RecordingState.Recording && _state != RecordingState.Paused) return null;

            SetState(RecordingState.Finalizing);
            try
            {
                _watch.Stop();
                await StopCurrentAsync().ConfigureAwait(false);
                return await FinalizeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LastError = "Finalize failed: " + ex.Message + " Parts kept in " + _partsDir;
                return null;
            }
            finally
            {
                SetState(RecordingState.Idle);
            }
        }
        finally
        {
            _ops.Release();
        }
    }

    public async Task CancelAsync()
    {
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state != RecordingState.Recording && _state != RecordingState.Paused) return;

            SetState(RecordingState.Finalizing);
            try
            {
                _watch.Stop();
                await StopCurrentAsync().ConfigureAwait(false);
                await DeletePartsAsync().ConfigureAwait(false);
            }
            finally
            {
                SetState(RecordingState.Idle);
            }
        }
        finally
        {
            _ops.Release();
        }
    }

    private async Task DeletePartsAsync()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_partsDir)) Directory.Delete(_partsDir, true);
                return;
            }
            catch
            {
                await Task.Delay(300).ConfigureAwait(false);
            }
        }

        LastError = "Could not delete " + _partsDir;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SegmentWriter? current = _current;
        _current = null;
        if (current != null)
        {
            try
            {
                Task.Run(() => current.StopAsync()).Wait(TimeSpan.FromSeconds(25));
            }
            catch
            {
            }
        }
    }

    private async Task StartSegmentAsync()
    {
        int index = _nextIndex;
        SegmentWriter segment = new(_ffmpegPath, _partsDir, index, _target!, _videoQuality, _audioQuality, _audio, _sessionId);
        segment.Faulted += _ => OnSegmentFaulted(segment);
        await segment.StartAsync().ConfigureAwait(false);
        _nextIndex = index + 1;
        _segments.Add(segment);
        _current = segment;
    }

    private async Task StopCurrentAsync()
    {
        SegmentWriter? segment = _current;
        if (segment == null) return;
        try
        {
            await segment.StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        Interlocked.Add(ref _finishedBytes, segment.FinalBytes);
        _current = null;
    }

    private void OnSegmentFaulted(SegmentWriter segment)
    {
        if (_state != RecordingState.Recording || !ReferenceEquals(segment, _current)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            LastError = "ERROR: see ffmpeg.log";
            Faulted?.Invoke(LastError);
        });
    }

    private async Task<string?> FinalizeAsync()
    {
        List<SegmentWriter> valid = new();
        foreach (SegmentWriter segment in _segments)
        {
            try
            {
                FileInfo info = new(segment.FilePath);
                if (info.Exists && info.Length > 0) valid.Add(segment);
            }
            catch
            {
            }
        }

        if (valid.Count == 0)
        {
            LastError = "No video data was recorded. Parts folder: " + _partsDir;
            return null;
        }

        string listPath = Path.Combine(_partsDir, "list.txt");
        File.WriteAllLines(
            listPath,
            valid.Select(s => "file '" + Path.GetFileName(s.FilePath) + "'"),
            new UTF8Encoding(false));

        string ext = Quality.Extension(_audioQuality, _hasAudio);
        string finalPath = Path.Combine(OutputFolder, "REC_" + _stamp + "." + ext);

        ProcessStartInfo psi = new()
        {
            FileName = _ffmpegPath,
            WorkingDirectory = _partsDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", "list.txt", "-c", "copy" })
        {
            psi.ArgumentList.Add(arg);
        }

        if (ext == "mp4")
        {
            psi.ArgumentList.Add("-movflags");
            psi.ArgumentList.Add("+faststart");
        }

        psi.ArgumentList.Add(finalPath);

        int exitCode;
        string errors;
        using (Process process = new() { StartInfo = psi })
        {
            process.Start();
            try
            {
                KillOnCloseJob.Assign(process);
            }
            catch
            {
            }

            try
            {
                process.StandardInput.Close();
            }
            catch
            {
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            using CancellationTokenSource cts = new(TimeSpan.FromMinutes(10));
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }
            }

            await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(3000)).ConfigureAwait(false);
            exitCode = process.HasExited ? process.ExitCode : -1;
            errors = stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty;
        }

        if (exitCode == 0 && File.Exists(finalPath) && new FileInfo(finalPath).Length > 0)
        {
            try
            {
                Directory.Delete(_partsDir, true);
            }
            catch
            {
            }

            return finalPath;
        }

        if (!string.IsNullOrWhiteSpace(errors))
        {
            try
            {
                File.AppendAllText(Path.Combine(_partsDir, "ffmpeg.log"), "[concat] " + errors + Environment.NewLine);
            }
            catch
            {
            }
        }

        LastError = "Could not assemble the final file. Parts kept in " + _partsDir;
        return null;
    }

    private void SetState(RecordingState state)
    {
        _state = state;
        StateChanged?.Invoke();
    }
}
