using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MyScreenRecorder.Recording;

public static class FfmpegLocator
{
    private static readonly object Gate = new();
    private static string? _cached;

    public static string? Resolve()
    {
        lock (Gate)
        {
            if (_cached != null && File.Exists(_cached)) return _cached;
            _cached = ResolveCore();
            return _cached;
        }
    }

    private static string? ResolveCore()
    {
        try
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(beside)) return beside;
        }
        catch
        {
        }

        string? embedded = ExtractEmbedded();
        if (embedded != null) return embedded;

        return FindOnPath();
    }

    private static string? ExtractEmbedded()
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string? name = asm.GetManifestResourceNames().FirstOrDefault(n =>
                string.Equals(n, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(".ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;

            using Stream? resource = asm.GetManifestResourceStream(name);
            if (resource == null) return null;

            long length = resource.Length;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyScreenRecorder");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, $"ffmpeg_{length}.exe");
            if (File.Exists(target) && new FileInfo(target).Length == length) return target;

            string temp = Path.Combine(dir, $"ffmpeg_{length}_{Guid.NewGuid():N}.tmp");
            try
            {
                using (FileStream output = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    resource.CopyTo(output);
                }

                try
                {
                    File.Move(temp, target, true);
                }
                catch
                {
                    if (!File.Exists(target)) throw;
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                }
            }

            return File.Exists(target) ? target : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOnPath()
    {
        try
        {
            string? path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (string raw in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = raw.Trim().Trim('"');
                if (entry.Length == 0) continue;
                try
                {
                    string candidate = Path.Combine(entry, "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
