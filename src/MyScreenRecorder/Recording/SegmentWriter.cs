using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyScreenRecorder.Audio;
using MyScreenRecorder.Capture;
using MyScreenRecorder.Native;

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
    private readonly TaskCompletionSource<bool> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    private Process? _process;
    private NamedPipeServerStream? _pipe;
    private Task? _stdoutTask;
    private Task? _stderrTask;
    private Task? _stopTask;
    private long _currentBytes;
    private volatile bool _stopping;
    private volatile bool _active;
    private volatile bool _audioBegun;
    private int _faultRaised;

    public SegmentWriter(string ffmpegPath, string partsDir, int index, CaptureTarget target, VideoQuality vq, AudioQuality aq, AudioEngine audio, string sessionId)
    {
        _ffmpegPath = ffmpegPath;
        _partsDir = partsDir;
        _index = index;
        _target = target;
        _videoQuality = vq;
        _audioQuality = aq;
        _audio = audio;
        _sessionId = sessionId;
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
        string pipeName = $"srec_{_sessionId}_{_index}";

        if (hasAudio)
        {
            _pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 1 << 20);
        }

        ProcessStartInfo psi = BuildStartInfo(hasAudio, pipeName);
        Process process = new() { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += OnExited;

        try
        {
            process.Start();
        }
        catch
        {
            process.Exited -= OnExited;
            process.Dispose();
            DisposePipe();
            throw;
        }

        _process = process;
        try
        {
            KillOnCloseJob.Assign(process);
        }
        catch
        {
        }

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
                _audio.BeginSegment(_pipe!);
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

    private ProcessStartInfo BuildStartInfo(bool hasAudio, string pipeName)
    {
        System.Drawing.Rectangle b = _target.Bounds;
        int width = Math.Max(2, b.Width & ~1);
        int height = Math.Max(2, b.Height & ~1);

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
        psi.Environment["__COMPAT_LAYER"] = "HighDpiAware";

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
            args.Add(@"\\.\pipe\" + pipeName);
        }

        args.Add("-f");
        args.Add("gdigrab");
        args.Add("-framerate");
        args.Add("30");
        args.Add("-draw_mouse");
        args.Add("1");
        args.Add("-offset_x");
        args.Add(b.X.ToString(CultureInfo.InvariantCulture));
        args.Add("-offset_y");
        args.Add(b.Y.ToString(CultureInfo.InvariantCulture));
        args.Add("-video_size");
        args.Add(width.ToString(CultureInfo.InvariantCulture) + "x" + height.ToString(CultureInfo.InvariantCulture));
        args.Add("-thread_queue_size");
        args.Add("1024");
        args.Add("-i");
        args.Add("desktop");

        args.Add("-map");
        args.Add(hasAudio ? "1:v" : "0:v");
        if (hasAudio)
        {
            args.Add("-map");
            args.Add("0:a");
        }

        foreach (string a in Quality.VideoArgList(_videoQuality)) args.Add(a);
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
        NamedPipeServerStream? pipe = _pipe;
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
