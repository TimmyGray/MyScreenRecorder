using System;
using System.Collections.Generic;
using Xunit;

namespace MyScreenRecorder.Platform.Linux.Tests;

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class SessionCheckTests
{
    private const string Wayland = "ERROR: Wayland session is not supported. Log in with an Xorg (X11) session.";
    private const string NoDisplay = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";

    [Theory]
    [InlineData("x11", null, ":0", SessionKind.X11)]
    [InlineData("tty", null, ":0", SessionKind.X11)]
    [InlineData(null, null, ":0", SessionKind.X11)]
    [InlineData("", "", ":0", SessionKind.X11)]
    [InlineData("x11", "", ":0", SessionKind.X11)]
    [InlineData("wayland", null, ":0", SessionKind.Wayland)]
    [InlineData("Wayland", null, ":0", SessionKind.Wayland)]
    [InlineData("WAYLAND", null, ":0", SessionKind.Wayland)]
    [InlineData(null, "wayland-0", ":0", SessionKind.Wayland)]
    [InlineData("x11", "wayland-0", ":0", SessionKind.Wayland)]
    [InlineData("wayland", "wayland-0", ":0", SessionKind.Wayland)]
    [InlineData(" wayland", null, ":0", SessionKind.X11)]
    [InlineData("wayland ", null, ":0", SessionKind.X11)]
    [InlineData("waylandx", null, ":0", SessionKind.X11)]
    [InlineData("xwayland", null, ":0", SessionKind.X11)]
    [InlineData(null, " ", ":0", SessionKind.Wayland)]
    [InlineData("x11", null, " ", SessionKind.X11)]
    [InlineData("x11", null, null, SessionKind.NoDisplay)]
    [InlineData("x11", null, "", SessionKind.NoDisplay)]
    [InlineData(null, null, null, SessionKind.NoDisplay)]
    [InlineData("wayland", null, "", SessionKind.NoDisplay)]
    [InlineData("wayland", "wayland-0", null, SessionKind.NoDisplay)]
    public void Evaluate_classifies_the_session(string? sessionType, string? waylandDisplay, string? display, SessionKind expected)
    {
        var environment = new Dictionary<string, string?>
        {
            ["XDG_SESSION_TYPE"] = sessionType,
            ["WAYLAND_DISPLAY"] = waylandDisplay,
            ["DISPLAY"] = display,
        };

        Assert.Equal(expected, SessionCheck.Evaluate(environment).Kind);
    }

    [Theory]
    [InlineData(SessionKind.X11, null)]
    [InlineData(SessionKind.Wayland, Wayland)]
    [InlineData(SessionKind.NoDisplay, NoDisplay)]
    public void Evaluate_returns_the_message_of_the_kind(SessionKind kind, string? message)
    {
        var environment = kind switch
        {
            SessionKind.X11 => new Dictionary<string, string?> { ["DISPLAY"] = ":0" },
            SessionKind.Wayland => new Dictionary<string, string?> { ["DISPLAY"] = ":0", ["XDG_SESSION_TYPE"] = "wayland" },
            _ => new Dictionary<string, string?>(),
        };

        var result = SessionCheck.Evaluate(environment);

        Assert.Equal(kind, result.Kind);
        Assert.Equal(message, result.Message);
    }

    [Fact]
    public void Evaluate_treats_absent_keys_as_unset()
    {
        Assert.Equal(SessionKind.NoDisplay, SessionCheck.Evaluate(new Dictionary<string, string?>()).Kind);
        Assert.Equal(SessionKind.X11, SessionCheck.Evaluate(new Dictionary<string, string?> { ["DISPLAY"] = ":0" }).Kind);
    }

    [Fact]
    public void Evaluate_matches_variable_names_exactly()
    {
        var environment = new Dictionary<string, string?>
        {
            ["display"] = ":0",
            ["xdg_session_type"] = "wayland",
        };

        Assert.Equal(SessionKind.NoDisplay, SessionCheck.Evaluate(environment).Kind);
    }

    [Fact]
    public void Evaluate_does_not_read_the_process_environment()
    {
        using var scope = new EnvironmentScope(("DISPLAY", ":0"), ("WAYLAND_DISPLAY", "wayland-0"), ("XDG_SESSION_TYPE", "wayland"));

        Assert.Equal(SessionKind.NoDisplay, SessionCheck.Evaluate(new Dictionary<string, string?>()).Kind);
    }
}

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class SessionCheckProcessEnvironmentTests : IDisposable
{
    private readonly EnvironmentScope _scope;

    public SessionCheckProcessEnvironmentTests()
    {
        _scope = new EnvironmentScope(("XDG_SESSION_TYPE", null), ("WAYLAND_DISPLAY", null), ("DISPLAY", null));
    }

    public void Dispose() => _scope.Dispose();

    [Fact]
    public void ReadProcessEnvironment_maps_each_variable_to_its_own_key()
    {
        Environment.SetEnvironmentVariable("XDG_SESSION_TYPE", "type-value");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-value");
        Environment.SetEnvironmentVariable("DISPLAY", "display-value");

        var environment = SessionCheck.ReadProcessEnvironment();

        Assert.Equal("type-value", environment["XDG_SESSION_TYPE"]);
        Assert.Equal("wayland-value", environment["WAYLAND_DISPLAY"]);
        Assert.Equal("display-value", environment["DISPLAY"]);
    }

    [Fact]
    public void ReadProcessEnvironment_reports_unset_variables_as_null()
    {
        var environment = SessionCheck.ReadProcessEnvironment();

        Assert.True(environment.ContainsKey("XDG_SESSION_TYPE"));
        Assert.True(environment.ContainsKey("WAYLAND_DISPLAY"));
        Assert.True(environment.ContainsKey("DISPLAY"));
        Assert.Null(environment["XDG_SESSION_TYPE"]);
        Assert.Null(environment["WAYLAND_DISPLAY"]);
        Assert.Null(environment["DISPLAY"]);
    }
}

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class EnvironmentScopeTests
{
    private const string SetBefore = "MSR_ENVIRONMENT_SCOPE_SET_BEFORE";
    private const string UnsetBefore = "MSR_ENVIRONMENT_SCOPE_UNSET_BEFORE";

    [Fact]
    public void Dispose_restores_the_values_from_before_the_scope()
    {
        Environment.SetEnvironmentVariable(SetBefore, "before");
        Environment.SetEnvironmentVariable(UnsetBefore, null);
        try
        {
            var scope = new EnvironmentScope((SetBefore, "inside"), (UnsetBefore, "inside-too"));
            Assert.Equal("inside", Environment.GetEnvironmentVariable(SetBefore));
            Assert.Equal("inside-too", Environment.GetEnvironmentVariable(UnsetBefore));

            scope.Dispose();

            Assert.Equal("before", Environment.GetEnvironmentVariable(SetBefore));
            Assert.Null(Environment.GetEnvironmentVariable(UnsetBefore));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SetBefore, null);
            Environment.SetEnvironmentVariable(UnsetBefore, null);
        }
    }

    [Fact]
    public void Dispose_restores_a_variable_the_scope_unset()
    {
        Environment.SetEnvironmentVariable(SetBefore, "before");
        try
        {
            var scope = new EnvironmentScope((SetBefore, null));
            Assert.Null(Environment.GetEnvironmentVariable(SetBefore));

            scope.Dispose();

            Assert.Equal("before", Environment.GetEnvironmentVariable(SetBefore));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SetBefore, null);
        }
    }
}

internal sealed class EnvironmentScope : IDisposable
{
    private readonly (string Name, string? Original)[] _saved;

    public EnvironmentScope(params (string Name, string? Value)[] values)
    {
        _saved = new (string, string?)[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            _saved[i] = (values[i].Name, Environment.GetEnvironmentVariable(values[i].Name));
        }

        foreach (var (name, value) in values)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in _saved)
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
