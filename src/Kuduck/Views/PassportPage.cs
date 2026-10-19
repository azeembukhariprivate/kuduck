using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageMagick;
using Kuduck.Controls;
using Kuduck.Services;
using Kuduck.Tools;

namespace Kuduck.Views
{
    public class PassportPage : ToolPage
    {
        private readonly ChipGroup _country;
        private readonly TextBox _customW;
        private readonly TextBox _customH;
        private readonly StackPanel _customPanel;
        private readonly ChipGroup _background;
        private readonly ChipGroup _output;
        private readonly Slider _zoom;
        private readonly Slider _vertical;
        private readonly Slider _horizontal;
        private readonly Image _preview;
        private readonly Border _previewFrame;
        private readonly TextBlock _faceStatus;
        private readonly DispatcherTimer _debounce;

        // Cached work for the current photo (loading, face finding and cut-out are slow).
        private string _loadedPath;
        private MagickImage _source;
        private MagickImage _cutout;
        private FaceBox? _face;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public PassportPage()
            : base("passport", "Passport photo", false, "a photo", FileHelpers.ImageFilter, FileHelpers.IsImage,
                   false, false, "Save passport photo")
        {
            Options.Children.Add(Ui.Section("Country / document", "ImageAccentBrush"));
            _country = new ChipGroup(2);
            foreach (PassportSpec spec in PassportEngine.Specs) _country.Add(spec.Key, spec.Name, spec.SizeText);
            _country.Add("custom", "Custom size", "enter mm");
            Options.Children.Add(_country);

            _customW = Ui.NumberBox(35);
            _customH = Ui.NumberBox(45);
            _customPanel = new StackPanel();
            _customPanel.Children.Add(Ui.Row(Ui.Field("Width (mm)", _customW), Ui.Field("Height (mm)", _customH)));
            Options.Children.Add(_customPanel);

            // ---- preview
            Options.Children.Add(Ui.Section("Preview", "ImageAccentBrush"));
            Grid previewRow = new Grid();
            previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _preview = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.HighQuality);
            _previewFrame = new Border
            {
                Width = 130, Height = 167, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Child = _preview, Margin = new Thickness(0, 0, 12, 0), ClipToBounds = true
            };
            _previewFrame.SetResourceReference(Border.BorderBrushProperty, "InputBorderBrush");
            _previewFrame.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            previewRow.Children.Add(_previewFrame);

            StackPanel adjust = new StackPanel();
            _faceStatus = Ui.Hint("Add a photo to see the preview.");
            adjust.Children.Add(_faceStatus);
            _zoom = AddSlider(adjust, "Size", 0.75, 1.35, 1.0);
            _vertical = AddSlider(adjust, "Up / down", -0.2, 0.2, 0);
            _horizontal = AddSlider(adjust, "Left / right", -0.2, 0.2, 0);
            Button reset = new Button { Style = Ui.GetStyle("LinkButton"), Content = "Reset", HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12 };
            reset.Click += (s, e) => { _zoom.Value = 1; _vertical.Value = 0; _horizontal.Value = 0; };
            adjust.Children.Add(reset);
            Grid.SetColumn(adjust, 1);
            previewRow.Children.Add(adjust);
            Options.Children.Add(previewRow);

            Options.Children.Add(Ui.Section("Background", "ImageAccentBrush"));
            _background = new ChipGroup(3).Add("keep", "Keep").Add("white", "White").Add("blue", "Light blue");
            _background.Value = "white";
            Options.Children.Add(_background);

            Options.Children.Add(Ui.Section("Output", "ImageAccentBrush"));
            _output = new ChipGroup(3).Add("digital", "Digital file").Add("4x6", "4×6 in sheet").Add("a4", "A4 sheet");
            Options.Children.Add(_output);
            Options.Children.Add(Ui.Hint("Print sheets fit as many copies as possible, at 300 dpi, with thin cutting lines. " +
                                         "Always check the official requirements before you submit — they can change."));

            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _debounce.Tick += async (s, e) => { _debounce.Stop(); await RefreshPreview(); };

            _country.Changed += (s, e) => { UpdateCustom(); Schedule(); };
            _background.Changed += (s, e) => Schedule();
            _customW.TextChanged += (s, e) => Schedule();
            _customH.TextChanged += (s, e) => Schedule();
            UpdateCustom();
        }

