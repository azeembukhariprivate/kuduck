using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImageMagick;
using Kuduck.Services;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.FaceAnalysis;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Kuduck.Tools
{
    /// <summary>Face box in the original image's pixels.</summary>
    public struct FaceBox
    {
        public double X, Y, Width, Height;
        public double CenterX => X + Width / 2;
    }

    /// <summary>
    /// Features built into Windows itself (no extra download): face detection,
    /// PDF page rendering and text recognition (OCR).
    /// </summary>
    public static class WinRt
    {
        // ------------------------------------------------------------------ helpers

        private static async Task<SoftwareBitmap> DecodeAsync(IRandomAccessStream stream)
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            SoftwareBitmap bmp = await decoder.GetSoftwareBitmapAsync();
            if (bmp.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || bmp.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                bmp = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            return bmp;
        }

        /// <summary>Writes a scaled copy as PNG to a temp file and decodes it with Windows imaging.</summary>
        private static async Task<SoftwareBitmap> FromMagickAsync(MagickImage image, uint maxSide)
        {
            string tmp = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (MagickImage copy = (MagickImage)image.Clone())
                {
                    if (copy.Width > maxSide || copy.Height > maxSide) copy.Resize(maxSide, maxSide);
                    copy.Format = MagickFormat.Png;
                    copy.Write(tmp);
                }
                StorageFile file = await StorageFile.GetFileFromPathAsync(tmp);
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
                    return await DecodeAsync(stream);
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        // ------------------------------------------------------------------ faces

        /// <summary>Finds the largest face, or null if none (or the feature isn't available).</summary>
        public static async Task<FaceBox?> FindFaceAsync(MagickImage image)
        {
            if (!FaceDetector.IsSupported) return null;
            const uint maxSide = 1200;
            double scale = Math.Max(image.Width, image.Height) > maxSide
                ? (double)Math.Max(image.Width, image.Height) / maxSide
                : 1.0;
            SoftwareBitmap bmp = await FromMagickAsync(image, maxSide);
            SoftwareBitmap gray = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Gray8);
            FaceDetector detector = await FaceDetector.CreateAsync();
            IList<DetectedFace> faces = await detector.DetectFacesAsync(gray);
            if (faces == null || faces.Count == 0) return null;
            DetectedFace best = faces.OrderByDescending(f => (double)f.FaceBox.Width * f.FaceBox.Height).First();
            return new FaceBox
            {
                X = best.FaceBox.X * scale,
                Y = best.FaceBox.Y * scale,
                Width = best.FaceBox.Width * scale,
                Height = best.FaceBox.Height * scale
            };
        }

        // ------------------------------------------------------------------ PDF rendering

        public static async Task<PdfDocument> OpenPdfAsync(string path, string password)
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            return string.IsNullOrEmpty(password)
                ? await PdfDocument.LoadFromFileAsync(file)
                : await PdfDocument.LoadFromFileAsync(file, password);
        }

        /// <summary>Renders one page to PNG bytes, <paramref name="width"/> pixels wide.</summary>
        public static async Task<byte[]> RenderPagePngAsync(PdfDocument doc, uint index, uint width)
        {
            using (PdfPage page = doc.GetPage(index))
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                PdfPageRenderOptions options = new PdfPageRenderOptions
                {
                    DestinationWidth = width,
                    BitmapEncoderId = BitmapEncoder.PngEncoderId
                };
                await page.RenderToStreamAsync(stream, options);
                return await ReadAllAsync(stream);
            }
        }

        private static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
        {
            uint size = (uint)stream.Size;
            byte[] bytes = new byte[size];
            using (DataReader reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync(size);
                reader.ReadBytes(bytes);
            }
            return bytes;
        }

        // ------------------------------------------------------------------ OCR

        private static OcrEngine CreateOcr()
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null)
                throw new InvalidOperationException(
                    "Text recognition needs a Windows language pack with OCR. Add English (or your language) in " +
                    "Windows Settings → Time & language → Language & region.");
            return engine;
        }

        private static string LinesOf(OcrResult result)
        {
            StringBuilder sb = new StringBuilder();
            foreach (OcrLine line in result.Lines) sb.AppendLine(line.Text);
            return sb.ToString();
        }

        public static async Task<string> RecognizeImageAsync(string path)
        {
            OcrEngine engine = CreateOcr();
            using (MagickImage img = ImageEngine.Load(path))
            {
                SoftwareBitmap bmp = await FromMagickAsync(img, OcrEngine.MaxImageDimension);
                OcrResult result = await engine.RecognizeAsync(bmp);
                return LinesOf(result);
            }
        }

        public static async Task<string> RecognizePdfAsync(string path, string password, IProgress<string> progress)
        {
            OcrEngine engine = CreateOcr();
            PdfDocument doc = await OpenPdfAsync(path, password);
            StringBuilder sb = new StringBuilder();
            uint max = Math.Min(2400u, OcrEngine.MaxImageDimension);
            for (uint i = 0; i < doc.PageCount; i++)
            {
                progress?.Report($"Reading page {i + 1} of {doc.PageCount}…");
                using (PdfPage page = doc.GetPage(i))
                using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
                {
                    PdfPageRenderOptions options = new PdfPageRenderOptions();
                    if (page.Size.Width >= page.Size.Height) options.DestinationWidth = max;
                    else options.DestinationHeight = max;
                    await page.RenderToStreamAsync(stream, options);
                    stream.Seek(0);
                    SoftwareBitmap bmp = await DecodeAsync(stream);
                    OcrResult result = await engine.RecognizeAsync(bmp);
                    if (i > 0) sb.AppendLine().AppendLine($"----- Page {i + 1} -----").AppendLine();
                    sb.Append(LinesOf(result));
                }
            }
            return sb.ToString();
        }
    }
}
