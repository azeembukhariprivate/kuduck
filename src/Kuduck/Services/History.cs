using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kuduck.Services
{
    /// <summary>Remembers the last action (for the duck's suggestion) and recent output files.</summary>
    public static class History
    {
        public static event EventHandler Changed;

        public static void Record(string tool, string summary, Dictionary<string, string> settings, IEnumerable<string> outputs)
        {
            AppSettings s = SettingsStore.Current;
            s.LastAction = new LastAction
            {
                Tool = tool,
                Summary = summary,
                Settings = settings ?? new Dictionary<string, string>(),
                When = DateTime.Now
            };
            s.SuggestionDismissed = false;
            if (outputs != null)
            {
                foreach (string o in outputs.Where(File.Exists))
                {
                    s.RecentFiles.Remove(o);
                    s.RecentFiles.Insert(0, o);
                }
                if (s.RecentFiles.Count > 8) s.RecentFiles = s.RecentFiles.Take(8).ToList();
            }
            SettingsStore.Save();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static List<string> ExistingRecent() =>
            SettingsStore.Current.RecentFiles.Where(File.Exists).ToList();

        public static string Get(Dictionary<string, string> settings, string key, string fallback)
        {
            if (settings != null && settings.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v)) return v;
            return fallback;
        }
    }
}
