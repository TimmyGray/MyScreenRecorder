using System;
using System.IO;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class AppPathsTests : IDisposable
{
    private readonly string? _xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? _home = Environment.GetEnvironmentVariable("HOME");
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("srec-apppaths-");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _xdgConfigHome);
        Environment.SetEnvironmentVariable("HOME", _home);
        _dir.Delete(recursive: true);
    }

    [Fact]
    public void SettingsFile_OnLinux_IsUnderXdgConfigHome()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _dir.FullName);

        Assert.Equal(Path.Combine(_dir.FullName, "MyScreenRecorder", "settings.json"), AppPaths.SettingsFile);
    }

    [Fact]
    public void SettingsFile_OnLinux_IsUnderXdgConfigHomeThatDoesNotExistYet()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        string missing = Path.Combine(_dir.FullName, "missing");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", missing);

        Assert.Equal(Path.Combine(missing, "MyScreenRecorder", "settings.json"), AppPaths.SettingsFile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/config")]
    public void SettingsFile_OnLinux_FallsBackToHomeConfigEvenWhenItDoesNotExistYet(string? xdgConfigHome)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        Environment.SetEnvironmentVariable("HOME", _dir.FullName);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdgConfigHome);

        Assert.Equal(Path.Combine(_dir.FullName, ".config", "MyScreenRecorder", "settings.json"), AppPaths.SettingsFile);
    }
}
