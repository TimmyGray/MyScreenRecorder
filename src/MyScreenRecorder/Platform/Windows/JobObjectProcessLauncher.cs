using System.Diagnostics;
using System.Runtime.Versioning;
using MyScreenRecorder.Native;

namespace MyScreenRecorder.Platform.Windows;

[SupportedOSPlatform("windows")]
internal sealed class JobObjectProcessLauncher : IChildProcessLauncher
{
    /// <summary>
    /// Starts <paramref name="process"/> and assigns it to <see cref="KillOnCloseJob"/> so the child dies with the app.
    /// The job assignment is best effort: its failure is ignored and the process keeps running unassigned.
    /// </summary>
    public void Start(Process process)
    {
        process.Start();
        try
        {
            KillOnCloseJob.Assign(process);
        }
        catch
        {
        }
    }
}
