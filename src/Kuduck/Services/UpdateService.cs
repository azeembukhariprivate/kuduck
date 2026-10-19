using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Kuduck.Services
{
    /// <summary>
    /// Keeps track of new versions: checks shortly after start-up and again once a day while
    /// KUDUCK keeps running, and can download and run the new installer in one click.
    /// </summary>
    public static class UpdateService
    {
        private static DispatcherTimer _timer;
        private static bool _checking;

        public static UpdateInfo Available { get; private set; }
        public static bool Installing { get; private set; }

        /// <summary>Raised (on the UI thread) when a newer version is found.</summary>
        public static event EventHandler AvailableChanged;

        public static void Start()
        {
            // First check 20 seconds after start-up, then look every hour whether a day has passed.
            DispatcherTimer first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            first.Tick += async (s, e) => { first.Stop(); await CheckAsync(false); };
            first.Start();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _timer.Tick += async (s, e) => await CheckAsync(false);
            _timer.Start();
        }

        /// <summary>Checks GitHub. <paramref name="force"/> = ignore the once-a-day limit and the setting.</summary>
        public static async Task<UpdateInfo> CheckAsync(bool force)
        {
            AppSettings s = SettingsStore.Current;
            if (_checking) return Available;
            if (!force)
            {
                if (!s.CheckForUpdates) return Available;
                if ((DateTime.Now - s.LastUpdateCheck).TotalHours < 23) return Available;
            }
            _checking = true;
            try
            {
                UpdateInfo info = await UpdateChecker.CheckAsync();
                s.LastUpdateCheck = DateTime.Now;
                SettingsStore.Save();
                if (info != null && (Available == null || Available.Version != info.Version))
                {
                    Available = info;
                    AvailableChanged?.Invoke(null, EventArgs.Empty);
                }
                return Available;
            }
            finally
            {
                _checking = false;
            }
        }

        /// <summary>
        /// Downloads the new installer, checks it, starts it and closes KUDUCK so it can be replaced.
        /// Falls back to opening the release page when the release has no installer attached.
        /// </summary>
        public static async Task InstallAsync(IProgress<string> progress)
        {
            UpdateInfo info = Available;
            if (info == null || Installing) return;
            if (string.IsNullOrEmpty(info.InstallerUrl))
            {
                AppPaths.OpenUrl(info.Url);
                return;
            }
            Installing = true;
            try
            {
                string dir = Path.Combine(AppPaths.TempDir, "update");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, Path.GetFileName(info.InstallerName));
                await DownloadAsync(info, file, progress);

                progress?.Report("Checking the download…");
                if (!string.IsNullOrEmpty(info.Sha256))
                {
                    string actual = await Task.Run(() => HashOf(file));
                    if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); } catch { }
                        throw new InvalidOperationException("The download didn't match the release. Please try again later.");
                    }
                }

                progress?.Report("Starting the installer…");
                // Silent per-user install: no admin prompt, KUDUCK restarts by itself when done.
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                    UseShellExecute = true
                };
                Process.Start(psi);
                App.Current.Quit();
            }
            finally
            {
                Installing = false;
            }
        }

        private static async Task DownloadAsync(UpdateInfo info, string file, IProgress<string> progress)
        {
            using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("KUDUCK/" + AppPaths.Version);
                using (HttpResponseMessage response = await http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? info.InstallerSize;
                    using (Stream input = await response.Content.ReadAsStreamAsync())
                    using (FileStream output = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long done = 0;
                        int lastPercent = -1;
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read);
                            done += read;
                            if (total > 0)
                            {
                                int percent = (int)(done * 100 / total);
                                if (percent != lastPercent)
                                {
                                    lastPercent = percent;
                                    progress?.Report($"Downloading update… {percent}%");
                                }
                            }
                        }
                    }
                }
            }
        }

        private static string HashOf(string file)
        {
            using (FileStream fs = File.OpenRead(file))
            using (SHA256 sha = SHA256.Create())
                return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
    }
}
