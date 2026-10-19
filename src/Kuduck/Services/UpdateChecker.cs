using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Kuduck.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string Url { get; set; }
    }

    /// <summary>Checks the GitHub Releases page for a newer version. Never downloads anything by itself.</summary>
    public static class UpdateChecker
    {
        public static async Task<UpdateInfo> CheckAsync()
        {
            try
            {
                using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("KUDUCK/" + AppPaths.Version);
                    string url = $"https://api.github.com/repos/{AppPaths.RepoOwner}/{AppPaths.RepoName}/releases/latest";
                    string json = await http.GetStringAsync(url);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                        string page = doc.RootElement.GetProperty("html_url").GetString();
                        if (IsNewer(tag.TrimStart('v', 'V'), AppPaths.Version))
                            return new UpdateInfo { Version = tag.TrimStart('v', 'V'), Url = page };
                    }
                }
            }
            catch
            {
                // Offline or no releases yet — stay quiet.
            }
            return null;
        }

        private static bool IsNewer(string remote, string local)
        {
            return System.Version.TryParse(remote, out Version r)
                   && System.Version.TryParse(local, out Version l)
                   && r > l;
        }
    }
}
