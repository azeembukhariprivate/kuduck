using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kuduck.Services;

namespace Kuduck.Tools
{
    /// <summary>Runs the bundled document helper for PDF → Word and PDF → Excel.</summary>
    public static class HelperRunner
    {
        public static async Task<string> ConvertAsync(string command, string input, string output, string password)
        {
            if (!File.Exists(AppPaths.HelperPath))
                throw new InvalidOperationException("The document converter is missing. Please reinstall KUDUCK.");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = AppPaths.HelperPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(AppPaths.HelperPath)
            };
            psi.ArgumentList.Add(command);
            psi.ArgumentList.Add(input);
            psi.ArgumentList.Add(output);
            if (!string.IsNullOrEmpty(password)) psi.ArgumentList.Add(password);

            using (Process p = Process.Start(psi))
            {
                if (p == null) throw new InvalidOperationException("Couldn't start the document converter.");
                Task<string> stdout = p.StandardOutput.ReadToEndAsync();
                Task<string> stderr = p.StandardError.ReadToEndAsync();
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                {
                    try { await p.WaitForExitAsync(cts.Token); }
                    catch (OperationCanceledException)
                    {
                        try { p.Kill(true); } catch { }
                        throw new TimeoutException("The conversion took too long and was stopped.");
                    }
                }
                string text = (await stdout).Trim();
                await stderr;
                string last = text.Length == 0 ? "" : text.Substring(text.LastIndexOf('\n') + 1);
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(last))
                    {
                        bool ok = doc.RootElement.GetProperty("ok").GetBoolean();
                        string message = doc.RootElement.GetProperty("message").GetString();
                        if (!ok) throw new InvalidOperationException(message);
                        return message;
                    }
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("The document converter stopped unexpectedly.");
                }
            }
        }
    }

    /// <summary>Word → PDF using Microsoft Word if installed, otherwise LibreOffice if installed.</summary>
    public static class WordConverter
    {
        public static bool WordInstalled => Type.GetTypeFromProgID("Word.Application") != null;

        public static string FindLibreOffice()
        {
            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                string path = Path.Combine(root, "LibreOffice", "program", "soffice.exe");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public static bool Available => WordInstalled || FindLibreOffice() != null;

        public static Task<string> ConvertAsync(string input)
        {
            string output = FileHelpers.OutputPath(input, "", ".pdf");
            if (WordInstalled) return RunOnStaThread(() => { ViaWord(input, output); return output; });
            string soffice = FindLibreOffice();
            if (soffice != null) return Task.Run(() => ViaLibreOffice(soffice, input, output));
            throw new InvalidOperationException(
                "Word → PDF needs Microsoft Word or the free LibreOffice installed on this PC.");
        }

        private static void ViaWord(string input, string output)
        {
            Type t = Type.GetTypeFromProgID("Word.Application");
            dynamic word = Activator.CreateInstance(t);
            dynamic doc = null;
            try
            {
                word.Visible = false;
                word.DisplayAlerts = 0;
                doc = word.Documents.Open(input, false, true, false);
                doc.ExportAsFixedFormat(output, 17); // 17 = PDF
            }
            finally
            {
                try { if (doc != null) doc.Close(false); } catch { }
                try { word.Quit(false); } catch { }
            }
        }

        private static string ViaLibreOffice(string soffice, string input, string output)
        {
            string tempDir = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = soffice,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("--headless");
                psi.ArgumentList.Add("--convert-to");
                psi.ArgumentList.Add("pdf");
                psi.ArgumentList.Add("--outdir");
                psi.ArgumentList.Add(tempDir);
                psi.ArgumentList.Add(input);
                using (Process p = Process.Start(psi))
                {
                    if (p == null || !p.WaitForExit(5 * 60 * 1000))
                        throw new TimeoutException("LibreOffice took too long to convert the file.");
                }
                string produced = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(input) + ".pdf");
                if (!File.Exists(produced)) throw new InvalidOperationException("LibreOffice couldn't convert this file.");
                File.Move(produced, output);
                return output;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static Task<string> RunOnStaThread(Func<string> work)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();
            Thread thread = new Thread(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            return tcs.Task;
        }
    }
}
