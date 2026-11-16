using System;
using System.Collections.Generic;

namespace MyScreenRecorder.Platform.Linux;

public enum SessionKind
{
    X11,
    Wayland,
    NoDisplay,
}

public sealed record SessionCheckResult(SessionKind Kind, string? Message);

public static class SessionCheck
{
    public const string SessionTypeVariable = "XDG_SESSION_TYPE";
    public const string WaylandDisplayVariable = "WAYLAND_DISPLAY";
    public const string DisplayVariable = "DISPLAY";
    public const string WaylandMessage = "ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.";
    public const string NoDisplayMessage = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";

    public static SessionCheckResult Evaluate(IReadOnlyDictionary<string, string?> environment)
    {
        if (string.IsNullOrEmpty(Get(environment, DisplayVariable)))
        {
            return new SessionCheckResult(SessionKind.NoDisplay, NoDisplayMessage);
        }

        var wayland = string.Equals(Get(environment, SessionTypeVariable), "wayland", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Get(environment, WaylandDisplayVariable));
        return wayland
            ? new SessionCheckResult(SessionKind.Wayland, WaylandMessage)
            : new SessionCheckResult(SessionKind.X11, null);
    }

    public static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
    {
        return new Dictionary<string, string?>
        {
            [SessionTypeVariable] = Environment.GetEnvironmentVariable(SessionTypeVariable),
            [WaylandDisplayVariable] = Environment.GetEnvironmentVariable(WaylandDisplayVariable),
            [DisplayVariable] = Environment.GetEnvironmentVariable(DisplayVariable),
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string?> environment, string name)
    {
        return environment.TryGetValue(name, out var value) ? value : null;
    }
}
