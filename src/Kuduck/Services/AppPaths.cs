using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Kuduck.Services
{
    public static class AppPaths
    {
        public const string AppName = "KUDUCK";
        public const string RepoOwner = "azeembukhariprivate";
        public const string RepoName = "kuduck";

        public static string DataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

        public static string TempDir
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), AppName);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string AppDir => AppContext.BaseDirectory;

        public static string ModelPath => Path.Combine(AppDir, "Assets", "models", "silueta.onnx");

        public static string HelperPath => Path.Combine(AppDir, "helper", "kuduck_helper.exe");

        public static string IconPath => Path.Combine(AppDir, "Assets", "kuduck.ico");

        public static string Version
        {
            get
            {
                System.Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public static string ReleasesUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases";

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        public static void ShowInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else if (Directory.Exists(path))
                    Process.Start("explorer.exe", $"\"{path}\"");
            }
            catch { }
        }
    }
}
