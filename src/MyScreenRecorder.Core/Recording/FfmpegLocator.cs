using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MyScreenRecorder.Recording;

public sealed class FfmpegLocator
{
    private readonly FfmpegLocatorOptions _options;
    private readonly string _baseDirectory;
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly object _gate = new();
    private string? _cached;

    public FfmpegLocator(FfmpegLocatorOptions options)
        : this(options, AppContext.BaseDirectory, Environment.GetEnvironmentVariable)
    {
    }

    internal FfmpegLocator(FfmpegLocatorOptions options, string baseDirectory, Func<string, string?> getEnvironmentVariable)
    {
        _options = options;
        _baseDirectory = baseDirectory;
        _getEnvironmentVariable = getEnvironmentVariable;
    }

    public string? Resolve()
    {
        lock (_gate)
        {
            if (_cached != null && File.Exists(_cached)) return _cached;
            _cached = ResolveCore();
            return _cached;
        }
    }

    private string? ResolveCore()
    {
        try
        {
            string beside = Path.Combine(_baseDirectory, _options.ExecutableName);
            if (File.Exists(beside)) return beside;
        }
        catch
        {
        }

        string? embedded = ExtractEmbedded();
        if (embedded != null) return embedded;

        return FindOnPath();
    }

    private string? ExtractEmbedded()
    {
        try
        {
            Assembly? asm = _options.ResourceAssembly;
            string? resourceName = _options.ResourceName;
            if (asm == null || resourceName == null) return null;

            string? name = asm.GetManifestResourceNames().FirstOrDefault(n =>
                string.Equals(n, resourceName, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;

            using Stream? resource = asm.GetManifestResourceStream(name);
            if (resource == null) return null;

            long length = resource.Length;
            string dir = _options.ExtractDirectory;
            Directory.CreateDirectory(dir);
            string stem = Path.GetFileNameWithoutExtension(resourceName);
            string extension = Path.GetExtension(resourceName);
            string target = Path.Combine(dir, $"{stem}_{length}{extension}");
            if (File.Exists(target) && new FileInfo(target).Length == length) return target;

            string temp = Path.Combine(dir, $"{stem}_{length}_{Guid.NewGuid():N}.tmp");
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

    private string? FindOnPath()
    {
        try
        {
            string? path = _getEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return null;
            foreach (string raw in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = raw.Trim().Trim('"');
                if (entry.Length == 0) continue;
                try
                {
                    string candidate = Path.Combine(entry, _options.ExecutableName);
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
