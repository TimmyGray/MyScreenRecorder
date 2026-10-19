using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MyScreenRecorder.Audio;

public sealed class AudioEngine : IDisposable
{
    private const int Rate = 48000;
    private const int ChunkFrames = 4800;

    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _running;
    private bool _segmentActive;
    private Stream? _pipe;
    private MixingSampleProvider? _mixer;
    private List<AudioSource> _armedSources = new();
    private bool _disposed;

    public AudioSource? Mic { get; private set; }

    public AudioSource? Speaker { get; private set; }

    public bool HasAudio => Mic != null || Speaker != null;

    public string? LastError { get; private set; }

    public float MicPeak => Mic?.Peak ?? 0f;

    public float SpeakerPeak => Speaker?.Peak ?? 0f;

    public void SetMic(string? deviceId)
    {
        lock (_gate)
        {
            if (_segmentActive)
            {
                throw new InvalidOperationException("Cannot change audio devices while recording.");
            }
            Mic?.Dispose();
            Mic = null;
            LastError = null;
            Mic = Open(deviceId, false);
        }
    }

    public void SetSpeaker(string? deviceId)
    {
        lock (_gate)
        {
            if (_segmentActive)
            {
                throw new InvalidOperationException("Cannot change audio devices while recording.");
            }
            Speaker?.Dispose();
            Speaker = null;
            LastError = null;
            Speaker = Open(deviceId, true);
        }
    }

    private AudioSource? Open(string? deviceId, bool loopback)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return null;
        }

        AudioSource? source = null;
        try
        {
            source = new AudioSource(deviceId, loopback);
            source.Start();
            return source;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            source?.Dispose();
            return null;
        }
    }

    public void BeginSegment(Stream pipe)
    {
        lock (_gate)
        {
            if (_segmentActive)
            {
                throw new InvalidOperationException("A segment is already running.");
            }

            var sources = new List<AudioSource>();
            if (Mic != null)
            {
                sources.Add(Mic);
            }
            if (Speaker != null)
            {
                sources.Add(Speaker);
            }

            var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2))
            {
                ReadFully = true
            };

            foreach (var source in sources)
            {
                source.ClearBuffer();
                source.Armed = true;
                mixer.AddMixerInput(source.Provider);
            }

            _armedSources = sources;
            _mixer = mixer;
            _pipe = pipe;
            _segmentActive = true;
            _running = true;

            _thread = new Thread(() => Pump(mixer, pipe, sources))
            {
                IsBackground = true,
                Name = "AudioPump"
            };
            _thread.Start();
        }
    }

    public void EndSegment()
    {
        Thread? thread;
        Stream? pipe;
        List<AudioSource> sources;

        lock (_gate)
        {
            if (!_segmentActive)
            {
                return;
            }
            _segmentActive = false;
            _running = false;
            thread = _thread;
            pipe = _pipe;
            sources = _armedSources;
            _thread = null;
            _pipe = null;
            _mixer = null;
            _armedSources = new List<AudioSource>();
        }

        if (thread != null)
        {
            try
            {
                thread.Join(2000);
            }
            catch (Exception)
            {
            }
        }

        foreach (var source in sources)
        {
            source.Armed = false;
        }

        if (pipe != null)
        {
            try
            {
                pipe.Flush();
            }
            catch (Exception)
            {
            }
            try
            {
                pipe.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    private void Pump(MixingSampleProvider mixer, Stream pipe, List<AudioSource> sources)
    {
        var floats = new float[ChunkFrames * 2];
        var bytes = new byte[floats.Length * sizeof(float)];
        var stopwatch = Stopwatch.StartNew();
        long written = 0;

        try
        {
            while (_running)
            {
                long due = (long)(stopwatch.Elapsed.TotalSeconds * Rate) - written;
                if (due > 0)
                {
                    foreach (var source in sources)
                    {
                        if (source.BufferedMilliseconds > 200)
                        {
                            source.DropExcess(100);
                        }
                    }

                    long remaining = due;
                    while (remaining > 0 && _running)
                    {
                        int frames = (int)Math.Min(remaining, ChunkFrames);
                        int samples = frames * 2;
                        int read = mixer.Read(floats, 0, samples);
                        for (int i = read; i < samples; i++)
                        {
                            floats[i] = 0f;
                        }
                        for (int i = 0; i < samples; i++)
                        {
                            float v = floats[i];
                            if (v > 1f)
                            {
                                floats[i] = 1f;
                            }
                            else if (v < -1f)
                            {
                                floats[i] = -1f;
                            }
                            else if (float.IsNaN(v))
                            {
                                floats[i] = 0f;
                            }
                        }
                        Buffer.BlockCopy(floats, 0, bytes, 0, samples * sizeof(float));
                        pipe.Write(bytes, 0, samples * sizeof(float));
                        remaining -= frames;
                        written += frames;
                    }
                }

                Thread.Sleep(10);
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        EndSegment();

        lock (_gate)
        {
            Mic?.Dispose();
            Mic = null;
            Speaker?.Dispose();
            Speaker = null;
        }
    }
}
