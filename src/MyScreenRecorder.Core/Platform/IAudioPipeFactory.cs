namespace MyScreenRecorder.Platform;

/// <summary>Creates the audio channel for one recording segment.</summary>
public interface IAudioPipeFactory
{
    /// <summary>
    /// <paramref name="sessionId"/> is a 32-hex string, <paramref name="index"/> the segment index. The pipe is
    /// listening before this returns, so a process started afterwards can connect. Throws when the endpoint
    /// cannot be created. The caller disposes the result; <see cref="IAudioPipe.Dispose"/> may run more than once.
    /// </summary>
    IAudioPipe Create(string sessionId, int index);
}
