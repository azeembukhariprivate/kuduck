using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ImageMagick;
using Kuduck.Services;
using Kuduck.Tools;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Kuduck.Tests
{
    public static class Program
    {
        private static int _failed;
        private static int _passed;
        private static string _root;

        public static int Main()
        {
            _root = Path.Combine(Path.GetTempPath(), "kuduck-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
            try
            {
                foreach (var m in typeof(Program).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).Where(m => m.Name.StartsWith("Test")))
                {
                    try
                    {
                        m.Invoke(null, null);
                        _passed++;
                        Console.WriteLine("PASS " + m.Name);
                    }
                    catch (TargetInvocationException ex)
                    {
                        _failed++;
                        Console.WriteLine("FAIL " + m.Name + ": " + ex.InnerException);
                    }
                }
            }
            finally
            {
                try { Directory.Delete(_root, true); }
                catch { /* ignore */ }
            }
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        // ================================================================== helpers

        private static void Check(bool condition, string what)
        {
            if (!condition) throw new Exception("Check failed: " + what);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected {expected}, got {actual}");
        }

        private static string Folder(string name)
        {
            var dir = Path.Combine(_root, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>A PDF whose pages have these widths (in points), so each page can be recognised later.</summary>
        private static string MakePdf(string path, params int[] widths)
        {
            using (var doc = new PdfDocument())
            {
                foreach (var w in widths)
                {
                    var page = doc.AddPage();
                    page.Width = XUnit.FromPoint(w);
                    page.Height = XUnit.FromPoint(500);
                    using (var g = XGraphics.FromPdfPage(page)) g.DrawRectangle(XBrushes.SteelBlue, 10, 10, w / 2, 100);
                }
                doc.Save(path);
            }
            return path;
        }

        private static List<int> Widths(string path, string password = null)
        {
            using (var doc = string.IsNullOrEmpty(password) ? PdfReader.Open(path, PdfDocumentOpenMode.Import) : PdfReader.Open(path, password, PdfDocumentOpenMode.Import))
                return doc.Pages.Cast<PdfPage>().Select(p => (int)Math.Round(p.Width.Point)).ToList();
        }

        private static string MakeImage(string path, int width, int height)
        {
            using (var img = new MagickImage(MagickColors.OrangeRed, (uint)width, (uint)height)) img.Write(path);
            return path;
        }

        // ================================================================== PDFs

        private static void TestPageRanges()
        {
            var g = PdfEngine.ParseRanges("1, 3-5, 7-end", 10);
            Equal(3, g.Count, "three parts");
            Check(g[0].SequenceEqual(new[] { 0 }), "page 1");
            Check(g[1].SequenceEqual(new[] { 2, 3, 4 }), "pages 3-5");
            Check(g[2].SequenceEqual(new[] { 6, 7, 8, 9 }), "7 to the end");
            Check(PdfEngine.ParseRanges("2-", 4)[0].SequenceEqual(new[] { 1, 2, 3 }), "open-ended range");
            foreach (var bad in new[] { "", "0", "11", "5-3", "a", "1-2-3", "3-12" })
                Check(PdfEngine.ParseRanges(bad, 10) == null, "rejected: \"" + bad + "\"");
        }

        private static void TestMergeAndSplit()
        {
            var dir = Folder("merge");
            var a = MakePdf(Path.Combine(dir, "a.pdf"), 200, 300, 400);
            var b = MakePdf(Path.Combine(dir, "b.pdf"), 250);

            var merged = PdfEngine.Merge(new[] { a, b }, "together", null);
            Equal(Path.Combine(dir, "together.pdf"), merged, "merged file name");
            Check(Widths(merged).SequenceEqual(new[] { 200, 300, 400, 250 }), "pages in order");
            var again = PdfEngine.Merge(new[] { b, a }, "together", null);
            Equal(Path.Combine(dir, "together (2).pdf"), again, "an existing result isn't overwritten");

            var parts = PdfEngine.Split(a, PdfEngine.ParseRanges("1, 2-3", 3), null);
            Equal(2, parts.Count, "two parts");
            Check(parts[0].EndsWith("a_page1.pdf") && parts[1].EndsWith("a_pages2-3.pdf"), "part names: " + string.Join(", ", parts));
            Check(Widths(parts[0]).SequenceEqual(new[] { 200 }), "part 1");
            Check(Widths(parts[1]).SequenceEqual(new[] { 300, 400 }), "part 2");
            Check(Widths(a).SequenceEqual(new[] { 200, 300, 400 }), "the original is unchanged");
        }

        private static void TestRearrange()
        {
            var dir = Folder("pages");
            var src = MakePdf(Path.Combine(dir, "doc.pdf"), 200, 300, 400);
            var output = PdfEngine.Rearrange(src, null, new[] { new PageEdit { Index = 2, Rotate = 90 }, new PageEdit { Index = 0 } });
            Check(output.EndsWith("doc_edited.pdf"), "result name");
            using (var doc = PdfReader.Open(output, PdfDocumentOpenMode.Import))
            {
                Equal(2, doc.PageCount, "page 2 deleted");
                Equal(400, (int)Math.Round(doc.Pages[0].Width.Point), "page 3 moved first");
                Equal(90, doc.Pages[0].Rotate, "and turned");
                Equal(200, (int)Math.Round(doc.Pages[1].Width.Point), "page 1 second");
            }
        }

        private static void TestLockUsesAes256()
        {
            var dir = Folder("lock");
            var src = MakePdf(Path.Combine(dir, "secret.pdf"), 300, 300);
            var locked = PdfEngine.Lock(src, "p@ss wörd");
            Check(locked.EndsWith("secret_locked.pdf"), "result name");

            var raw = Encoding.ASCII.GetString(File.ReadAllBytes(locked));
            Check(raw.Contains("/AESV3"), "encrypted with AES-256 (AESV3)");
            Check(raw.Contains("/V 5"), "PDF 2.0 encryption (V 5)");

            var refused = false;
            try { PdfEngine.PageCount(locked); }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "opening without the password is refused");
            var wrong = false;
            try { PdfEngine.PageCount(locked, "nope"); }
            catch (InvalidOperationException) { wrong = true; }
            Check(wrong, "a wrong password is refused");
            Equal(2, PdfEngine.PageCount(locked, "p@ss wörd"), "opens with the password");

            var unlocked = PdfEngine.Unlock(locked, "p@ss wörd");
            Equal(2, PdfEngine.PageCount(unlocked), "unlocked copy opens without a password");
        }

        // ================================================================== images

        private static void TestImagesNeverOverwriteOriginals()
        {
            var dir = Folder("images");
            var photo = MakeImage(Path.Combine(dir, "photo.png"), 400, 200);
            var before = File.ReadAllBytes(photo);

            var half = ImageEngine.Process(photo, new ImageJob { Resize = ImageResize.Percent, Percent = 50, Format = "jpg" });
            Equal(Path.Combine(dir, "photo.jpg"), half, "converted name");
            Check(ImageEngine.Dimensions(half).SequenceEqual(new[] { 200, 100 }), "50%");

            var same = ImageEngine.Process(photo, new ImageJob { Resize = ImageResize.Pixels, Width = 100, Height = 100 });
            Equal(Path.Combine(dir, "photo_kuduck.png"), same, "same format gets a suffix");
            Check(ImageEngine.Dimensions(same).SequenceEqual(new[] { 100, 50 }), "fits 100x100, keeping the shape");
            var again = ImageEngine.Process(photo, new ImageJob { Resize = ImageResize.Pixels, Width = 100, Height = 100 });
            Equal(Path.Combine(dir, "photo_kuduck (2).png"), again, "and never replaces an earlier result");

            var stretched = ImageEngine.Process(photo, new ImageJob { Resize = ImageResize.Pixels, Width = 100, Height = 100, KeepAspect = false, Format = "webp" });
            Check(ImageEngine.Dimensions(stretched).SequenceEqual(new[] { 100, 100 }), "exact size when asked");

            var icon = ImageEngine.Process(photo, new ImageJob { Format = "ico" });
            Check(ImageEngine.Dimensions(icon).All(d => d <= 256), "icons are at most 256 px");

            Check(before.SequenceEqual(File.ReadAllBytes(photo)), "the original is untouched");
        }

        private static void TestFileNames()
        {
            Equal(".jpg", ImageEngine.ExtensionFor("keep", "IMG_1.HEIC"), "HEIC is saved as JPG");
            Equal(".png", ImageEngine.ExtensionFor("keep", "a.PNG"), "kept format");
            Equal(".tif", ImageEngine.ExtensionFor("tiff", "a.png"), "TIFF");
            Check(FileHelpers.IsImage("x.WebP") && FileHelpers.IsPdf("y.PDF") && !FileHelpers.IsImage("z.pdf"), "file kinds");
            var dir = Folder("names");
            File.WriteAllText(Path.Combine(dir, "report.pdf"), "x");
            Equal(Path.Combine(dir, "report (2).pdf"), FileHelpers.UniquePath(dir, "report", "pdf"), "unique name");
        }
    }
}
