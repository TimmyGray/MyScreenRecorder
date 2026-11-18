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

        await AssertExitsWithNoDisplayMessageAsync(start);
    }

    [Fact]
    public async Task Without_DISPLAY_the_app_exits_with_1_even_when_Avalonia_cannot_be_loaded()
    {
        var copy = Path.Combine(Path.GetTempPath(), "msr-no-avalonia-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyFiles(AppDirectory(), copy);
            var removed = Directory.GetFiles(copy, "Avalonia*.dll");
            Assert.NotEmpty(removed);
            foreach (var file in removed)
            {
                File.Delete(file);
            }

            var start = CreateStartInfo(environment => Set(environment, "DISPLAY", null), copy);

            await AssertExitsWithNoDisplayMessageAsync(start);
        }
        finally
        {
            if (Directory.Exists(copy))
            {
                Directory.Delete(copy, recursive: true);
            }
        }
    }

    private static async Task AssertExitsWithNoDisplayMessageAsync(ProcessStartInfo start)
    {
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
                process.WaitForExit();
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
        Assert.SkipUnless((await RunXwininfoAsync("-root")).ExitCode == 0, "needs a reachable X server (DISPLAY)");

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

    private static string AppDirectory()
    {
        return typeof(LinuxAppProcessTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "LinuxAppDirectory").Value!;
    }

    private static void CopyFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static ProcessStartInfo CreateStartInfo(Action<IDictionary<string, string?>> edit, string? directory = null)
    {
        directory ??= AppDirectory();
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
        var (exitCode, output) = await RunXwininfoAsync("-name", title);
        return exitCode == 0 && output.Contains("Map State: IsViewable", StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunXwininfoAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("xwininfo") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = Process.Start(start)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
            return (process.ExitCode, await output);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return (-1, string.Empty);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit();
            }
        }
    }
}
