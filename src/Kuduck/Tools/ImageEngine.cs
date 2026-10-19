using System;
using System.IO;
using ImageMagick;
using Kuduck.Services;

namespace Kuduck.Tools
{
    public enum ImageResize { None, Pixels, Percent }

    public class ImageJob
    {
        public ImageResize Resize { get; set; } = ImageResize.None;
        public int Width { get; set; } = 1080;
        public int Height { get; set; } = 1080;
        public bool KeepAspect { get; set; } = true;
        public int Percent { get; set; } = 50;
        /// <summary>keep, jpg, png, webp, bmp, ico, gif, tiff</summary>
        public string Format { get; set; } = "keep";
        public int Quality { get; set; } = 85;
        public bool StripMetadata { get; set; } = true;
        public string Suffix { get; set; } = "";
    }

    public static class ImageEngine
    {
        public static string ExtensionFor(string format, string source)
        {
            switch (format)
            {
                case "jpg": return ".jpg";
                case "png": return ".png";
                case "webp": return ".webp";
                case "bmp": return ".bmp";
                case "ico": return ".ico";
                case "gif": return ".gif";
                case "tiff": return ".tif";
                default:
                    string ext = Path.GetExtension(source).ToLowerInvariant();
                    // Formats we read but don't write well get saved as JPG.
                    if (ext == ".heic" || ext == ".heif" || ext == ".avif" || ext == ".jpeg") return ".jpg";
                    return ext;
            }
        }

        public static MagickFormat MagickFormatFor(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".png": return MagickFormat.Png;
                case ".webp": return MagickFormat.WebP;
                case ".bmp": return MagickFormat.Bmp;
                case ".ico": return MagickFormat.Ico;
                case ".gif": return MagickFormat.Gif;
                case ".tif":
                case ".tiff": return MagickFormat.Tiff;
                default: return MagickFormat.Jpeg;
            }
        }

        /// <summary>Opens an image the right way up (respects the camera's rotation flag).</summary>
        public static MagickImage Load(string path)
        {
            MagickImage img = new MagickImage(path);
            img.AutoOrient();
            return img;
        }

        public static string Process(string source, ImageJob job)
        {
            string ext = ExtensionFor(job.Format, source);
            // Never overwrite the original: same format gets a suffix (and UniquePath adds (2), (3)… if needed).
            string suffix = job.Suffix;
            if (string.IsNullOrEmpty(suffix) && ext.Equals(Path.GetExtension(source), StringComparison.OrdinalIgnoreCase))
                suffix = "_kuduck";
            string output = FileHelpers.OutputPath(source, suffix, ext);

            using (MagickImage img = Load(source))
            {
                ApplyResize(img, job);
                if (ext == ".ico")
                {
                    // Icons can't be bigger than 256 px.
                    if (img.Width > 256 || img.Height > 256) img.Resize(256, 256);
                }
                Save(img, output, job.Quality, job.StripMetadata);
            }
            return output;
        }

        public static void ApplyResize(MagickImage img, ImageJob job)
        {
            if (job.Resize == ImageResize.Pixels)
            {
                uint w = (uint)Math.Max(1, job.Width);
                uint h = (uint)Math.Max(1, job.Height);
                MagickGeometry g = new MagickGeometry(w, h) { IgnoreAspectRatio = !job.KeepAspect };
                img.Resize(g);
            }
            else if (job.Resize == ImageResize.Percent)
            {
                img.Resize(new Percentage(Math.Max(1, job.Percent)));
            }
        }

        public static void Save(MagickImage img, string output, int quality, bool strip)
        {
            MagickFormat format = MagickFormatFor(Path.GetExtension(output));
            if (strip) img.Strip();
            if (format == MagickFormat.Jpeg || format == MagickFormat.Bmp)
            {
                // These formats have no transparency: put transparent areas on white.
                if (img.HasAlpha)
                {
                    img.BackgroundColor = MagickColors.White;
                    img.Alpha(AlphaOption.Remove);
                }
            }
            img.Quality = (uint)Math.Max(1, Math.Min(100, quality));
            img.Format = format;
            img.Write(output);
        }

        public static int[] Dimensions(string path)
        {
            try
            {
                MagickImageInfo info = new MagickImageInfo(path);
                return new[] { (int)info.Width, (int)info.Height };
            }
            catch
            {
                return null;
            }
        }
    }
}
