using System;
using System.IO;

namespace MyScreenRecorder;

public static class AppPaths
{
    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "MyScreenRecorder",
        "settings.json");
}
