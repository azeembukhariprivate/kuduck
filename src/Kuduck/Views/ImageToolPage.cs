using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kuduck.Controls;
using Kuduck.Services;
using Kuduck.Tools;

namespace Kuduck.Views
{
    /// <summary>Resize, convert and compress images (one page, three starting points).</summary>
    public class ImageToolPage : ToolPage
    {
        private readonly ChipGroup _resizeMode;
        private readonly StackPanel _pixelsPanel;
        private readonly StackPanel _percentPanel;
        private readonly StackPanel _presetPanel;
        private readonly TextBox _width;
        private readonly TextBox _height;
        private readonly CheckBox _keepAspect;
        private readonly TextBox _percent;
        private readonly ChipGroup _preset;
        private readonly ChipGroup _format;
        private readonly Slider _quality;
        private readonly TextBlock _qualityText;
        private readonly CheckBox _strip;

        private static readonly Dictionary<string, int[]> Presets = new Dictionary<string, int[]>
        {
            { "fhd", new[] { 1920, 1080 } },
            { "hd", new[] { 1280, 720 } },
            { "square", new[] { 1080, 1080 } },
            { "story", new[] { 1080, 1920 } },
            { "email", new[] { 1024, 1024 } },
            { "thumb", new[] { 400, 400 } },
        };

