using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kuduck.Services
{
    public static class FileHelpers
    {
        public static readonly string[] ImageExtensions =
            { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif" };

        public static bool IsImage(string path) =>
            ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        public static bool IsPdf(string path) =>
            Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        public static bool IsWord(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".doc" || ext == ".docx" || ext == ".rtf" || ext == ".odt";
        }

        public const string ImageFilter =
            "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.heic;*.heif;*.avif|All files|*.*";
        public const string PdfFilter = "PDF files|*.pdf";
        public const string WordFilter = "Word documents|*.doc;*.docx;*.rtf;*.odt";

        /// <summary>Folder where results go: next to the original, or the folder chosen in Settings.</summary>
        public static string OutputDirFor(string sourceFile)
        {
            AppSettings s = SettingsStore.Current;
            if (!s.SaveNextToOriginal && !string.IsNullOrWhiteSpace(s.OutputFolder) && Directory.Exists(s.OutputFolder))
                return s.OutputFolder;
            string dir = Path.GetDirectoryName(sourceFile);
            return string.IsNullOrEmpty(dir) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : dir;
        }

        /// <summary>Returns a path that does not exist yet, adding " (2)", " (3)"… when needed.</summary>
        public static string UniquePath(string dir, string baseName, string extension)
        {
            if (!extension.StartsWith(".")) extension = "." + extension;
            string path = Path.Combine(dir, baseName + extension);
            int n = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, $"{baseName} ({n}){extension}");
                n++;
            }
            return path;
        }

        public static string OutputPath(string source, string suffix, string extension) =>
            UniquePath(OutputDirFor(source), Path.GetFileNameWithoutExtension(source) + suffix, extension);

        public static bool IsPermissionProblem(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e is UnauthorizedAccessException) return true;
            return false;
        }

        /// <summary>A short, friendly error message for the user.</summary>
        public static string Friendly(Exception ex)
        {
            if (IsPermissionProblem(ex))
                return "KUDUCK doesn't have permission to save there. Choose another folder in Settings, " +
                       "or close KUDUCK and run it as administrator (right-click → Run as administrator).";
            if (ex is IOException && ex.Message.Contains("being used by another process"))
                return "That file is open in another program. Close it and try again.";
            Exception inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }

        public static string SizeText(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        public static IEnumerable<string> ExpandDropped(IEnumerable<string> paths)
        {
            foreach (string p in paths)
            {
                if (Directory.Exists(p))
                {
                    foreach (string f in Directory.EnumerateFiles(p).OrderBy(x => x))
                        yield return f;
                }
                else if (File.Exists(p))
                {
                    yield return p;
                }
            }
        }
    }
}
