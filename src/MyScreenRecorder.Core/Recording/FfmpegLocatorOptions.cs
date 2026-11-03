using System.Reflection;

namespace MyScreenRecorder.Recording;

public sealed record FfmpegLocatorOptions(string ExecutableName, Assembly? ResourceAssembly, string? ResourceName, string ExtractDirectory);
