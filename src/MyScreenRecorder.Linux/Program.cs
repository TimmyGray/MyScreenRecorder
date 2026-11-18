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

        return StartAvalonia(session, args);
    }

    // Kept out of Main: compiling a method loads the assemblies it references, and the NoDisplay exit must not depend on Avalonia loading.
    private static int StartAvalonia(SessionCheckResult session, string[] args)
    {
        return AppBuilder.Configure(() => new App { Session = session })
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
