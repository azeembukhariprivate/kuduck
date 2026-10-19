using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;
using Kuduck.Services;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Kuduck.Tools
{
    public class PageEdit
    {
        /// <summary>0-based page index in the original file.</summary>
        public int Index { get; set; }
        /// <summary>Extra clockwise rotation in degrees (0, 90, 180, 270).</summary>
        public int Rotate { get; set; }
    }

    public static class PdfEngine
    {
        public static PdfDocument OpenForImport(string path, string password = null)
        {
            try
            {
                return string.IsNullOrEmpty(password)
                    ? PdfReader.Open(path, PdfDocumentOpenMode.Import)
                    : PdfReader.Open(path, password, PdfDocumentOpenMode.Import);
            }
            catch (Exception ex) when (ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(password)
                        ? $"\"{Path.GetFileName(path)}\" is password protected. Unlock it first with Lock / Unlock."
                        : "That password isn't right.");
            }
        }

        public static int PageCount(string path, string password = null)
        {
            using (PdfDocument doc = OpenForImport(path, password)) return doc.PageCount;
        }

        private static void Finish(PdfDocument doc)
        {
            doc.Info.Creator = "KUDUCK";
            doc.Options.CompressContentStreams = true;
            doc.Options.NoCompression = false;
        }

        // ------------------------------------------------------------------ merge

        public static string Merge(IReadOnlyList<string> files, string outputName, IProgress<string> progress)
        {
            string dir = FileHelpers.OutputDirFor(files[0]);
            string name = string.IsNullOrWhiteSpace(outputName) ? "merged" : Path.GetFileNameWithoutExtension(outputName.Trim());
            string output = FileHelpers.UniquePath(dir, name, ".pdf");
            using (PdfDocument result = new PdfDocument())
            {
                for (int i = 0; i < files.Count; i++)
                {
                    progress?.Report($"Adding file {i + 1} of {files.Count}…");
                    using (PdfDocument src = OpenForImport(files[i]))
                        for (int p = 0; p < src.PageCount; p++) result.AddPage(src.Pages[p]);
                }
                Finish(result);
                result.Save(output);
            }
            return output;
        }

        // ------------------------------------------------------------------ split

        /// <summary>Parses "1-3, 5, 8-10" into 0-based page groups. Returns null when invalid.</summary>
        public static List<List<int>> ParseRanges(string text, int pageCount)
        {
            List<List<int>> groups = new List<List<int>>();
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (string raw in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                int a, b;
                if (part.Contains("-"))
                {
                    string[] ab = part.Split('-');
                    if (ab.Length != 2) return null;
                    string left = ab[0].Trim(), right = ab[1].Trim();
                    if (!int.TryParse(left, out a)) return null;
                    if (right.Length == 0 || right.Equals("end", StringComparison.OrdinalIgnoreCase)) b = pageCount;
                    else if (!int.TryParse(right, out b)) return null;
                }
                else
                {
                    if (!int.TryParse(part, out a)) return null;
                    b = a;
                }
                if (a < 1 || b < a || b > pageCount) return null;
                groups.Add(Enumerable.Range(a - 1, b - a + 1).ToList());
            }
            return groups.Count == 0 ? null : groups;
        }

        public static List<string> Split(string file, List<List<int>> groups, IProgress<string> progress)
        {
            List<string> outputs = new List<string>();
            using (PdfDocument src = OpenForImport(file))
            {
                for (int g = 0; g < groups.Count; g++)
                {
                    progress?.Report($"Saving part {g + 1} of {groups.Count}…");
                    List<int> pages = groups[g];
                    string label = pages.Count == 1 ? $"_page{pages[0] + 1}" : $"_pages{pages[0] + 1}-{pages[pages.Count - 1] + 1}";
                    string output = FileHelpers.OutputPath(file, label, ".pdf");
                    using (PdfDocument part = new PdfDocument())
                    {
                        foreach (int p in pages) part.AddPage(src.Pages[p]);
                        Finish(part);
                        part.Save(output);
                    }
                    outputs.Add(output);
                }
            }
            return outputs;
        }

        // ------------------------------------------------------------------ pages (reorder / rotate / delete)

        public static string Rearrange(string file, string password, IReadOnlyList<PageEdit> pages)
        {
            string output = FileHelpers.OutputPath(file, "_edited", ".pdf");
            using (PdfDocument src = OpenForImport(file, password))
            using (PdfDocument result = new PdfDocument())
            {
                foreach (PageEdit edit in pages)
                {
                    PdfPage added = result.AddPage(src.Pages[edit.Index]);
                    if (edit.Rotate % 360 != 0)
                        added.Rotate = ((added.Rotate + edit.Rotate) % 360 + 360) % 360;
                }
                Finish(result);
                result.Save(output);
            }
            return output;
        }

        // ------------------------------------------------------------------ lock / unlock

        public static string Lock(string file, string password)
        {
            string output = FileHelpers.OutputPath(file, "_locked", ".pdf");
            using (PdfDocument src = OpenForImport(file))
            using (PdfDocument result = new PdfDocument())
            {
                for (int p = 0; p < src.PageCount; p++) result.AddPage(src.Pages[p]);
                Finish(result);
                result.SecuritySettings.UserPassword = password;
                result.SecuritySettings.OwnerPassword = password;
                result.Save(output);
            }
            return output;
        }

        public static string Unlock(string file, string password)
        {
            string output = FileHelpers.OutputPath(file, "_unlocked", ".pdf");
            using (PdfDocument src = OpenForImport(file, password))
            using (PdfDocument result = new PdfDocument())
            {
                for (int p = 0; p < src.PageCount; p++) result.AddPage(src.Pages[p]);
                Finish(result);
                result.Save(output);
            }
            return output;
        }

        // ------------------------------------------------------------------ compress

        /// <summary>
        /// Shrinks a PDF by re-saving its photos (JPEG images) at a lower quality and size,
        /// and compressing everything else. Text and vector graphics stay sharp.
        /// </summary>
        public static string Compress(string file, int quality, int maxImageSide, IProgress<string> progress)
        {
            string output = FileHelpers.OutputPath(file, "_compressed", ".pdf");
            PdfDocument doc;
            try
            {
                doc = PdfReader.Open(file, PdfDocumentOpenMode.Modify);
            }
            catch (Exception ex) when (ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException($"\"{Path.GetFileName(file)}\" is password protected. Unlock it first.");
            }
            using (doc)
            {
                PdfObject[] all = doc.Internals.GetAllObjects();
                int done = 0;
                foreach (PdfObject obj in all)
                {
                    PdfDictionary dict = obj as PdfDictionary;
                    if (dict == null || dict.Stream == null) continue;
                    if (dict.Elements.GetName("/Subtype") != "/Image") continue;
                    PdfItem filter = dict.Elements["/Filter"];
                    bool isJpeg = filter is PdfName name && name.Value == "/DCTDecode";
                    if (!isJpeg) continue;
                    done++;
                    if (done % 5 == 0) progress?.Report($"Shrinking images… {done}");
                    try
                    {
                        byte[] original = dict.Stream.Value;
                        using (MagickImage img = new MagickImage(original))
                        {
                            ImageMagick.ColorSpace cs = img.ColorSpace;
                            if (cs != ImageMagick.ColorSpace.sRGB && cs != ImageMagick.ColorSpace.Gray && cs != ImageMagick.ColorSpace.RGB) continue;
                            if (img.Width > maxImageSide || img.Height > maxImageSide)
                                img.Resize((uint)maxImageSide, (uint)maxImageSide);
                            img.Strip();
                            img.Quality = (uint)quality;
                            img.Format = MagickFormat.Jpeg;
                            byte[] smaller = img.ToByteArray();
                            if (smaller.Length < original.Length * 0.9)
                            {
                                dict.Stream.Value = smaller;
                                dict.Elements.SetInteger("/Width", (int)img.Width);
                                dict.Elements.SetInteger("/Height", (int)img.Height);
                                dict.Elements.SetInteger("/Length", smaller.Length);
                            }
                        }
                    }
                    catch
                    {
                        // Leave any image we can't read untouched.
                    }
                }
                Finish(doc);
                doc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;
                doc.Save(output);
            }
            return output;
        }

        // ------------------------------------------------------------------ images to PDF

        /// <param name="pageSize">a4, letter or fit (page = image size)</param>
        /// <param name="marginMm">blank border around each image</param>
        public static string ImagesToPdf(IReadOnlyList<string> images, string outputName, string pageSize, double marginMm,
                                         IProgress<string> progress)
        {
            string dir = FileHelpers.OutputDirFor(images[0]);
            string name = string.IsNullOrWhiteSpace(outputName) ? "images" : Path.GetFileNameWithoutExtension(outputName.Trim());
            string output = FileHelpers.UniquePath(dir, name, ".pdf");
            List<string> temps = new List<string>();
            try
            {
                using (PdfDocument doc = new PdfDocument())
                {
                    for (int i = 0; i < images.Count; i++)
                    {
                        progress?.Report($"Adding image {i + 1} of {images.Count}…");
                        // Normalise every picture to JPG/PNG so PDF can embed it (and fix rotation).
                        string tmp;
                        double pxW, pxH;
                        using (MagickImage img = ImageEngine.Load(images[i]))
                        {
                            bool alpha = img.HasAlpha;
                            tmp = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N") + (alpha ? ".png" : ".jpg"));
                            img.Strip();
                            img.Quality = 92;
                            img.Format = alpha ? MagickFormat.Png : MagickFormat.Jpeg;
                            img.Write(tmp);
                            pxW = img.Width;
                            pxH = img.Height;
                        }
                        temps.Add(tmp);

                        double margin = marginMm / 25.4 * 72.0;
                        double pageW, pageH; // in points (1/72 inch)
                        if (pageSize == "fit")
                        {
                            // 1 image pixel = 1/150 inch keeps photos a sensible size on screen and paper.
                            pageW = pxW / 150.0 * 72.0 + 2 * margin;
                            pageH = pxH / 150.0 * 72.0 + 2 * margin;
                        }
                        else
                        {
                            double shortSide = pageSize == "letter" ? 612 : 595.28;
                            double longSide = pageSize == "letter" ? 792 : 841.89;
                            bool landscape = pxW > pxH;
                            pageW = landscape ? longSide : shortSide;
                            pageH = landscape ? shortSide : longSide;
                        }
                        PdfPage page = doc.AddPage();
                        page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(pageW, pageH));

                        double boxW = pageW - 2 * margin, boxH = pageH - 2 * margin;
                        double scale = Math.Min(boxW / pxW, boxH / pxH);
                        double drawW = pxW * scale, drawH = pxH * scale;
                        using (XGraphics gfx = XGraphics.FromPdfPage(page))
                        using (XImage ximg = XImage.FromFile(tmp))
                        {
                            gfx.DrawImage(ximg, (pageW - drawW) / 2, (pageH - drawH) / 2, drawW, drawH);
                        }
                    }
                    Finish(doc);
                    doc.Save(output);
                }
            }
            finally
            {
                foreach (string t in temps) { try { File.Delete(t); } catch { } }
            }
            return output;
        }
    }
}
