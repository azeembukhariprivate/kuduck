using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Kuduck.Services
{
    public class LastAction
    {
        public string Tool { get; set; }
        public string Summary { get; set; }
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>();
        public DateTime When { get; set; }
    }

    public class AppSettings
    {
        public bool DarkMode { get; set; }
        public bool PinOnTop { get; set; } = true;
        public bool CheckForUpdates { get; set; } = true;
        public DateTime LastUpdateCheck { get; set; }
        public double DuckLeft { get; set; } = double.NaN;
        public double DuckTop { get; set; } = double.NaN;
        public bool SaveNextToOriginal { get; set; } = true;
        public string OutputFolder { get; set; }
        public LastAction LastAction { get; set; }
        public List<string> RecentFiles { get; set; } = new List<string>();
        public bool SuggestionDismissed { get; set; }
        /// <summary>Last version the tray notification was shown for (so it pops up only once per version).</summary>
        public string NotifiedVersion { get; set; }
    }

    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        public static AppSettings Current { get; private set; } = new AppSettings();

        public static string FilePath => Path.Combine(AppPaths.DataDir, "settings.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                    if (loaded != null) Current = loaded;
                }
            }
            catch
            {
                Current = new AppSettings();
            }
            if (Current.RecentFiles == null) Current.RecentFiles = new List<string>();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Options));
            }
            catch
            {
                // Settings are best-effort; never crash the app over them.
            }
        }
    }
}
