using System;
using System.IO;

namespace MyScreenRecorder.Core.Tests;

public sealed class FakeFfmpeg : IDisposable
{
    private const string Script = """
        #!/bin/sh
        dir=$(dirname "$0")
        mode=$(cat "$dir/mode" 2>/dev/null)
        for last; do :; done
        if [ "$mode" = exit-early ]; then exit 1; fi
        case " $* " in
          *" concat "*)
            if [ "$mode" = concat-fail ]; then echo "concat boom" >&2; exit 1; fi
            sed -n "s/^file '\(.*\)'\$/\1/p" list.txt | while read -r f; do cat "$f"; done > "$last"
            exit 0 ;;
        esac
        echo "segment warning" >&2
        if [ "$mode" = empty-output ]; then : > "$last"; else printf 'seg' > "$last"; fi
        echo total_size=1234
        if [ "$mode" = die-soon ]; then sleep 0.3; exit 1; fi
        while read -r line; do
          if [ "$line" = q ] && [ "$mode" != ignore-q ]; then break; fi
        done
        echo progress=end
        exit 0
        """;

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("srec-fakeffmpeg-");

    public FakeFfmpeg(string mode = "")
    {
        Path = System.IO.Path.Combine(_dir.FullName, "ffmpeg");
        File.WriteAllText(Path, Script.Replace("\r\n", "\n") + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Mode = mode;
    }

    public string Path { get; }

    public string DirectoryPath => _dir.FullName;

    public string Mode
    {
        set => File.WriteAllText(System.IO.Path.Combine(_dir.FullName, "mode"), value);
    }

    public void Dispose()
    {
        _dir.Delete(recursive: true);
    }
}