        private Slider AddSlider(StackPanel host, string label, double min, double max, double value)
        {
            host.Children.Add(Ui.Text(label, 11.5, FontWeights.SemiBold, "MutedBrush"));
            Slider s = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 0, 0, 4) };
            s.ValueChanged += (o, e) => Schedule();
            host.Children.Add(s);
            return s;
        }

        private void UpdateCustom()
        {
            if (_customPanel == null) return;
            _customPanel.Visibility = _country.Value == "custom" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Schedule()
        {
            if (_debounce == null) return;
            _debounce.Stop();
            _debounce.Start();
        }

        private PassportSpec CurrentSpec()
        {
            string key = _country.Value;
            if (key == "custom")
            {
                double w = Ui.ReadInt(_customW, 35, 10, 300);
                double h = Ui.ReadInt(_customH, 45, 10, 300);
                return new PassportSpec { Key = "custom", Name = "Custom", WidthMm = w, HeightMm = h, HeadRatio = 0.70, SizeText = $"{w} × {h} mm" };
            }
            foreach (PassportSpec s in PassportEngine.Specs) if (s.Key == key) return s;
            return PassportEngine.Specs[0];
        }

        private PassportAdjust CurrentAdjust() =>
            new PassportAdjust { Zoom = _zoom.Value, OffsetY = _vertical.Value, OffsetX = _horizontal.Value };

        protected override void OnFilesChanged()
        {
            if (_preview == null) return;
            if (Files.Count == 0)
            {
                _preview.Source = null;
                _faceStatus.Text = "Add a photo to see the preview.";
                return;
            }
            Schedule();
        }

        /// <summary>Loads the photo, finds the face and (if needed) cuts out the background — cached per photo.</summary>
        private async Task EnsurePrepared(string path, bool needCutout, IProgress<string> progress)
        {
            if (_loadedPath != path)
            {
                _source?.Dispose();
                _cutout?.Dispose();
                _source = null;
                _cutout = null;
                _face = null;
                progress?.Report("Opening photo…");
                MagickImage src = await Task.Run(() =>
                {
                    MagickImage img = ImageEngine.Load(path);
                    // Very large photos slow everything down; 2400 px is plenty for a passport photo.
                    if (img.Width > 2400 || img.Height > 2400) img.Resize(2400, 2400);
                    return img;
                });
                _source = src;
                _loadedPath = path;
                progress?.Report("Finding the face…");
                try { _face = await Task.Run(() => WinRt.FindFaceAsync(src)); }
                catch { _face = null; }
            }
            if (needCutout && _cutout == null)
            {
                progress?.Report("Removing the background…");
                MagickImage src = _source;
                _cutout = await Task.Run(() =>
                {
                    MagickImage copy = (MagickImage)src.Clone();
                    BackgroundRemover.Apply(copy);
                    return copy;
                });
            }
        }

        private MagickColor BackgroundColor() =>
            _background.Value == "blue" ? new MagickColor("#DCEBFF") : MagickColors.White;

        private async Task RefreshPreview()
        {
            if (Files.Count == 0) return;
            if (!await _lock.WaitAsync(0)) { Schedule(); return; }
            try
            {
                string path = Files[0];
                bool needCutout = _background.Value != "keep";
                Progress<string> p = new Progress<string>(t => _faceStatus.Text = t);
                try
                {
                    await EnsurePrepared(path, needCutout && BackgroundRemover.ModelAvailable, p);
                }
                catch (Exception ex)
                {
                    _faceStatus.Text = FileHelpers.Friendly(ex);
                    return;
                }
                PassportSpec spec = CurrentSpec();
                PassportAdjust adj = CurrentAdjust();
                MagickImage basis = needCutout && _cutout != null ? _cutout : _source;
                MagickColor bg = BackgroundColor();
                FaceBox? face = _face;
                BitmapImage bmp = await Task.Run(() =>
                {
                    using (MagickImage composed = PassportEngine.Compose(basis, face, spec, adj, bg, 300))
                        return ToBitmap(composed.ToByteArray(MagickFormat.Png));
                });
                _preview.Source = bmp;
                _previewFrame.Height = 130 * spec.HeightMm / spec.WidthMm;
                string faceText = _face.HasValue ? "Face found and centred." : "No face found — use the sliders to position it.";
                if (needCutout && !BackgroundRemover.ModelAvailable) faceText += " (Background tool not installed.)";
                _faceStatus.Text = faceText;
            }
            finally
            {
                _lock.Release();
            }
        }

        private static BitmapImage ToBitmap(byte[] png)
        {
            BitmapImage img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = new MemoryStream(png);
            img.EndInit();
            img.Freeze();
            return img;
        }

        public override void ApplyPreset(Dictionary<string, string> s)
        {
            _country.Value = Get(s, "country", "in");
            _background.Value = Get(s, "bg", "white");
            _output.Value = Get(s, "output", "digital");
            _customW.Text = Get(s, "cw", _customW.Text);
            _customH.Text = Get(s, "ch", _customH.Text);
            UpdateCustom();
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string path = Files[0];
            bool needCutout = _background.Value != "keep";
            if (needCutout && !BackgroundRemover.ModelAvailable)
                throw new InvalidOperationException("The background tool isn't installed. Choose 'Keep' background, or reinstall KUDUCK.");
            await _lock.WaitAsync();
            try
            {
                await EnsurePrepared(path, needCutout, progress);
            }
            finally
            {
                _lock.Release();
            }
            PassportSpec spec = CurrentSpec();
            PassportAdjust adj = CurrentAdjust();
            MagickImage basis = needCutout ? _cutout : _source;
            MagickColor bg = BackgroundColor();
            FaceBox? face = _face;
            string outputKind = _output.Value ?? "digital";
            progress.Report("Saving…");
            string output = await Task.Run(() =>
            {
                int widthPx = PassportEngine.MmToPx(spec.WidthMm);
                using (MagickImage photo = PassportEngine.Compose(basis, face, spec, adj, bg, widthPx))
                {
                    string label = spec.Key == "custom" ? "custom" : spec.Key;
                    if (outputKind == "digital")
                    {
                        string o = FileHelpers.OutputPath(path, $"_passport_{label}", ".jpg");
                        ImageEngine.Save(photo, o, 95, true);
                        return o;
                    }
                    using (MagickImage sheet = PassportEngine.Sheet(photo, outputKind))
                    {
                        string o = FileHelpers.OutputPath(path, $"_passport_{label}_{outputKind}", ".jpg");
                        ImageEngine.Save(sheet, o, 95, true);
                        return o;
                    }
                }
            });
            ToolResult result = new ToolResult { Outputs = { output } };
            result.Message = $"Saved {Path.GetFileName(output)} ({spec.SizeText}, 300 dpi).";
            result.Summary = $"You made a {spec.Name} photo ({spec.SizeText}).";
            result.Settings["country"] = _country.Value;
            result.Settings["bg"] = _background.Value;
            result.Settings["output"] = outputKind;
            result.Settings["cw"] = _customW.Text;
            result.Settings["ch"] = _customH.Text;
            return result;
        }
    }
}
