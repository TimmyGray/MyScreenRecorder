using System.Diagnostics;

namespace MyScreenRecorder.Platform;

/// <summary>
/// Starts the pre-configured process (start info and event handlers already set) and registers it for cleanup
/// when the app dies. Throws only when the process did not start (whatever <see cref="Process.Start()"/> throws);
/// once the process has started, wrapping and registration failures are handled inside and never thrown, so a
/// caller that catches an exception has no running child to clean up. Does not return before the process has
/// started. May be called from any thread.
/// </summary>
public interface IChildProcessLauncher
{
    void Start(Process process);
}
