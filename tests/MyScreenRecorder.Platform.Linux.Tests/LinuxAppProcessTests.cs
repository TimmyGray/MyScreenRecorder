using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MyScreenRecorder.Platform.Linux.Tests;

[Trait("Category", "Integration")]
public sealed class LinuxAppProcessTests
{
    private const string NoDisplayMessage = "ERROR: DISPLAY is not set. MyScreenRecorder needs an Xorg (X11) session.";
    private const string WindowTitle = "MY SCREEN RECORDER";

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData(null, "wayland", "wayland-0")]
    [InlineData("", "x11", "")]
    public async Task Without_DISPLAY_the_app_prints_the_message_to_stderr_and_exits_with_1(string? display, string? sessionType, string? waylandDisplay)
    {
        var start = CreateStartInfo(environment =>
        {
            Set(environment, "DISPLAY", display);
            Set(environment, "XDG_SESSION_TYPE", sessionType);
            Set(environment, "WAYLAND_DISPLAY", waylandDisplay);
        });

        using var process = Process.Start(start)!;
        bool exited;
        Task<string> stderr;
        Task<string> stdout;
        try
        {
            stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            exited = process.WaitForExit(TimeSpan.FromSeconds(30));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        Assert.True(exited, "the app did not exit within 30 s");
        Assert.Equal(1, process.ExitCode);
        Assert.Equal(NoDisplayMessage + "\n", await stderr);
        Assert.Equal(string.Empty, await stdout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wayland")]
    public async Task On_an_X11_display_the_app_opens_a_window_titled_MY_SCREEN_RECORDER(string? sessionType)
    {
        Assert.SkipUnless(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")), "needs an X11 session (DISPLAY)");
        Assert.SkipUnless(OnPath("xwininfo"), "needs xwininfo on PATH");

        var start = CreateStartInfo(environment =>
        {
            Set(environment, "XDG_SESSION_TYPE", sessionType);
            Set(environment, "WAYLAND_DISPLAY", null);
        });
        using var process = Process.Start(start)!;
        try
        {
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            var found = false;
            while (!found && DateTime.UtcNow < deadline && !process.HasExited)
            {
                found = await XwininfoFindsAsync(WindowTitle);
                if (!found)
                {
                    await Task.Delay(250, TestContext.Current.CancellationToken);
                }
            }

            Assert.True(found, "no window titled '" + WindowTitle + "' appeared; app exited=" + process.HasExited + " stderr=" + (process.HasExited ? await stderr : "<running>"));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit();
        }
    }

    private static ProcessStartInfo CreateStartInfo(Action<IDictionary<string, string?>> edit)
    {
        var directory = typeof(LinuxAppProcessTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "LinuxAppDirectory").Value!;
        var executable = Path.Combine(directory, "MyScreenRecorder");
        Assert.True(File.Exists(executable), "the Linux app is not built: " + executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var dotnetRoot = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        start.Environment.Remove("DOTNET_ROOT_X64");
        start.Environment["DOTNET_ROOT"] = dotnetRoot;
        edit(start.Environment);
        return start;
    }

    private static void Set(IDictionary<string, string?> environment, string name, string? value)
    {
        if (value is null)
        {
            environment.Remove(name);
        }
        else
        {
            environment[name] = value;
        }
    }

    private static bool OnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Any(d => File.Exists(Path.Combine(d, name)));
    }

    private static async Task<bool> XwininfoFindsAsync(string title)
    {
        var start = new ProcessStartInfo("xwininfo") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-name");
        start.ArgumentList.Add(title);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await Task.WhenAll(output, error);
        return process.ExitCode == 0 && (await output).Contains("Map State: IsViewable", StringComparison.Ordinal);
    }
}
