using System;
using Avalonia;
using MyScreenRecorder.Platform.Linux;

namespace MyScreenRecorder.Linux;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var session = SessionCheck.Evaluate(SessionCheck.ReadProcessEnvironment());
        if (session.Kind == SessionKind.NoDisplay)
        {
            Console.Error.WriteLine(session.Message);
            return 1;
        }

        return AppBuilder.Configure(() => new App { Session = session })
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
