using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Platform;

namespace MyScreenRecorder.Recording;

public sealed class SegmentWriter
{
    private static readonly object LogGate = new();

    private readonly string _ffmpegPath;
    private readonly string _partsDir;
    private readonly int _index;
    private readonly CaptureTarget _target;
    private readonly VideoQuality _videoQuality;
    private readonly AudioQuality _audioQuality;
    private readonly AudioEngine _audio;
    private readonly string _sessionId;
    private readonly IChildProcessLauncher _launcher;
    private readonly IAudioPipeFactory _pipes;
    private readonly ScreenGrab _grab;
    private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private Process? _process;
    private IAudioPipe? _pipe;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private Task? _stopTask;
    private long _currentBytes;
    private volatile bool _stopping;
    private volatile bool _active;
    private volatile bool _audioBegun;
    private int _faultRaised;

    public SegmentWriter(string ffmpegPath, string partsDir, int index, CaptureTarget target, VideoQuality vq, AudioQuality aq, AudioEngine audio, string sessionId, IChildProcessLauncher launcher, IAudioPipeFactory pipes, ScreenGrab grab)
    {
        _ffmpegPath = ffmpegPath;
        _partsDir = partsDir;
        _index = index;
        _target = target;
        _videoQuality = vq;
        _audioQuality = aq;
        _audio = audio;
        _sessionId = sessionId;
        _launcher = launcher;
        _pipes = pipes;
        _grab = grab;
        FilePath = Path.Combine(partsDir, $"seg_{index:D3}.mkv");
    }

    public event Action<string>? Faulted;

    public string FilePath { get; }

    public long FinalBytes { get; private set; }

    public long CurrentBytes
    {
        get => Interlocked.Read(ref _currentBytes);
        private set => Interlocked.Exchange(ref _currentBytes, value);
    }

