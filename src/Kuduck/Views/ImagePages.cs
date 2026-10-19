using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImageMagick;
using Kuduck.Controls;
using Kuduck.Services;
using Kuduck.Tools;

namespace Kuduck.Views
{
    /// <summary>Cut out the subject of a photo.</summary>
    public class RemoveBgPage : ToolPage
    {
        private readonly ChipGroup _background;

        public RemoveBgPage()
            : base("removebg", "Remove background", false, "images", FileHelpers.ImageFilter, FileHelpers.IsImage,
                   true, false, "Remove background")
        {
            Options.Children.Add(Ui.Section("New background", "ImageAccentBrush"));
            _background = new ChipGroup(2)
                .Add("none", "Transparent", "saved as PNG")
                .Add("white", "White", "saved as JPG")
                .Add("blue", "Light blue", "saved as JPG")
                .Add("grey", "Light grey", "saved as JPG");
            Options.Children.Add(_background);
            Options.Children.Add(Ui.Hint("Works best on people, products and pets with a clear outline. Runs on your PC — nothing is uploaded."));
        }

        public override void ApplyPreset(Dictionary<string, string> s) => _background.Value = Get(s, "bg", "none");

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string bg = _background.Value ?? "none";
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            await Task.Run(() =>
            {
                for (int i = 0; i < files.Count; i++)
                {
                    progress.Report($"Cutting out {i + 1} of {files.Count}…");
                    using (MagickImage img = ImageEngine.Load(files[i]))
                    {
                        BackgroundRemover.Apply(img);
                        string output;
                        if (bg == "none")
                        {
                            output = FileHelpers.OutputPath(files[i], "_nobg", ".png");
                        }
                        else
                        {
                            BackgroundRemover.Flatten(img, ColorFor(bg));
                            output = FileHelpers.OutputPath(files[i], "_nobg", ".jpg");
                        }
                        ImageEngine.Save(img, output, 92, false);
                        result.Outputs.Add(output);
                    }
                }
            });
            result.Message = $"Saved {Plural(result.Outputs.Count, "image")}.";
            result.Summary = $"You removed the background from {Plural(result.Outputs.Count, "image")}.";
            result.Settings["bg"] = bg;
            return result;
        }

        public static MagickColor ColorFor(string key)
        {
            switch (key)
            {
                case "blue": return new MagickColor("#DCEBFF");
                case "grey": return new MagickColor("#EDEDED");
                case "red": return new MagickColor("#E03A3A");
                default: return MagickColors.White;
            }
        }
    }

    /// <summary>Put several pictures into one PDF.</summary>
    public class ImagesToPdfPage : ToolPage
    {
        private readonly ChipGroup _pageSize;
        private readonly ChipGroup _margin;
        private readonly TextBox _name;

        public ImagesToPdfPage()
            : base("img2pdf", "Images → PDF", false, "images", FileHelpers.ImageFilter, FileHelpers.IsImage,
                   true, true, "Create PDF")
        {
            Options.Children.Add(Ui.Section("Page size", "ImageAccentBrush"));
            _pageSize = new ChipGroup(3).Add("a4", "A4").Add("letter", "Letter").Add("fit", "Fit image");
            Options.Children.Add(_pageSize);
            Options.Children.Add(Ui.Section("Margin", "ImageAccentBrush"));
            _margin = new ChipGroup(3).Add("0", "None").Add("6", "Small").Add("15", "Normal");
            _margin.Value = "6";
            Options.Children.Add(_margin);
            _name = new TextBox { Text = "images.pdf" };
            Options.Children.Add(Ui.Field("PDF file name", _name));
            Options.Children.Add(Ui.Hint("One picture per page, in the order above. Use the arrows to reorder."));
        }

        public override void ApplyPreset(Dictionary<string, string> s)
        {
            _pageSize.Value = Get(s, "size", "a4");
            _margin.Value = Get(s, "margin", "6");
        }

        protected override string ButtonText(int fileCount) =>
            fileCount > 0 ? $"Create PDF from {Plural(fileCount, "image")}" : "Create PDF";

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string size = _pageSize.Value ?? "a4";
            double margin = double.Parse(_margin.Value ?? "6", System.Globalization.CultureInfo.InvariantCulture);
            string name = _name.Text;
            List<string> files = new List<string>(Files);
            string output = await Task.Run(() => PdfEngine.ImagesToPdf(files, name, size, margin, progress));
            ToolResult result = new ToolResult { Outputs = { output } };
            result.Message = $"Saved {System.IO.Path.GetFileName(output)}.";
            result.Summary = $"You made a PDF from {Plural(files.Count, "image")}.";
            result.Settings["size"] = size;
            result.Settings["margin"] = _margin.Value;
            return result;
        }
    }
}