        public ImageToolPage(string toolKey)
            : base(toolKey, TitleFor(toolKey), false, "images", FileHelpers.ImageFilter, FileHelpers.IsImage,
                   true, false, VerbFor(toolKey))
        {
            // ---- size
            Options.Children.Add(Ui.Section("Size", "ImageAccentBrush"));
            _resizeMode = new ChipGroup(4).Add("none", "Keep size").Add("px", "Pixels").Add("pct", "Percent").Add("preset", "Presets");
            Options.Children.Add(_resizeMode);

            _width = Ui.NumberBox(1080);
            _height = Ui.NumberBox(1080);
            _keepAspect = new CheckBox { Content = "Keep proportions (fit inside this size)", IsChecked = true, Margin = new Thickness(0, 8, 0, 0) };
            _pixelsPanel = new StackPanel();
            _pixelsPanel.Children.Add(Ui.Row(Ui.Field("Width (px)", _width), Ui.Field("Height (px)", _height)));
            _pixelsPanel.Children.Add(_keepAspect);
            Options.Children.Add(_pixelsPanel);

            _percent = Ui.NumberBox(50);
            _percentPanel = Ui.Field("Percent of original size", _percent);
            Options.Children.Add(_percentPanel);

            _preset = new ChipGroup(2)
                .Add("fhd", "Full HD", "1920 × 1080")
                .Add("hd", "HD", "1280 × 720")
                .Add("square", "Square post", "1080 × 1080")
                .Add("story", "Story / reel", "1080 × 1920")
                .Add("email", "Email friendly", "max 1024 px")
                .Add("thumb", "Thumbnail", "max 400 px");
            _presetPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            _presetPanel.Children.Add(_preset);
            Options.Children.Add(_presetPanel);

            // ---- format
            Options.Children.Add(Ui.Section("Save as", "ImageAccentBrush"));
            _format = new ChipGroup(4).Add("keep", "Same").Add("jpg", "JPG").Add("png", "PNG").Add("webp", "WEBP")
                                      .Add("bmp", "BMP").Add("ico", "ICO").Add("gif", "GIF").Add("tiff", "TIFF");
            Options.Children.Add(_format);

            // ---- quality
            Grid qRow = new Grid { Margin = new Thickness(0, 10, 0, 4) };
            qRow.Children.Add(Ui.Text("Quality", 12, FontWeights.SemiBold, "MutedBrush"));
            _qualityText = Ui.Text("85%", 12, FontWeights.SemiBold, "MutedBrush");
            _qualityText.HorizontalAlignment = HorizontalAlignment.Right;
            qRow.Children.Add(_qualityText);
            Options.Children.Add(qRow);
            _quality = new Slider { Minimum = 10, Maximum = 100, Value = 85, IsSnapToTickEnabled = true, TickFrequency = 5 };
            _quality.ValueChanged += (s, e) => _qualityText.Text = (int)_quality.Value + "%";
            Options.Children.Add(_quality);
            Options.Children.Add(Ui.Hint("Lower quality = smaller file. Applies to JPG and WEBP."));

            _strip = new CheckBox { Content = "Remove hidden info (location, camera details)", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
            Options.Children.Add(_strip);

            _resizeMode.Changed += (s, e) => UpdatePanels();

            switch (toolKey)
            {
                case "resize": _resizeMode.Value = "px"; break;
                case "convert": _format.Value = "jpg"; break;
                case "compress": _quality.Value = 70; _resizeMode.Value = "none"; break;
            }
            UpdatePanels();
        }

        private static string TitleFor(string key) =>
            key == "convert" ? "Convert images" : key == "compress" ? "Compress images" : "Resize images";

        private static string VerbFor(string key) =>
            key == "convert" ? "Convert & save" : key == "compress" ? "Compress & save" : "Resize & save";

        private void UpdatePanels()
        {
            if (_pixelsPanel == null) return;
            string mode = _resizeMode.Value;
            _pixelsPanel.Visibility = mode == "px" ? Visibility.Visible : Visibility.Collapsed;
            _percentPanel.Visibility = mode == "pct" ? Visibility.Visible : Visibility.Collapsed;
            _presetPanel.Visibility = mode == "preset" ? Visibility.Visible : Visibility.Collapsed;
        }

        public override void ApplyPreset(Dictionary<string, string> s)
        {
            _resizeMode.Value = Get(s, "mode", _resizeMode.Value);
            _width.Text = Get(s, "w", _width.Text);
            _height.Text = Get(s, "h", _height.Text);
            _keepAspect.IsChecked = Get(s, "keep", "1") == "1";
            _percent.Text = Get(s, "pct", _percent.Text);
            _preset.Value = Get(s, "preset", _preset.Value);
            _format.Value = Get(s, "format", _format.Value);
            if (double.TryParse(Get(s, "q", ""), out double q)) _quality.Value = q;
            UpdatePanels();
        }

        private ImageJob BuildJob()
        {
            ImageJob job = new ImageJob
            {
                Format = _format.Value ?? "keep",
                Quality = (int)_quality.Value,
                StripMetadata = _strip.IsChecked == true
            };
            switch (_resizeMode.Value)
            {
                case "px":
                    job.Resize = ImageResize.Pixels;
                    job.Width = Ui.ReadInt(_width, 1080);
                    job.Height = Ui.ReadInt(_height, 1080);
                    job.KeepAspect = _keepAspect.IsChecked == true;
                    break;
                case "pct":
                    job.Resize = ImageResize.Percent;
                    job.Percent = Ui.ReadInt(_percent, 50, 1, 1000);
                    break;
                case "preset":
                    int[] wh = Presets[_preset.Value ?? "fhd"];
                    job.Resize = ImageResize.Pixels;
                    job.Width = wh[0];
                    job.Height = wh[1];
                    job.KeepAspect = true;
                    break;
            }
            return job;
        }

        private string Describe(ImageJob job, int count)
        {
            string what = Plural(count, "image");
            string fmt = job.Format == "keep" ? "" : $" ({job.Format.ToUpperInvariant()})";
            switch (job.Resize)
            {
                case ImageResize.Pixels:
                    return $"You resized {what} to {job.Width} × {job.Height} px{fmt}.";
                case ImageResize.Percent:
                    return $"You resized {what} to {job.Percent}%{fmt}.";
                default:
                    return job.Format != "keep"
                        ? $"You converted {what} to {job.Format.ToUpperInvariant()}."
                        : $"You compressed {what} at {job.Quality}% quality.";
            }
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            ImageJob job = BuildJob();
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            int failed = 0;
            string firstError = null;
            await Task.Run(() =>
            {
                for (int i = 0; i < files.Count; i++)
                {
                    progress.Report($"Saving {i + 1} of {files.Count}…");
                    try
                    {
                        result.Outputs.Add(ImageEngine.Process(files[i], job));
                    }
                    catch (Exception ex)
                    {
                        if (FileHelpers.IsPermissionProblem(ex)) throw;
                        failed++;
                        if (firstError == null) firstError = FileHelpers.Friendly(ex);
                    }
                }
            });
            if (result.Outputs.Count == 0) throw new InvalidOperationException(firstError ?? "Nothing was saved.");
            result.Message = failed == 0
                ? $"Saved {Plural(result.Outputs.Count, "image")}."
                : $"Saved {result.Outputs.Count}, {failed} failed: {firstError}";
            result.Summary = Describe(job, result.Outputs.Count);
            result.Settings = new Dictionary<string, string>
            {
                { "mode", _resizeMode.Value }, { "w", _width.Text }, { "h", _height.Text },
                { "keep", _keepAspect.IsChecked == true ? "1" : "0" }, { "pct", _percent.Text },
                { "preset", _preset.Value }, { "format", _format.Value }, { "q", ((int)_quality.Value).ToString() }
            };
            return result;
        }
    }
}