    public async Task StartAsync()
    {
        Directory.CreateDirectory(_partsDir);
        bool hasAudio = _audio.HasAudio;

        if (hasAudio)
        {
            _pipe = _pipes.Create(_sessionId, _index);
        }

        ProcessStartInfo psi;
        try
        {
            psi = BuildStartInfo(hasAudio ? _pipe!.FfmpegInput : null);
        }
        catch
        {
            DisposePipe();
            throw;
        }

        Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += OnExited;

        try
        {
            _launcher.Start(process);
        }
        catch
        {
            process.Exited -= OnExited;
            process.Dispose();
            DisposePipe();
            throw;
        }

        _process = process;

        _stdoutTask = Task.Run(() => ReadStdoutAsync(process));
        _stderrTask = Task.Run(() => ReadStderrAsync(process));

        if (hasAudio)
        {
            try
            {
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
                Task connect = _pipe!.WaitForConnectionAsync(cts.Token);
                Task first = await Task.WhenAny(connect, _exited.Task).ConfigureAwait(false);
                if (!ReferenceEquals(first, connect))
                {
                    throw new InvalidOperationException("ffmpeg exited before the audio pipe was connected. See ffmpeg.log in " + _partsDir);
                }

                await connect.ConfigureAwait(false);
                _audio.BeginSegment(_pipe!.Stream);
                _audioBegun = true;
            }
            catch (Exception ex)
            {
                _stopping = true;
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch
                {
                }

                await Task.WhenAny(_exited.Task, Task.Delay(3000)).ConfigureAwait(false);
                ReleaseResources();
                if (ex is InvalidOperationException) throw;
                throw new InvalidOperationException("Could not connect the audio pipe to ffmpeg. See ffmpeg.log in " + _partsDir, ex);
            }
        }

        _active = true;
        if (_exited.Task.IsCompleted && !_stopping) RaiseFaulted();
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            _stopTask ??= StopCoreAsync();
            return _stopTask;
        }
    }

    internal ProcessStartInfo BuildStartInfo(string? audioInput)
    {
        bool hasAudio = audioInput != null;
        System.Drawing.Rectangle b = _target.Bounds;
        System.Drawing.Rectangle even = new(b.X, b.Y, Math.Max(2, b.Width & ~1), Math.Max(2, b.Height & ~1));

        ProcessStartInfo psi = new()
        {
            FileName = _ffmpegPath,
            WorkingDirectory = _partsDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        _grab.ApplyEnvironment(psi.Environment);

        var args = psi.ArgumentList;
        args.Add("-hide_banner");
        args.Add("-loglevel");
        args.Add("error");
        args.Add("-y");

        if (hasAudio)
        {
            args.Add("-f");
            args.Add("f32le");
            args.Add("-ar");
            args.Add("48000");
            args.Add("-ac");
            args.Add("2");
            args.Add("-thread_queue_size");
            args.Add("1024");
            args.Add("-i");
            args.Add(audioInput!);
        }

        foreach (string a in _grab.InputArgs(even, 15)) args.Add(a);

        args.Add("-map");
        args.Add(hasAudio ? "1:v" : "0:v");
        if (hasAudio)
        {
            args.Add("-map");
            args.Add("0:a");
        }

        foreach (string a in Quality.VideoArgList(_videoQuality)) args.Add(a);
        foreach (string a in _grab.EncoderArgs()) args.Add(a);
        if (hasAudio)
        {
            foreach (string a in Quality.AudioArgList(_audioQuality)) args.Add(a);
        }

        args.Add("-progress");
        args.Add("pipe:1");
        args.Add("-nostats");
        args.Add(FilePath);

        return psi;
    }

    private void OnExited(object? sender, EventArgs e)
    {
        _exited.TrySetResult(true);
        if (_active && !_stopping) RaiseFaulted();
    }

    private void RaiseFaulted()
    {
        if (Interlocked.Exchange(ref _faultRaised, 1) == 0)
        {
            Faulted?.Invoke("ERROR: see ffmpeg.log");
        }
    }

    private async Task ReadStdoutAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (line.StartsWith("total_size=", StringComparison.Ordinal) &&
                    long.TryParse(line.AsSpan(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
                {
                    CurrentBytes = value;
                }
            }
        }
        catch
        {
        }
    }

    private async Task ReadStderrAsync(Process process)
    {
        try
        {
            string logPath = Path.Combine(_partsDir, "ffmpeg.log");
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                try
                {
                    lock (LogGate)
                    {
                        File.AppendAllText(logPath, $"[seg_{_index:D3}] {line}{Environment.NewLine}");
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    private async Task StopCoreAsync()
    {
        _stopping = true;
        Process? process = _process;

        if (process != null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Write("q\n");
                    process.StandardInput.Flush();
                }
            }
            catch
            {
            }

            if (_audioBegun)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        _audio.EndSegment();
                    }
                    catch
                    {
                    }
                }).ConfigureAwait(false);
            }

            Task finished = await Task.WhenAny(_exited.Task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
            if (!ReferenceEquals(finished, _exited.Task))
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                }

                await Task.WhenAny(_exited.Task, Task.Delay(5000)).ConfigureAwait(false);
            }

            Task? stdout = _stdoutTask;
            Task? stderr = _stderrTask;
            if (stdout != null && stderr != null)
            {
                await Task.WhenAny(Task.WhenAll(stdout, stderr), Task.Delay(2000)).ConfigureAwait(false);
            }
        }

        long size = 0;
        try
        {
            FileInfo info = new(FilePath);
            if (info.Exists) size = info.Length;
        }
        catch
        {
        }

        FinalBytes = size;
        CurrentBytes = size;
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        Process? process = _process;
        _process = null;
        if (process != null)
        {
            try
            {
                process.Exited -= OnExited;
            }
            catch
            {
            }

            try
            {
                process.Dispose();
            }
            catch
            {
            }
        }

        DisposePipe();
    }

    private void DisposePipe()
    {
        IAudioPipe? pipe = _pipe;
        _pipe = null;
        if (pipe != null)
        {
            try
            {
                pipe.Dispose();
            }
            catch
            {
            }
        }
    }
}
