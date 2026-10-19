using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kuduck.Controls;
using Kuduck.Services;
using Kuduck.Tools;

namespace Kuduck.Views
{
    public class MergePage : ToolPage
    {
        private readonly TextBox _name;

        public MergePage()
            : base("merge", "Merge PDFs", true, "PDFs", FileHelpers.PdfFilter, FileHelpers.IsPdf, true, true, "Merge", 2)
        {
            _name = new TextBox { Text = "merged.pdf" };
            Options.Children.Add(Ui.Field("Output file name", _name));
            Options.Children.Add(Ui.Hint("Files are joined top to bottom. Use the arrows to change the order."));
        }

        protected override string ButtonText(int n) => n >= 2 ? $"Merge {n} files" : "Add at least 2 PDFs";

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            List<string> files = new List<string>(Files);
            string name = _name.Text;
            string output = await Task.Run(() => PdfEngine.Merge(files, name, progress));
            return new ToolResult
            {
                Outputs = { output },
                Message = $"Saved {Path.GetFileName(output)}.",
                Summary = $"You merged {files.Count} PDFs into one."
            };
        }
    }

    public class SplitPage : ToolPage
    {
        private readonly ChipGroup _mode;
        private readonly TextBox _ranges;
        private readonly TextBox _every;
        private readonly StackPanel _rangesPanel;
        private readonly StackPanel _everyPanel;
        private readonly TextBlock _info;
        private int _pageCount;

        public SplitPage()
            : base("split", "Split PDF", true, "a PDF", FileHelpers.PdfFilter, FileHelpers.IsPdf, false, false, "Split")
        {
            _info = Ui.Hint("");
            _info.Margin = new Thickness(0, 4, 0, 0);
            Options.Children.Add(_info);
            Options.Children.Add(Ui.Section("How to split", "PdfAccentBrush"));
            _mode = new ChipGroup(3).Add("each", "Every page").Add("ranges", "Page ranges").Add("every", "Every N pages");
            Options.Children.Add(_mode);
            _ranges = new TextBox { Text = "1-3, 4-end" };
            _rangesPanel = Ui.Field("Ranges (one file per range)", _ranges);
            _rangesPanel.Children.Add(Ui.Hint("Example: 1-3, 5, 8-end"));
            Options.Children.Add(_rangesPanel);
            _every = Ui.NumberBox(2);
            _everyPanel = Ui.Field("Pages per file", _every);
            Options.Children.Add(_everyPanel);
            _mode.Changed += (s, e) => UpdateMode();
            UpdateMode();
        }

        private void UpdateMode()
        {
            if (_rangesPanel == null) return;
            _rangesPanel.Visibility = _mode.Value == "ranges" ? Visibility.Visible : Visibility.Collapsed;
            _everyPanel.Visibility = _mode.Value == "every" ? Visibility.Visible : Visibility.Collapsed;
        }

        protected override async void OnFilesChanged()
        {
            if (_info == null) return;
            _pageCount = 0;
            _info.Text = "";
            if (Files.Count == 0) return;
            string file = Files[0];
            try
            {
                _pageCount = await Task.Run(() => PdfEngine.PageCount(file));
                _info.Text = $"This PDF has {Plural(_pageCount, "page")}.";
            }
            catch (Exception ex)
            {
                _info.Text = FileHelpers.Friendly(ex);
            }
        }

        public override void ApplyPreset(Dictionary<string, string> s)
        {
            _mode.Value = Get(s, "mode", "each");
            _ranges.Text = Get(s, "ranges", _ranges.Text);
            _every.Text = Get(s, "every", _every.Text);
            UpdateMode();
        }

        protected override string Validate()
        {
            if (_pageCount == 0) return "Couldn't read this PDF.";
            if (_mode.Value == "ranges" && PdfEngine.ParseRanges(_ranges.Text, _pageCount) == null)
                return $"Check the ranges — pages go from 1 to {_pageCount}.";
            return null;
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string file = Files[0];
            List<List<int>> groups;
            string summary;
            switch (_mode.Value)
            {
                case "ranges":
                    groups = PdfEngine.ParseRanges(_ranges.Text, _pageCount);
                    summary = $"You split a PDF into ranges {_ranges.Text.Trim()}.";
                    break;
                case "every":
                    int n = Ui.ReadInt(_every, 2, 1, 10000);
                    groups = Enumerable.Range(0, (_pageCount + n - 1) / n)
                        .Select(g => Enumerable.Range(g * n, Math.Min(n, _pageCount - g * n)).ToList()).ToList();
                    summary = $"You split a PDF every {Plural(n, "page")}.";
                    break;
                default:
                    groups = Enumerable.Range(0, _pageCount).Select(p => new List<int> { p }).ToList();
                    summary = "You split a PDF into single pages.";
                    break;
            }
            List<string> outputs = await Task.Run(() => PdfEngine.Split(file, groups, progress));
            ToolResult result = new ToolResult { Outputs = outputs, Message = $"Saved {Plural(outputs.Count, "file")}.", Summary = summary };
            result.Settings["mode"] = _mode.Value;
            result.Settings["ranges"] = _ranges.Text;
            result.Settings["every"] = _every.Text;
            return result;
        }
    }

    public class PdfCompressPage : ToolPage
    {
        private readonly ChipGroup _level;

        public PdfCompressPage()
            : base("pdfcompress", "Compress PDF", true, "PDFs", FileHelpers.PdfFilter, FileHelpers.IsPdf, true, false, "Compress")
        {
            Options.Children.Add(Ui.Section("Compression", "PdfAccentBrush"));
            _level = new ChipGroup(3)
                .Add("light", "Light", "best quality")
                .Add("medium", "Medium", "recommended")
                .Add("strong", "Strong", "smallest file");
            _level.Value = "medium";
            Options.Children.Add(_level);
            Options.Children.Add(Ui.Hint("Shrinks the photos inside the PDF. Text stays sharp. PDFs that are mostly text may not get much smaller."));
        }

        public override void ApplyPreset(Dictionary<string, string> s) => _level.Value = Get(s, "level", "medium");

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string level = _level.Value ?? "medium";
            int quality = level == "light" ? 80 : level == "strong" ? 45 : 65;
            int maxSide = level == "light" ? 2400 : level == "strong" ? 1200 : 1700;
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            long before = 0, after = 0;
            await Task.Run(() =>
            {
                for (int i = 0; i < files.Count; i++)
                {
                    progress.Report($"Compressing {i + 1} of {files.Count}…");
                    string output = PdfEngine.Compress(files[i], quality, maxSide, progress);
                    before += new FileInfo(files[i]).Length;
                    after += new FileInfo(output).Length;
                    result.Outputs.Add(output);
                }
            });
            double saved = before > 0 ? 100.0 * (before - after) / before : 0;
            result.Message = saved >= 1
                ? $"Done: {FileHelpers.SizeText(before)} → {FileHelpers.SizeText(after)} ({saved:0}% smaller)."
                : "Done, but this PDF couldn't be made much smaller.";
            result.Summary = $"You compressed {Plural(files.Count, "PDF")} ({level}).";
            result.Settings["level"] = level;
            return result;
        }
    }

    public class LockPage : ToolPage
    {
        private readonly ChipGroup _mode;
        private readonly PasswordBox _password;
        private readonly PasswordBox _confirm;
        private readonly StackPanel _confirmPanel;

        public LockPage()
            : base("lock", "Lock / Unlock PDF", true, "PDFs", FileHelpers.PdfFilter, FileHelpers.IsPdf, true, false, "Lock")
        {
            Options.Children.Add(Ui.Section("Action", "PdfAccentBrush"));
            _mode = new ChipGroup(2).Add("lock", "Add a password").Add("unlock", "Remove a password");
            Options.Children.Add(_mode);
            _password = new PasswordBox();
            Options.Children.Add(Ui.Field("Password", _password));
            _confirm = new PasswordBox();
            _confirmPanel = Ui.Field("Type it again", _confirm);
            Options.Children.Add(_confirmPanel);
            Options.Children.Add(Ui.Hint("To remove a password you need to know it. KUDUCK doesn't store passwords."));
            _mode.Changed += (s, e) =>
            {
                _confirmPanel.Visibility = _mode.Value == "lock" ? Visibility.Visible : Visibility.Collapsed;
                UpdateButton();
            };
        }

        protected override string ButtonText(int n)
        {
            string verb = _mode?.Value == "unlock" ? "Unlock" : "Lock";
            return n > 1 ? $"{verb} {n} files" : verb;
        }

        protected override string Validate()
        {
            if (_password.Password.Length == 0) return "Enter a password.";
            if (_mode.Value == "lock" && _password.Password != _confirm.Password) return "The two passwords don't match.";
            return null;
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            bool unlock = _mode.Value == "unlock";
            string pw = _password.Password;
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            await Task.Run(() =>
            {
                foreach (string f in files)
                    result.Outputs.Add(unlock ? PdfEngine.Unlock(f, pw) : PdfEngine.Lock(f, pw));
            });
            _password.Clear();
            _confirm.Clear();
            result.Message = unlock ? $"Unlocked {Plural(files.Count, "file")}." : $"Locked {Plural(files.Count, "file")}.";
            result.Summary = unlock ? $"You removed the password from {Plural(files.Count, "PDF")}." : $"You password-protected {Plural(files.Count, "PDF")}.";
            result.Settings["mode"] = unlock ? "unlock" : "lock";
            return result;
        }

        public override void ApplyPreset(Dictionary<string, string> s) => _mode.Value = Get(s, "mode", "lock");
    }

    /// <summary>PDF → Word and PDF → Excel (via the bundled helper).</summary>
    public class PdfConvertPage : ToolPage
    {
        private readonly bool _excel;
        private readonly PasswordBox _password;

        public PdfConvertPage(bool excel)
            : base(excel ? "pdf2excel" : "pdf2word", excel ? "PDF → Excel" : "PDF → Word", true, "PDFs",
                   FileHelpers.PdfFilter, FileHelpers.IsPdf, true, false, "Convert")
        {
            _excel = excel;
            _password = new PasswordBox();
            Options.Children.Add(Ui.Field("Password (only if the PDF has one)", _password));
            Options.Children.Add(Ui.Hint(excel
                ? "Tables become spreadsheet rows, one sheet per page. Works with PDFs that contain real text."
                : "Keeps text, headings, tables and pictures. Works with PDFs that contain real text — for scans use Scan → Text."));
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string pw = _password.Password;
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            string lastMessage = null;
            for (int i = 0; i < files.Count; i++)
            {
                progress.Report($"Converting {i + 1} of {files.Count}… (large files take a while)");
                string output = FileHelpers.OutputPath(files[i], "", _excel ? ".xlsx" : ".docx");
                lastMessage = await HelperRunner.ConvertAsync(_excel ? "pdf2excel" : "pdf2word", files[i], output, pw);
                result.Outputs.Add(output);
            }
            result.Message = files.Count == 1 ? lastMessage : $"Converted {files.Count} files.";
            result.Summary = $"You converted {Plural(files.Count, "PDF")} to {(_excel ? "Excel" : "Word")}.";
            return result;
        }
    }

    public class WordToPdfPage : ToolPage
    {
        public WordToPdfPage()
            : base("word2pdf", "Word → PDF", true, "Word files", FileHelpers.WordFilter, FileHelpers.IsWord, true, false, "Convert to PDF")
        {
            Options.Children.Add(Ui.Hint(WordConverter.WordInstalled
                ? "Uses Microsoft Word on this PC, so the PDF looks exactly like your document."
                : WordConverter.FindLibreOffice() != null
                    ? "Uses LibreOffice on this PC."
                    : "Needs Microsoft Word or the free LibreOffice installed on this PC."));
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            for (int i = 0; i < files.Count; i++)
            {
                progress.Report($"Converting {i + 1} of {files.Count}…");
                result.Outputs.Add(await WordConverter.ConvertAsync(files[i]));
            }
            result.Message = $"Saved {Plural(result.Outputs.Count, "PDF")}.";
            result.Summary = $"You turned {Plural(files.Count, "Word file")} into PDF.";
            return result;
        }
    }

    public class OcrPage : ToolPage
    {
        private readonly PasswordBox _password;

        public OcrPage()
            : base("ocr", "Scan → Text", true, "scans (images or PDFs)",
                   "Scans|*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp|All files|*.*",
                   p => FileHelpers.IsPdf(p) || FileHelpers.IsImage(p), true, false, "Read text")
        {
            _password = new PasswordBox();
            Options.Children.Add(Ui.Field("PDF password (only if it has one)", _password));
            Options.Children.Add(Ui.Hint("Reads the text from scanned pages and photos of documents and saves it as a .txt file. " +
                                         "Uses the text recognition built into Windows, in your Windows display languages."));
        }

        protected override async Task<ToolResult> RunAsync(IProgress<string> progress)
        {
            string pw = _password.Password;
            List<string> files = new List<string>(Files);
            ToolResult result = new ToolResult();
            for (int i = 0; i < files.Count; i++)
            {
                progress.Report($"Reading {i + 1} of {files.Count}…");
                string f = files[i];
                string text = FileHelpers.IsPdf(f)
                    ? await Task.Run(() => WinRt.RecognizePdfAsync(f, pw, progress))
                    : await Task.Run(() => WinRt.RecognizeImageAsync(f));
                string output = FileHelpers.OutputPath(f, "_text", ".txt");
                File.WriteAllText(output, text, new System.Text.UTF8Encoding(true));
                result.Outputs.Add(output);
            }
            result.Message = $"Saved {Plural(result.Outputs.Count, "text file")}.";
            result.Summary = $"You read the text from {Plural(files.Count, "scan")}.";
            return result;
        }
    }
}
