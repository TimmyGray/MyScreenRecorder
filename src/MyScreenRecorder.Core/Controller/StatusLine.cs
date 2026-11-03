namespace MyScreenRecorder.Controller;

public enum StatusSeverity
{
    Normal,
    Recording,
    Warning,
    Success,
    Error
}

public sealed record StatusLine(string Text, StatusSeverity Severity);
