using System;
using System.Collections.Generic;
using System.Linq;
using Kuduck.Services;

namespace Kuduck.Views
{
    public class ToolInfo
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Icon { get; set; }
        public bool Pdf { get; set; }
        public Func<IToolView> Create { get; set; }
    }

    public static class ToolRegistry
    {
        public static readonly List<ToolInfo> Image = new List<ToolInfo>
        {
            new ToolInfo { Key = "resize", Label = "Resize", Icon = "IcoResize", Create = () => new ImageToolPage("resize") },
            new ToolInfo { Key = "convert", Label = "Convert", Icon = "IcoConvert", Create = () => new ImageToolPage("convert") },
            new ToolInfo { Key = "compress", Label = "Compress", Icon = "IcoCompress", Create = () => new ImageToolPage("compress") },
            new ToolInfo { Key = "passport", Label = "Passport", Icon = "IcoPassport", Create = () => new PassportPage() },
            new ToolInfo { Key = "removebg", Label = "Remove BG", Icon = "IcoScissors", Create = () => new RemoveBgPage() },
            new ToolInfo { Key = "img2pdf", Label = "Images → PDF", Icon = "IcoImages", Create = () => new ImagesToPdfPage() },
        };

        public static readonly List<ToolInfo> Pdf = new List<ToolInfo>
        {
            new ToolInfo { Key = "merge", Label = "Merge", Icon = "IcoMerge", Pdf = true, Create = () => new MergePage() },
            new ToolInfo { Key = "split", Label = "Split", Icon = "IcoSplit", Pdf = true, Create = () => new SplitPage() },
            new ToolInfo { Key = "pdfcompress", Label = "Compress", Icon = "IcoCompress", Pdf = true, Create = () => new PdfCompressPage() },
            new ToolInfo { Key = "pdf2word", Label = "PDF → Word", Icon = "IcoWord", Pdf = true, Create = () => new PdfConvertPage(false) },
            new ToolInfo { Key = "pdf2excel", Label = "PDF → Excel", Icon = "IcoExcel", Pdf = true, Create = () => new PdfConvertPage(true) },
            new ToolInfo { Key = "word2pdf", Label = "Word → PDF", Icon = "IcoToPdf", Pdf = true, Create = () => new WordToPdfPage() },
            new ToolInfo { Key = "pages", Label = "Pages", Icon = "IcoPages", Pdf = true, Create = () => new PagesPage() },
            new ToolInfo { Key = "lock", Label = "Lock / Unlock", Icon = "IcoLock", Pdf = true, Create = () => new LockPage() },
            new ToolInfo { Key = "ocr", Label = "Scan → Text", Icon = "IcoOcr", Pdf = true, Create = () => new OcrPage() },
        };

        public static ToolInfo Find(string key) =>
            Image.Concat(Pdf).FirstOrDefault(t => t.Key == key);

        /// <summary>Picks a sensible tool for files dropped on the duck or the home screen.</summary>
        public static string RouteFor(IReadOnlyList<string> files)
        {
            if (files.Count == 0) return null;
            int pdfs = files.Count(FileHelpers.IsPdf);
            int images = files.Count(FileHelpers.IsImage);
            int words = files.Count(FileHelpers.IsWord);
            if (words > 0 && words >= pdfs && words >= images) return "word2pdf";
            if (pdfs > 0 && pdfs >= images) return pdfs > 1 ? "merge" : "pages";
            if (images > 0) return "resize";
            return null;
        }
    }
}
