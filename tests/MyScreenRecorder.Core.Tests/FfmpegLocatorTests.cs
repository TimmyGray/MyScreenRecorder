using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MyScreenRecorder.Recording;
using Xunit;

namespace MyScreenRecorder.Core.Tests;

public sealed class FfmpegLocatorTests : IDisposable
{
    private static readonly Assembly TestAssembly = typeof(FfmpegLocatorTests).Assembly;
    private static readonly byte[] ResourceBytes = System.Text.Encoding.ASCII.GetBytes("fake ffmpeg resource");
    private static readonly byte[] SuffixedBytes = System.Text.Encoding.ASCII.GetBytes("suffixed resource payload");
    private static readonly byte[] ToolBytes = System.Text.Encoding.ASCII.GetBytes("tool resource");
    private static readonly byte[] NoExtensionBytes = System.Text.Encoding.ASCII.GetBytes("no extension resource");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("srec-locator-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
    }

    private string NewDir(string name)
    {
        string path = Path.Combine(_root.FullName, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string Extract => Path.Combine(_root.FullName, "extract");

    private FfmpegLocatorOptions Options(string? resourceName = "ffmpeg.exe", bool withAssembly = true, string name = "ffmpeg.exe") =>
        new(name, withAssembly ? TestAssembly : null, resourceName, Extract);

    private static FfmpegLocator Locator(FfmpegLocatorOptions options, string baseDirectory, Func<string?> path) =>
        new(options, baseDirectory, key => key == "PATH" ? path() : null);

    private static string Touch(string dir, string name)
    {
        string file = Path.Combine(dir, name);
        File.WriteAllText(file, "x");
        return file;
    }

    [Fact]
    public void Embedded_IsExtractedWithExactBytes()
    {
        var locator = Locator(Options(), NewDir("base"), () => NewDir("emptypath"));

        string? result = locator.Resolve();

        Assert.Equal(Path.Combine(Extract, $"ffmpeg_{ResourceBytes.Length}.exe"), result);
        Assert.Equal(ResourceBytes, File.ReadAllBytes(result!));
        Assert.Empty(Directory.GetFiles(Extract, "*.tmp"));
    }

    [Fact]
    public void Embedded_ExistingFileOfSameLength_IsNotRewritten()
    {
        string baseDir = NewDir("base");
        string first = Locator(Options(), baseDir, () => null).Resolve()!;
        File.SetLastWriteTimeUtc(first, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        DateTime before = File.GetLastWriteTimeUtc(first);

        string? second = Locator(Options(), baseDir, () => null).Resolve();

        Assert.Equal(first, second);
        Assert.Equal(before, File.GetLastWriteTimeUtc(first));
    }

    [Fact]
    public void Embedded_ExistingFileOfOtherLength_IsReplaced()
    {
        Directory.CreateDirectory(Extract);
        string target = Path.Combine(Extract, $"ffmpeg_{ResourceBytes.Length}.exe");
        File.WriteAllText(target, "short");

        string? result = Locator(Options(), NewDir("base"), () => null).Resolve();

        Assert.Equal(target, result);
        Assert.Equal(ResourceBytes, File.ReadAllBytes(target));
        Assert.Empty(Directory.GetFiles(Extract, "*.tmp"));
    }

    [Fact]
    public void BesideExecutable_WinsOverEmbeddedAndPath()
    {
        string baseDir = NewDir("base");
        string beside = Touch(baseDir, "ffmpeg.exe");
        string onPath = NewDir("onpath");
        Touch(onPath, "ffmpeg.exe");

        string? result = Locator(Options(), baseDir, () => onPath).Resolve();

        Assert.Equal(beside, result);
    }

    [Fact]
    public void Embedded_WinsOverPath()
    {
        string onPath = NewDir("onpath");
        Touch(onPath, "ffmpeg.exe");

        string? result = Locator(Options(), NewDir("base"), () => onPath).Resolve();

        Assert.Equal(Path.Combine(Extract, $"ffmpeg_{ResourceBytes.Length}.exe"), result);
    }

    [Fact]
    public void Path_IsSplitByPlatformSeparatorAndFirstMatchWins()
    {
        string empty = NewDir("empty");
        string withExe = NewDir("withexe");
        Touch(withExe, "ffmpeg");
        string other = NewDir("other");
        Touch(other, "ffmpeg");
        string path = string.Join(Path.PathSeparator, empty, withExe, other);

        string? result = Locator(Options(withAssembly: false, name: "ffmpeg"), NewDir("base"), () => path).Resolve();

        Assert.Equal(Path.Combine(withExe, "ffmpeg"), result);
    }

    [Fact]
    public void Path_EntriesAreTrimmedUnquotedAndEmptyOnesSkipped()
    {
        string withExe = NewDir("with exe");
        Touch(withExe, "ffmpeg");
        string path = string.Join(Path.PathSeparator, string.Empty, "  ", "\"" + withExe + "\"");

        Assert.Equal(Path.Combine(withExe, "ffmpeg"), Locator(Options(withAssembly: false, name: "ffmpeg"), NewDir("b1"), () => path).Resolve());

        string padded = "  " + withExe + "  ";
        Assert.Equal(Path.Combine(withExe, "ffmpeg"), Locator(Options(withAssembly: false, name: "ffmpeg"), NewDir("b2"), () => padded).Resolve());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(";;")]
    public void Path_UnsetEmptyOrOnlySeparators_YieldsNull(string? value)
    {
        string path = value == ";;" ? string.Join(Path.PathSeparator, string.Empty, string.Empty, string.Empty) : value!;

        Assert.Null(Locator(Options(withAssembly: false), NewDir("base"), () => path).Resolve());
    }

    [Fact]
    public void Resolve_ReadsOnlyThePathVariable()
    {
        string withExe = NewDir("withexe");
        Touch(withExe, "ffmpeg");
        var locator = new FfmpegLocator(Options(withAssembly: false, name: "ffmpeg"), NewDir("base"), key => key == "PATH" ? withExe : "/nonexistent");

        Assert.Equal(Path.Combine(withExe, "ffmpeg"), locator.Resolve());
    }

    [Fact]
    public void ResourceName_MatchesBySuffixAndCaseInsensitively()
    {
        string? suffix = Locator(Options("fake.exe"), NewDir("base"), () => null).Resolve();
        Assert.Equal(Path.Combine(Extract, $"fake_{SuffixedBytes.Length}.exe"), suffix);
        Assert.Equal(SuffixedBytes, File.ReadAllBytes(suffix!));

        string? upper = Locator(Options("FFMPEG.EXE"), NewDir("base2"), () => null).Resolve();
        Assert.Equal(ResourceBytes, File.ReadAllBytes(upper!));
    }

    [Fact]
    public void ResourceName_SuffixMatchNeedsADotBoundary()
    {
        Assert.Null(Locator(Options("ools.fake.exe"), NewDir("base"), () => null).Resolve());
        Assert.False(Directory.Exists(Extract));
    }

    [Fact]
    public void ResourceName_ExtensionOfTheExtractedFileComesFromTheResourceName()
    {
        string? withExtension = Locator(Options("tool.bin"), NewDir("base"), () => null).Resolve();
        Assert.Equal(Path.Combine(Extract, $"tool_{ToolBytes.Length}.bin"), withExtension);
        Assert.Equal(ToolBytes, File.ReadAllBytes(withExtension!));

        string? withoutExtension = Locator(Options("noext"), NewDir("base2"), () => null).Resolve();
        Assert.Equal(Path.Combine(Extract, $"noext_{NoExtensionBytes.Length}"), withoutExtension);
        Assert.Equal(NoExtensionBytes, File.ReadAllBytes(withoutExtension!));
    }

    [Fact]
    public void ResourceName_NotInAssembly_FallsThroughToPathOrNull()
    {
        Assert.Null(Locator(Options("nothere.exe"), NewDir("base"), () => null).Resolve());

        string onPath = NewDir("onpath");
        string exe = Touch(onPath, "ffmpeg.exe");
        Assert.Equal(exe, Locator(Options("nothere.exe"), NewDir("base2"), () => onPath).Resolve());
    }

    [Fact]
    public void ResourceNameNull_SkipsEmbeddedWithoutException()
    {
        Assert.Null(Locator(Options(resourceName: null), NewDir("base"), () => null).Resolve());
        Assert.False(Directory.Exists(Extract));
    }

    [Fact]
    public void ExtractDirectoryThatCannotBeCreated_FallsBackToPathOrNull()
    {
        File.WriteAllText(Extract, "a file stands here");
        string onPath = NewDir("onpath");
        string exe = Touch(onPath, "ffmpeg.exe");

        Assert.Null(Locator(Options(), NewDir("base"), () => null).Resolve());
        Assert.Equal(exe, Locator(Options(), NewDir("base2"), () => onPath).Resolve());
    }

    [Fact]
    public void Resolve_CachesUntilTheCachedFileIsDeleted()
    {
        string first = NewDir("first");
        string firstExe = Touch(first, "ffmpeg");
        string second = NewDir("second");
        string secondExe = Touch(second, "ffmpeg");
        string current = first;
        var locator = Locator(Options(withAssembly: false, name: "ffmpeg"), NewDir("base"), () => current);

        Assert.Equal(firstExe, locator.Resolve());
        current = second;
        Assert.Equal(firstExe, locator.Resolve());

        File.Delete(firstExe);
        Assert.Equal(secondExe, locator.Resolve());
    }

    [Fact]
    public void Resolve_NothingFound_ReturnsNull()
    {
        Assert.Null(Locator(Options(withAssembly: false), NewDir("base"), () => NewDir("empty")).Resolve());
    }

    [Fact]
    public void TestAssembly_HasTheTestResources()
    {
        string[] names = TestAssembly.GetManifestResourceNames();
        Assert.Contains("ffmpeg.exe", names);
        Assert.Contains("Vendor.Tools.fake.exe", names);
        Assert.Contains("tool.bin", names);
        Assert.Contains("noext", names);
        Assert.Equal(2, names.Count(n => n.EndsWith(".exe", StringComparison.Ordinal)));
    }
}
