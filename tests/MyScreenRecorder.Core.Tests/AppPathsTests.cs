using System;
using System.IO;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

[Collection(typeof(ProcessEnvironmentCollection))]
public sealed class AppPathsTests : IDisposable
{
    private readonly string? _xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? _home = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _xdgVideosDir = Environment.GetEnvironmentVariable("XDG_VIDEOS_DIR");
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("srec-apppaths-");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _xdgConfigHome);
        Environment.SetEnvironmentVariable("HOME", _home);
        Environment.SetEnvironmentVariable("XDG_VIDEOS_DIR", _xdgVideosDir);
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

    private string UseHome()
    {
        string home = Path.Combine(_dir.FullName, "home");
        string config = Path.Combine(_dir.FullName, "config");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(config);
        Environment.SetEnvironmentVariable("HOME", home);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", config);
        Environment.SetEnvironmentVariable("XDG_VIDEOS_DIR", null);
        return home;
    }

    [Fact]
    public void OutputFolder_OnLinuxWithoutUserDirs_IsUnderHomeVideosAndExists()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        string home = UseHome();

        string folder = AppPaths.OutputFolder;

        Assert.Equal(Path.Combine(home, "Videos", "MyScreenRecorder"), folder);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void OutputFolder_OnLinuxWithUserDirsVideosDir_UsesItWhenItExists()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        string home = UseHome();
        Directory.CreateDirectory(Path.Combine(home, "Movies"));
        File.WriteAllText(Path.Combine(_dir.FullName, "config", "user-dirs.dirs"), "XDG_VIDEOS_DIR=\"$HOME/Movies\"\n");

        Assert.Equal(Path.Combine(home, "Movies", "MyScreenRecorder"), AppPaths.OutputFolder);
    }

    [Fact]
    public void OutputFolder_OnLinuxWithUserDirsVideosDirMissing_FallsBackToHomeVideos()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        string home = UseHome();
        File.WriteAllText(Path.Combine(_dir.FullName, "config", "user-dirs.dirs"), "XDG_VIDEOS_DIR=\"$HOME/Movies\"\n");

        Assert.Equal(Path.Combine(home, "Videos", "MyScreenRecorder"), AppPaths.OutputFolder);
    }

    [Fact]
    public void OutputFolder_OnLinuxWhenFolderCannotBeCreated_StillReturnsThePath()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG paths apply to Linux only");
        string home = UseHome();
        File.WriteAllText(Path.Combine(home, "Videos"), "a file stands here");

        string folder = AppPaths.OutputFolder;

        Assert.Equal(Path.Combine(home, "Videos", "MyScreenRecorder"), folder);
        Assert.False(Directory.Exists(folder));
    }
}
