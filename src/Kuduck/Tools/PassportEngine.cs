using System;
using System.Collections.Generic;
using ImageMagick;

namespace Kuduck.Tools
{
    public class PassportSpec
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        /// <summary>Head (chin to top of hair) as a share of the photo height.</summary>
        public double HeadRatio { get; set; }
        public string SizeText { get; set; }
    }

    public class PassportAdjust
    {
        /// <summary>1.0 = automatic size. Bigger = closer.</summary>
        public double Zoom { get; set; } = 1.0;
        /// <summary>Shift as a share of the photo size (-0.3 … 0.3).</summary>
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
    }

    public static class PassportEngine
    {
        public const int Dpi = 300;

        /// <summary>Common sizes. Always check the official site before submitting — rules change.</summary>
        public static readonly List<PassportSpec> Specs = new List<PassportSpec>
        {
            new PassportSpec { Key = "in", Name = "India passport", WidthMm = 35, HeightMm = 45, HeadRatio = 0.72, SizeText = "35 × 45 mm" },
            new PassportSpec { Key = "us", Name = "US passport / visa", WidthMm = 50.8, HeightMm = 50.8, HeadRatio = 0.60, SizeText = "2 × 2 in" },
            new PassportSpec { Key = "uk", Name = "UK passport", WidthMm = 35, HeightMm = 45, HeadRatio = 0.70, SizeText = "35 × 45 mm" },
            new PassportSpec { Key = "ae", Name = "UAE visa", WidthMm = 43, HeightMm = 55, HeadRatio = 0.68, SizeText = "43 × 55 mm" },
            new PassportSpec { Key = "in-visa", Name = "India visa / OCI", WidthMm = 50.8, HeightMm = 50.8, HeadRatio = 0.60, SizeText = "2 × 2 in" },
            new PassportSpec { Key = "eu", Name = "Schengen visa", WidthMm = 35, HeightMm = 45, HeadRatio = 0.75, SizeText = "35 × 45 mm" },
            new PassportSpec { Key = "ca", Name = "Canada passport", WidthMm = 50, HeightMm = 70, HeadRatio = 0.48, SizeText = "50 × 70 mm" },
            new PassportSpec { Key = "pk", Name = "Pakistan passport", WidthMm = 35, HeightMm = 45, HeadRatio = 0.72, SizeText = "35 × 45 mm" },
        };

        public static int MmToPx(double mm, int dpi = Dpi) => (int)Math.Round(mm / 25.4 * dpi);

        /// <summary>
        /// Crops <paramref name="source"/> to the passport shape with the head sized and centred.
        /// Without a detected face the crop is centred on the upper middle of the picture.
        /// </summary>
        public static MagickImage Compose(MagickImage source, FaceBox? face, PassportSpec spec, PassportAdjust adjust,
                                          MagickColor background, int outWidth)
        {
            double aspect = spec.WidthMm / spec.HeightMm;
            double cropH, centerX, top;
            if (face.HasValue)
            {
                FaceBox f = face.Value;
                // Windows' face box covers roughly brows to chin; estimate the full head from it.
                double headTop = f.Y - f.Height * 0.45;
                double chin = f.Y + f.Height * 1.08;
                double headH = chin - headTop;
                cropH = headH / spec.HeadRatio;
                centerX = f.CenterX;
                double spare = cropH - headH;
                top = headTop - spare * 0.42;
            }
            else
            {
                cropH = Math.Min(source.Height, source.Width / aspect);
                centerX = source.Width / 2.0;
                top = (source.Height - cropH) / 3.0;
            }

            double zoom = Math.Max(0.5, Math.Min(2.0, adjust.Zoom));
            double newH = cropH / zoom;
            top += (cropH - newH) / 2;
            cropH = newH;
            double cropW = cropH * aspect;
            double left = centerX - cropW / 2 - adjust.OffsetX * cropW;
            top -= adjust.OffsetY * cropH;

            // Pad generously so a crop that runs off the picture is filled with the background colour.
            uint pad = (uint)Math.Ceiling(Math.Max(cropW, cropH));
            MagickImage work = (MagickImage)source.Clone();
            if (work.HasAlpha)
            {
                work.BackgroundColor = background;
                work.Alpha(AlphaOption.Remove);
            }
            work.BorderColor = background;
            work.Border(pad);
            int x = (int)Math.Round(left + pad);
            int y = (int)Math.Round(top + pad);
            work.Crop(new MagickGeometry(x, y, (uint)Math.Max(1, Math.Round(cropW)), (uint)Math.Max(1, Math.Round(cropH))));
            work.ResetPage();

            int outHeight = (int)Math.Round(outWidth / aspect);
            work.Resize(new MagickGeometry((uint)outWidth, (uint)outHeight) { IgnoreAspectRatio = true });
            work.Density = new Density(Dpi, Dpi, DensityUnit.PixelsPerInch);
            return work;
        }

        /// <summary>Lays out as many copies as fit on a sheet (4×6 in or A4) with cutting gaps.</summary>
        public static MagickImage Sheet(MagickImage photo, string sheet)
        {
            int sheetW = sheet == "a4" ? MmToPx(210) : MmToPx(101.6);
            int sheetH = sheet == "a4" ? MmToPx(297) : MmToPx(152.4);
            int gap = MmToPx(3);
            int margin = MmToPx(5);
            int pw = (int)photo.Width, ph = (int)photo.Height;
            int cols = Math.Max(1, (sheetW - 2 * margin + gap) / (pw + gap));
            int rows = Math.Max(1, (sheetH - 2 * margin + gap) / (ph + gap));
            MagickImage canvas = new MagickImage(MagickColors.White, (uint)sheetW, (uint)sheetH);
            int usedW = cols * pw + (cols - 1) * gap;
            int usedH = rows * ph + (rows - 1) * gap;
            int startX = (sheetW - usedW) / 2;
            int startY = (sheetH - usedH) / 2;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int px = startX + c * (pw + gap);
                    int py = startY + r * (ph + gap);
                    canvas.Composite(photo, px, py, CompositeOperator.Over);
                    // Thin grey outline to help cutting.
                    DrawOutline(canvas, px, py, pw, ph);
                }
            }
            canvas.Density = new Density(Dpi, Dpi, DensityUnit.PixelsPerInch);
            return canvas;
        }

        private static void DrawOutline(MagickImage canvas, int x, int y, int w, int h)
        {
            new Drawables()
                .StrokeColor(new MagickColor("#C8C8C8"))
                .StrokeWidth(1)
                .FillColor(MagickColors.Transparent)
                .Rectangle(x - 1, y - 1, x + w, y + h)
                .Draw(canvas);
        }
    }
}
