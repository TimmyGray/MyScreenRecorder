namespace MyScreenRecorder.Controller;

public sealed record ControlState(
    bool SourceEnabled,
    bool VideoQualityEnabled,
    bool MicEnabled,
    bool SpeakerEnabled,
    bool AudioQualityEnabled,
    bool RegionEnabled,
    bool RecEnabled,
    bool PauseEnabled,
    string PauseLabel,
    bool StopEnabled,
    bool CancelEnabled);
