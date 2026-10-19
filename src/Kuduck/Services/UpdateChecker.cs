using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kuduck.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        /// <summary>Release page (what's new).</summary>
        public string Url { get; set; }
        /// <summary>Direct download of KUDUCK-Setup-x.y.z.exe, when the release has one.</summary>
        public string InstallerUrl { get; set; }
        public string InstallerName { get; set; }
        public long InstallerSize { get; set; }
        /// <summary>SHA-256 of the installer as reported by GitHub (lower-case hex), if available.</summary>
        public string Sha256 { get; set; }
    }

    /// <summary>Asks GitHub for the latest release. Never downloads anything by itself.</summary>
    public static class UpdateChecker
    {
        public static string InstallerPrefix =>
            $"https://github.com/{AppPaths.RepoOwner}/{AppPaths.RepoName}/releases/download/";

        public static async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("KUDUCK/" + AppPaths.Version);
                    string url = $"https://api.github.com/repos/{AppPaths.RepoOwner}/{AppPaths.RepoName}/releases/latest";
                    string json = await http.GetStringAsync(url);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        string tag = (root.GetProperty("tag_name").GetString() ?? "").TrimStart('v', 'V');
                        if (!IsNewer(tag, AppPaths.Version)) return null;

                        UpdateInfo info = new UpdateInfo { Version = tag, Url = root.GetProperty("html_url").GetString() };
                        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement a in assets.EnumerateArray())
                            {
                                string name = a.GetProperty("name").GetString() ?? "";
                                string download = a.GetProperty("browser_download_url").GetString() ?? "";
                                bool isInstaller = name.StartsWith("KUDUCK-Setup", StringComparison.OrdinalIgnoreCase)
                                                   && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                                // Only ever accept installers published on this project's own Releases page.
                                if (!isInstaller || !download.StartsWith(InstallerPrefix, StringComparison.Ordinal)) continue;
                                info.InstallerName = name;
                                info.InstallerUrl = download;
                                if (a.TryGetProperty("size", out JsonElement size) && size.ValueKind == JsonValueKind.Number)
                                    info.InstallerSize = size.GetInt64();
                                if (a.TryGetProperty("digest", out JsonElement digest) && digest.ValueKind == JsonValueKind.String)
                                {
                                    string d = digest.GetString() ?? "";
                                    if (d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                                        info.Sha256 = d.Substring(7).ToLowerInvariant();
                                }
                                break;
                            }
                        }
                        return info;
                    }
                }
            }
            catch
            {
                // Offline or no releases yet — stay quiet.
                return null;
            }
        }

        private static bool IsNewer(string remote, string local)
        {
            return System.Version.TryParse(remote, out Version r)
                   && System.Version.TryParse(local, out Version l)
                   && r > l;
        }
    }
}
