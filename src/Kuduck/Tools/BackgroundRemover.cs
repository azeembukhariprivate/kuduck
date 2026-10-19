using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImageMagick;
using Kuduck.Services;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Kuduck.Tools
{
    /// <summary>Removes image backgrounds with a small U²-Net model that runs locally (offline).</summary>
    public static class BackgroundRemover
    {
        private const int Size = 320;
        private static readonly object Gate = new object();
        private static InferenceSession _session;

        public static bool ModelAvailable => File.Exists(AppPaths.ModelPath);

        private static InferenceSession Session
        {
            get
            {
                if (_session == null)
                {
                    if (!ModelAvailable)
                        throw new InvalidOperationException("The background-removal model is missing. Please reinstall KUDUCK.");
                    SessionOptions options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
                    _session = new InferenceSession(AppPaths.ModelPath, options);
                }
                return _session;
            }
        }

        /// <summary>Returns a grayscale mask (white = keep) the same size as <paramref name="image"/>.</summary>
        public static MagickImage CreateMask(MagickImage image)
        {
            float[] mean = { 0.485f, 0.456f, 0.406f };
            float[] std = { 0.229f, 0.224f, 0.225f };

            DenseTensor<float> input = new DenseTensor<float>(new[] { 1, 3, Size, Size });
            using (MagickImage small = (MagickImage)image.Clone())
            {
                if (small.HasAlpha)
                {
                    small.BackgroundColor = MagickColors.White;
                    small.Alpha(AlphaOption.Remove);
                }
                small.Resize(new MagickGeometry((uint)Size, (uint)Size) { IgnoreAspectRatio = true });
                byte[] rgb = small.GetPixels().ToByteArray("RGB");
                float max = 1f;
                for (int i = 0; i < rgb.Length; i++) if (rgb[i] > max) max = rgb[i];
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        int p = (y * Size + x) * 3;
                        for (int c = 0; c < 3; c++)
                            input[0, c, y, x] = (rgb[p + c] / max - mean[c]) / std[c];
                    }
                }
            }

            float[] pred;
            lock (Gate)
            {
                string inputName = Session.InputMetadata.Keys.First();
                List<NamedOnnxValue> inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, input) };
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = Session.Run(inputs))
                {
                    pred = results.First().AsTensor<float>().ToArray();
                }
            }

            float lo = pred.Min(), hi = pred.Max();
            float range = Math.Max(1e-6f, hi - lo);
            byte[] maskBytes = new byte[Size * Size * 3];
            for (int i = 0; i < Size * Size; i++)
            {
                byte v = (byte)Math.Round(Math.Max(0, Math.Min(1, (pred[i] - lo) / range)) * 255);
                maskBytes[i * 3] = v;
                maskBytes[i * 3 + 1] = v;
                maskBytes[i * 3 + 2] = v;
            }
            PixelReadSettings settings = new PixelReadSettings((uint)Size, (uint)Size, StorageType.Char, PixelMapping.RGB);
            MagickImage mask = new MagickImage(maskBytes, settings);
            mask.Resize(new MagickGeometry(image.Width, image.Height) { IgnoreAspectRatio = true });
            return mask;
        }

        /// <summary>Makes the background of <paramref name="image"/> transparent (in place).</summary>
        public static void Apply(MagickImage image)
        {
            using (MagickImage mask = CreateMask(image))
            {
                image.Alpha(AlphaOption.Set);
                image.Composite(mask, CompositeOperator.CopyAlpha);
            }
        }

        /// <summary>Puts the cut-out on a solid colour (in place).</summary>
        public static void Flatten(MagickImage image, MagickColor color)
        {
            image.BackgroundColor = color;
            image.Alpha(AlphaOption.Remove);
        }
    }
}
