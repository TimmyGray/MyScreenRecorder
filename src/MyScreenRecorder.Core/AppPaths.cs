using System;
using System.IO;

namespace MyScreenRecorder;

public static class AppPaths
{
    public static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "MyScreenRecorder",
        "settings.json");

    public static string OutputFolder
    {
        get
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (string.IsNullOrEmpty(videos))
            {
                videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
            }

            string folder = Path.Combine(videos, "MyScreenRecorder");
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
            }

            return folder;
        }
    }
}
